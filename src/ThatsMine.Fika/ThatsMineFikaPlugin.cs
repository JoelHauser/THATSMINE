using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using Fika.Core.Main.Components;
using Fika.Core.Main.Players;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;

namespace ThatsMine.Fika
{
    /// <summary>
    /// Makes THATSMINE work in a Fika raid. Only needed with Fika; without it this plugin is
    /// skipped by BepInEx.
    ///
    /// AI protection: in Fika the host runs every bot, so the host is the only machine whose
    /// block on a bot's move counts. When a client opens one of its kills, it tells the host
    /// which body; the host protects that body until the client closes it, dies, or drops out.
    /// On the client itself THATSMINE only reports, never refuses: the host already decided
    /// every bot move it sees.
    ///
    /// Own-kills rule: the host chooses it in F12 and tells each client. Every machine then
    /// stops only its own player from looting another player's kill.
    ///
    /// Fika throws on a packet type nobody registered, and the throw drops the rest of that
    /// frame's network events. So a client sends only to the host (which must have this
    /// plugin), and the host sends only to clients that said hello first.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, ThatsMinePlugin.PluginVersion)]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(ThatsMinePlugin.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class ThatsMineFikaPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.thatsmine.fika";
        public const string PluginName = "THATSMINE Fika";

        private static ConfigEntry<bool> _ownKillsOnly;

        /// <summary>Host only: clients in this raid that have THATSMINE and can take our packets.</summary>
        private static readonly List<NetPeer> HelloPeers = new List<NetPeer>();

        /// <summary>Who the last open was sent for, so the close names the same player.</summary>
        private static string _openLooterId;

        private void Awake()
        {
            _ownKillsOnly = Config.Bind(
                "1. Co-op",
                "Players can only loot their own kills",
                false,
                "Set by the host; on everyone else this setting is ignored and the host's is used. "
                + "On: a body killed by another player can be opened but not looted, until its killer "
                + "dies or extracts. Can be changed during a raid.");
            _ownKillsOnly.SettingChanged += (sender, args) => OnHostRuleChanged();

            BodyProtection.IsActiveHuman = IsActiveHuman;
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkManagerCreated);
            FikaEventDispatcher.SubscribeEvent<FikaRaidStartedEvent>(OnRaidStarted);
            BodyProtection.LocalOpened += OnLocalOpened;
            BodyProtection.LocalClosed += OnLocalClosed;
            Logger.LogInfo(PluginName + " " + ThatsMinePlugin.PluginVersion + " loaded.");
        }

        /// <summary>Fires once per raid, on the host and on each client.</summary>
        private static void OnNetworkManagerCreated(FikaNetworkManagerCreatedEvent e)
        {
            HelloPeers.Clear();
            _openLooterId = null;
            BodyProtection.EnforceLocally = !FikaBackendUtils.IsClient;

            if (FikaBackendUtils.IsServer)
            {
                BodyProtection.OwnKillsOnly = _ownKillsOnly.Value;
                e.Manager.RegisterPacket<BodyLootPacket, NetPeer>(OnBodyLootFromClient);
                e.Manager.RegisterPacket<HelloPacket, NetPeer>(OnHelloFromClient);
            }
            else
            {
                // Off until the host says otherwise; a host without THATSMINE never will.
                BodyProtection.OwnKillsOnly = false;
                e.Manager.RegisterPacket<RulesPacket>(OnRulesFromHost);
            }
        }

        /// <summary>By raid start the client is connected, so the hello reaches the host.</summary>
        private static void OnRaidStarted(FikaRaidStartedEvent e)
        {
            if (FikaBackendUtils.IsClient)
            {
                Send(new HelloPacket());
            }
        }

        // ---- AI protection: client -> host ----------------------------------------------

        private static void OnLocalOpened(Player dead, Player looter)
        {
            _openLooterId = looter.ProfileId;
            Send(new BodyLootPacket { Opened = true, LooterProfileId = looter.ProfileId, BodyProfileId = dead.ProfileId });
        }

        private static void OnLocalClosed()
        {
            if (_openLooterId == null)
            {
                return;
            }
            Send(new BodyLootPacket { Opened = false, LooterProfileId = _openLooterId, BodyProfileId = "" });
            _openLooterId = null;
        }

        /// <summary>
        /// Host only. Fika reuses one packet instance for every arrival, so its fields are read
        /// here and not kept.
        /// </summary>
        private static void OnBodyLootFromClient(BodyLootPacket packet, NetPeer peer)
        {
            try
            {
                if (packet.Opened)
                {
                    BodyProtection.SetRemote(packet.LooterProfileId, packet.BodyProfileId,
                        () => peer.ConnectionState == ConnectionState.Connected);
                }
                else
                {
                    BodyProtection.ClearRemote(packet.LooterProfileId);
                }
            }
            catch (Exception e)
            {
                ThatsMinePlugin.Log.LogError("Could not handle a body protection message: " + e);
            }
        }

        // ---- own-kills rule: host -> clients that said hello ----------------------------

        private static void OnHelloFromClient(HelloPacket packet, NetPeer peer)
        {
            if (!HelloPeers.Contains(peer))
            {
                HelloPeers.Add(peer);
            }
            SendRules(peer);
        }

        private static void OnHostRuleChanged()
        {
            if (!FikaBackendUtils.IsServer || !Singleton<IFikaNetworkManager>.Instantiated)
            {
                return;
            }

            BodyProtection.OwnKillsOnly = _ownKillsOnly.Value;
            ThatsMinePlugin.Log.LogInfo("Own-kills-only looting is now " + (_ownKillsOnly.Value ? "on" : "off") + ".");
            foreach (NetPeer peer in HelloPeers)
            {
                if (peer.ConnectionState == ConnectionState.Connected)
                {
                    SendRules(peer);
                }
            }
        }

        private static void SendRules(NetPeer peer)
        {
            try
            {
                var packet = new RulesPacket { OwnKillsOnly = _ownKillsOnly.Value };
                Singleton<IFikaNetworkManager>.Instance.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
            }
            catch (Exception e)
            {
                ThatsMinePlugin.Log.LogError("Could not send the loot rules to a player: " + e);
            }
        }

        private static void OnRulesFromHost(RulesPacket packet)
        {
            BodyProtection.OwnKillsOnly = packet.OwnKillsOnly;
            ThatsMinePlugin.Log.LogInfo("Host set own-kills-only looting " + (packet.OwnKillsOnly ? "on" : "off") + ".");
        }

        // ---- shared ---------------------------------------------------------------------

        private static void Send<T>(T packet) where T : class, INetSerializable
        {
            if (!FikaBackendUtils.IsClient)
            {
                return;
            }

            try
            {
                Singleton<IFikaNetworkManager>.Instance?.SendData(ref packet, DeliveryMethod.ReliableOrdered);
            }
            catch (Exception e)
            {
                ThatsMinePlugin.Log.LogError("Could not send a message to the host: " + e);
            }
        }

        /// <summary>
        /// A human in this raid, alive and not extracted. Fika keeps both lists on every machine
        /// (extracts are synced by its ClientExtractPacket). A player who disconnects is not
        /// removed from either, so they still count.
        /// </summary>
        private static bool IsActiveHuman(IPlayer player)
        {
            CoopHandler coopHandler = Singleton<IFikaNetworkManager>.Instance?.CoopHandler;
            if (coopHandler == null || player == null)
            {
                return false;
            }

            foreach (FikaPlayer human in coopHandler.HumanPlayers)
            {
                if (human != null && human.ProfileId == player.ProfileId)
                {
                    return human.HealthController.IsAlive && !coopHandler.ExtractedPlayers.Contains(human.NetId);
                }
            }
            return false;
        }
    }
}
