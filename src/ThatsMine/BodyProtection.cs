using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ThatsMine
{
    /// <summary>
    /// The bodies currently protected, and the rule for what counts as taking from them.
    ///
    /// The local session is the body this player has open. It starts when the loot screen opens
    /// on a body and ends through the screen's own close callback, which the game calls however
    /// the screen closes: Tab, Escape, death, or the early exit when another screen is up.
    /// Opening any loot screen also ends the previous session first.
    ///
    /// Remote sessions exist only on a Fika host: one per other player, for the body that player
    /// has open, added and removed by ThatsMine.Fika. The host runs every bot, so the host is the
    /// only place a bot's move can be refused; on a Fika client <see cref="EnforceLocally"/> is
    /// off and the local session is only reported to the host.
    ///
    /// Everything is held by reference to this raid's objects, so nothing can carry over into a
    /// later raid: a stale body would match no item there. Each session is also dropped as soon
    /// as it is found to belong to a finished raid, a dead looter, or a disconnected player.
    /// </summary>
    internal static class BodyProtection
    {
        /// <summary>Set by Patches.Apply; the field is protected in Player.</summary>
        internal static AccessTools.FieldRef<Player, IPlayer> LastAggressorRef;

        /// <summary>
        /// False on a Fika client: the host runs the bots and decides their moves, so refusing a
        /// move here would only make this client disagree with the host about the body.
        /// </summary>
        internal static bool EnforceLocally = true;

        /// <summary>
        /// Co-op rule, set by ThatsMine.Fika from the host's choice: a body killed by another
        /// player who is still in the raid can be opened but not looted by this player.
        /// </summary>
        internal static bool OwnKillsOnly;

        /// <summary>
        /// Set by ThatsMine.Fika: true if the given player is a human in this raid who is alive
        /// and has not extracted. The game's own AI flag cannot answer this on a Fika client,
        /// where every bot is a remote copy with the flag unset.
        /// </summary>
        internal static Func<IPlayer, bool> IsActiveHuman;

        /// <summary>One-entry cache for the body this player is looting; CanModifyItem asks a lot.</summary>
        private static InventoryEquipment _ownerCacheBody;
        private static Player _ownerCachePlayer;

        /// <summary>Raised when this player starts protecting a body: (body owner, looter).</summary>
        internal static event Action<Player, Player> LocalOpened;

        /// <summary>Raised when this player's protection ends, however it ends.</summary>
        internal static event Action LocalClosed;

        /// <summary>Bumped per session, so a stale close callback cannot end a newer one.</summary>
        private static int _session;

        private static Session _local;

        /// <summary>Remote sessions by the looter's profile id. Host only.</summary>
        private static readonly Dictionary<string, Session> Remotes = new Dictionary<string, Session>();

        /// <summary>Items already logged as blocked, so a retry loop logs once per session.</summary>
        private static readonly HashSet<Item> LoggedBlocks = new HashSet<Item>();

        internal static bool Active => (_local != null && EnforceLocally) || Remotes.Count > 0;

        /// <summary>
        /// Called as a loot screen opens. Starts protecting <paramref name="loot"/> if it is a
        /// body that qualifies, and wraps <paramref name="callback"/> to stop when it closes.
        /// Never blocks the screen from opening.
        /// </summary>
        internal static void OnLootOpened(Player looter, CompoundItem loot, ref Action callback)
        {
            EndLocal();

            // A body's loot is its whole equipment. Bags, crates and the armor-plate view never are.
            if (looter == null || !(loot is InventoryEquipment equipment))
            {
                return;
            }

            GameWorld world = Singleton<GameWorld>.Instance;
            Player dead = world != null ? FindOwner(world, equipment) : null;
            if (dead == null)
            {
                return;
            }

            if (LockedFor(dead, looter, out string killerName))
            {
                NotificationManager.DisplayWarningNotification(
                    "Not your kill. Only " + killerName + " can loot this body while they are in the raid.");
                return;
            }

            if (!ThatsMinePlugin.Enabled.Value)
            {
                return;
            }

            if (ThatsMinePlugin.OnlyAsScav.Value && looter.Side != EPlayerSide.Savage)
            {
                return;
            }

            if (ThatsMinePlugin.OnlyMyKills.Value && !KilledBy(dead, looter))
            {
                ThatsMinePlugin.Debug("Opened " + Describe(dead) + ", not your kill: not protected.");
                return;
            }

            int session = ++_session;
            _local = new Session(equipment, Describe(dead), looter, world, null);
            ThatsMinePlugin.Log.LogInfo("Protecting " + _local.Name + " from AI looting while you loot it.");
            LocalOpened?.Invoke(dead, looter);

            Action original = callback;
            callback = () =>
            {
                EndLocal(session);
                original?.Invoke();
            };
        }

        /// <summary>
        /// Host only: protects the body of <paramref name="bodyProfileId"/> for another player,
        /// replacing whatever that player had protected before. <paramref name="connected"/> is
        /// polled so a player who drops out does not leave the body locked.
        /// </summary>
        internal static void SetRemote(string looterProfileId, string bodyProfileId, Func<bool> connected)
        {
            ClearRemote(looterProfileId);

            GameWorld world = Singleton<GameWorld>.Instance;
            Player looter = world?.GetEverExistedPlayerByID(looterProfileId);
            Player dead = world?.GetEverExistedPlayerByID(bodyProfileId);
            if (looter == null || dead == null || dead.Equipment == null)
            {
                ThatsMinePlugin.Debug("Ignored a protect request for a body or player this game does not know.");
                return;
            }

            var remote = new Session(dead.Equipment, Describe(dead), looter, world, connected);
            Remotes[looterProfileId] = remote;
            ThatsMinePlugin.Log.LogInfo("Protecting " + remote.Name + " from AI looting while "
                                        + Describe(looter) + " loots it.");
        }

        /// <summary>Host only: ends another player's protection, if they have one.</summary>
        internal static void ClearRemote(string looterProfileId)
        {
            if (looterProfileId != null && Remotes.TryGetValue(looterProfileId, out Session remote))
            {
                Remotes.Remove(looterProfileId);
                ThatsMinePlugin.Log.LogInfo("Stopped protecting " + remote.Name + " for " + Describe(remote.Looter) + ".");
            }
        }

        /// <summary>
        /// True if <paramref name="controller"/> belongs to an AI and <paramref name="item"/> is
        /// on a protected body. Ordered so the common case (nothing protected) costs one compare.
        /// </summary>
        internal static bool ShouldBlock(Item item, ItemController controller)
        {
            if (item == null || controller == null)
            {
                return false;
            }

            Item root = item.GetRootItem();
            Session session = Find(root);
            if (session == null || controller == session.LooterController)
            {
                return false;
            }

            // Person controllers carry their owner's profile id.
            Player actor = session.World.GetEverExistedPlayerByID(controller.ID);
            if (actor == null || !actor.IsAI)
            {
                return false;
            }

            if (LoggedBlocks.Add(item))
            {
                ThatsMinePlugin.Debug("Blocked " + Describe(actor) + " from taking "
                                      + item.LocalizedName() + " off " + session.Name + ".");
            }
            return true;
        }

        /// <summary>
        /// Co-op rule: true if <paramref name="controller"/> is this machine's own player and
        /// <paramref name="item"/> is on a body another player killed and still has the right
        /// to. Each machine only ever stops its own player, so no machine overrules another's
        /// move after the fact.
        /// </summary>
        internal static bool LockedForMe(Item item, ItemController controller, out string killerName)
        {
            killerName = null;
            Player me = Singleton<GameWorld>.Instance?.MainPlayer;
            if (me == null || item == null || controller != me.InventoryController)
            {
                return false;
            }

            // Moves inside your own gear are by far the most common; they leave here.
            if (!(item.GetRootItem() is InventoryEquipment body) || body == me.Equipment)
            {
                return false;
            }

            if (body != _ownerCacheBody)
            {
                _ownerCacheBody = body;
                _ownerCachePlayer = FindOwner(Singleton<GameWorld>.Instance, body);
            }
            return _ownerCachePlayer != null && LockedFor(_ownerCachePlayer, me, out killerName);
        }

        private static bool LockedFor(Player dead, Player looter, out string killerName)
        {
            killerName = null;
            if (!OwnKillsOnly || IsActiveHuman == null || dead.HealthController == null || dead.HealthController.IsAlive)
            {
                return false;
            }

            IPlayer killer = LastAggressorRef(dead);
            if (killer == null || killer.ProfileId == looter.ProfileId || !IsActiveHuman(killer))
            {
                return false;
            }

            killerName = killer.Profile?.Nickname ?? "another player";
            return true;
        }

        /// <summary>True if <paramref name="deadPlayer"/>'s body is a protected one.</summary>
        internal static bool IsProtected(Player deadPlayer)
        {
            return deadPlayer != null && Find(deadPlayer.Equipment) != null;
        }

        /// <summary>
        /// The valid session protecting <paramref name="root"/>, or null. Sessions found to be
        /// stale on the way are dropped.
        /// </summary>
        private static Session Find(Item root)
        {
            if (root == null)
            {
                return null;
            }

            if (EnforceLocally && _local != null && _local.Body == root)
            {
                if (_local.StillValid())
                {
                    return _local;
                }
                EndLocal();
            }

            if (Remotes.Count == 0)
            {
                return null;
            }

            foreach (KeyValuePair<string, Session> pair in Remotes)
            {
                if (pair.Value.Body != root)
                {
                    continue;
                }
                if (pair.Value.StillValid())
                {
                    return pair.Value;
                }
                ClearRemote(pair.Key);
                return null;
            }
            return null;
        }

        private static void EndLocal(int session)
        {
            if (session == _session)
            {
                EndLocal();
            }
        }

        private static void EndLocal()
        {
            if (_local == null)
            {
                return;
            }

            ThatsMinePlugin.Log.LogInfo("Stopped protecting " + _local.Name + ".");
            _local = null;
            LoggedBlocks.Clear();
            LocalClosed?.Invoke();
        }

        /// <summary>
        /// The kill is credited the way the game credits it: to the body's last aggressor. In a
        /// Fika raid that is also what the host sends every client when a player dies, so it
        /// agrees on every machine; LastDamageInfo (behind KillerId) is not sent.
        /// </summary>
        private static bool KilledBy(Player dead, Player looter)
        {
            IPlayer aggressor = LastAggressorRef(dead);
            if (aggressor != null)
            {
                return aggressor.ProfileId == looter.ProfileId;
            }
            return dead.KillerId == looter.ProfileId;
        }

        /// <summary>
        /// The player whose equipment this is. A corpse is built from its player's own
        /// <c>Equipment</c> object (<c>Player.CreateCorpse</c>), so a reference match is exact.
        /// Runs once per loot screen opened, over the few dozen players a raid has.
        /// </summary>
        private static Player FindOwner(GameWorld world, InventoryEquipment equipment)
        {
            foreach (Player player in world.AllPlayersEverExisted)
            {
                if (player != null && player.Equipment == equipment)
                {
                    return player;
                }
            }
            return null;
        }

        private static string Describe(Player player)
        {
            return (player.Profile?.Nickname ?? "?") + " (" + player.Side + ")";
        }

        private sealed class Session
        {
            internal readonly InventoryEquipment Body;
            internal readonly string Name;
            internal readonly Player Looter;
            internal readonly ItemController LooterController;
            internal readonly GameWorld World;
            private readonly Func<bool> _connected;

            internal Session(InventoryEquipment body, string name, Player looter, GameWorld world, Func<bool> connected)
            {
                Body = body;
                Name = name;
                Looter = looter;
                LooterController = looter.InventoryController;
                World = world;
                _connected = connected;
            }

            internal bool StillValid()
            {
                return Singleton<GameWorld>.Instance == World
                       && Looter != null
                       && Looter.HealthController != null
                       && Looter.HealthController.IsAlive
                       && (_connected == null || _connected());
            }
        }
    }
}
