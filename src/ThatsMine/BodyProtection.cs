using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace ThatsMine
{
    /// <summary>
    /// The one body currently protected, and the rule for what counts as taking from it.
    ///
    /// A session starts when the loot screen opens on a body and ends through the screen's own
    /// close callback, which the game calls however the screen closes: Tab, Escape, death, or
    /// the early exit when another screen is up. Opening any loot screen also ends the previous
    /// session first, so there is never more than one protected body.
    ///
    /// Everything is held by reference to this raid's objects, so nothing can carry over into a
    /// later raid: a stale body would match no item there. <see cref="StillValid"/> also drops
    /// the session if the raid it belongs to is gone or the looter is dead.
    /// </summary>
    internal static class BodyProtection
    {
        /// <summary>Bumped per session, so a stale close callback cannot end a newer one.</summary>
        private static int _session;

        /// <summary>The body's root item: the dead player's equipment, which the corpse holds.</summary>
        private static InventoryEquipment _body;

        private static string _bodyName;
        private static Player _looter;
        private static ItemController _looterController;
        private static GameWorld _world;

        /// <summary>Items already logged as blocked this session, so a retry loop logs once.</summary>
        private static readonly HashSet<Item> LoggedBlocks = new HashSet<Item>();

        internal static bool Active => _body != null;

        /// <summary>
        /// Called as a loot screen opens. Starts protecting <paramref name="loot"/> if it is a
        /// body that qualifies, and wraps <paramref name="callback"/> to stop when it closes.
        /// Never blocks the screen from opening.
        /// </summary>
        internal static void OnLootOpened(Player looter, CompoundItem loot, ref Action callback)
        {
            End();

            // A body's loot is its whole equipment. Bags, crates and the armor-plate view never are.
            if (!ThatsMinePlugin.Enabled.Value || looter == null || !(loot is InventoryEquipment equipment))
            {
                return;
            }

            if (ThatsMinePlugin.OnlyAsScav.Value && looter.Side != EPlayerSide.Savage)
            {
                return;
            }

            GameWorld world = Singleton<GameWorld>.Instance;
            Player dead = world != null ? FindOwner(world, equipment) : null;
            if (dead == null)
            {
                return;
            }

            if (ThatsMinePlugin.OnlyMyKills.Value && dead.KillerId != looter.ProfileId)
            {
                ThatsMinePlugin.Debug("Opened " + Describe(dead) + ", not your kill: not protected.");
                return;
            }

            int session = ++_session;
            _body = equipment;
            _bodyName = Describe(dead);
            _looter = looter;
            _looterController = looter.InventoryController;
            _world = world;
            ThatsMinePlugin.Log.LogInfo("Protecting " + _bodyName + " from AI looting while you loot it.");

            Action original = callback;
            callback = () =>
            {
                End(session);
                original?.Invoke();
            };
        }

        /// <summary>
        /// True if <paramref name="controller"/> belongs to an AI and <paramref name="item"/> is
        /// on the protected body. Ordered so the common cases (no session, or the looter's own
        /// moves) cost one or two reference compares.
        /// </summary>
        internal static bool ShouldBlock(Item item, ItemController controller)
        {
            if (_body == null || controller == null || controller == _looterController)
            {
                return false;
            }

            if (item == null || item.GetRootItem() != _body)
            {
                return false;
            }

            if (!StillValid())
            {
                End();
                return false;
            }

            // Person controllers carry their owner's profile id.
            Player actor = _world.GetEverExistedPlayerByID(controller.ID);
            if (actor == null || !actor.IsAI)
            {
                return false;
            }

            if (LoggedBlocks.Add(item))
            {
                ThatsMinePlugin.Debug("Blocked " + Describe(actor) + " from taking "
                                      + item.LocalizedName() + " off " + _bodyName + ".");
            }
            return true;
        }

        /// <summary>True if <paramref name="deadPlayer"/>'s body is the protected one.</summary>
        internal static bool IsProtected(Player deadPlayer)
        {
            if (_body == null || deadPlayer == null || deadPlayer.Equipment != _body)
            {
                return false;
            }

            if (!StillValid())
            {
                End();
                return false;
            }
            return true;
        }

        private static bool StillValid()
        {
            return Singleton<GameWorld>.Instance == _world
                   && _looter != null
                   && _looter.HealthController != null
                   && _looter.HealthController.IsAlive;
        }

        private static void End(int session)
        {
            if (session == _session)
            {
                End();
            }
        }

        private static void End()
        {
            if (_body != null)
            {
                ThatsMinePlugin.Log.LogInfo("Stopped protecting " + _bodyName + ".");
            }
            _body = null;
            _bodyName = null;
            _looter = null;
            _looterController = null;
            _world = null;
            LoggedBlocks.Clear();
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
    }
}
