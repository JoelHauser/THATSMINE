using System;
using System.Reflection;
using Diz.LanguageExtensions;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ThatsMine
{
    /// <summary>
    /// Three hooks:
    ///
    /// 1. <see cref="EftGamePlayerOwner.ShowInventoryScreenLoot"/>, which a body's Search opens
    ///    through, starts the session and wraps its close callback to end it.
    /// 2. <see cref="ItemManipulator.CanModifyItem"/> is the game's own "may this controller take
    ///    this item from where it is" check. Move, Swap, Split, Transfer and Discard (ThrowItem)
    ///    all go through it before anything changes, for every bot looting system: vanilla
    ///    <see cref="BotDeadBodyWork"/>, ORBIT's OrbitLootHandler, and anything else that uses the
    ///    game's inventory operations. Refusing there makes the AI's move fail the normal way,
    ///    with an error it already handles. It is checked when the move is attempted, so a bot
    ///    that was already walking to or kneeling at the body when you opened it is stopped too.
    ///    The co-op "own kills only" rule refuses this player's own moves at the same place.
    /// 3. <see cref="BotDeadBodyWork.UpdateWorks"/> is paused for a vanilla bot whose body is the
    ///    protected one. Without this it would step through its whole loot list with every move
    ///    refused, then give up on the body for good. Paused, it waits beside the body and carries
    ///    on where it was once you close the screen.
    /// </summary>
    internal static class Patches
    {
        public static void Apply(Harmony harmony)
        {
            FieldInfo lastAggressor = AccessTools.Field(typeof(Player), "LastAggressor");
            if (lastAggressor == null || lastAggressor.FieldType != typeof(IPlayer))
            {
                throw new MissingFieldException("Player.LastAggressor was not found in this client.");
            }
            BodyProtection.LastAggressorRef = AccessTools.FieldRefAccess<Player, IPlayer>(lastAggressor);

            harmony.Patch(
                Require(AccessTools.DeclaredMethod(typeof(EftGamePlayerOwner), nameof(EftGamePlayerOwner.ShowInventoryScreenLoot),
                    new[] { typeof(CompoundItem), typeof(Action), typeof(bool) })),
                prefix: new HarmonyMethod(typeof(Patches), nameof(ShowInventoryScreenLootPrefix)));

            harmony.Patch(
                Require(AccessTools.DeclaredMethod(typeof(ItemManipulator), nameof(ItemManipulator.CanModifyItem),
                    new[] { typeof(Item), typeof(ItemAddress), typeof(ItemController), typeof(Error).MakeByRefType() })),
                postfix: new HarmonyMethod(typeof(Patches), nameof(CanModifyItemPostfix)));

            harmony.Patch(
                Require(AccessTools.DeclaredMethod(typeof(BotDeadBodyWork), nameof(BotDeadBodyWork.UpdateWorks), Type.EmptyTypes)),
                prefix: new HarmonyMethod(typeof(Patches), nameof(UpdateWorksPrefix)));

            // Diagnostics: every item leaving a corpse passes here, whoever moved it.
            harmony.Patch(
                Require(AccessTools.DeclaredMethod(typeof(Corpse), nameof(Corpse.RemoveLootItem), new[] { typeof(RemoveItemEventArgs) })),
                postfix: new HarmonyMethod(typeof(Patches), nameof(CorpseRemoveLootItemPostfix)));
        }

        private static MethodInfo Require(MethodInfo method)
        {
            if (method == null)
            {
                throw new MissingMethodException("A patch target was not found in this client.");
            }
            return method;
        }

        private static void ShowInventoryScreenLootPrefix(EftGamePlayerOwner __instance, CompoundItem loot, ref Action callback)
        {
            try
            {
                BodyProtection.OnLootOpened(__instance.Player, loot, ref callback);
            }
            catch (Exception e)
            {
                ThatsMinePlugin.Log.LogError("Could not start protecting this body: " + e);
            }
        }

        private static void CanModifyItemPostfix(Item item, ItemController controller, ref bool __result, ref Error error)
        {
            if (!__result)
            {
                return;
            }

            if (BodyProtection.Active && BodyProtection.ShouldBlock(item, controller))
            {
                __result = false;
                error = new BodyInUseError(item);
                return;
            }

            if (BodyProtection.OwnKillsOnly && BodyProtection.LockedForMe(item, controller, out string killerName))
            {
                __result = false;
                error = new NotYourKillError(killerName);
            }
        }

        private static void CorpseRemoveLootItemPostfix(Corpse __instance, RemoveItemEventArgs args)
        {
            try
            {
                BodyProtection.OnCorpseItemRemoved(__instance.Item, args);
            }
            catch (Exception e)
            {
                ThatsMinePlugin.Log.LogError("Could not check an item leaving a body: " + e);
            }
        }

        private static bool UpdateWorksPrefix(BotDeadBodyWork __instance)
        {
            return !BodyProtection.Active || !BodyProtection.IsProtected(__instance._activeBody?.Player);
        }
    }

    /// <summary>Why an AI's move was refused. Only ever seen by the AI's own failure handling.</summary>
    internal sealed class BodyInUseError : InventoryError
    {
        private readonly Item _item;

        public BodyInUseError(Item item)
        {
            _item = item;
        }

        public override string ToString()
        {
            return "(BodyInUseError) The player is looting this body: " + _item;
        }
    }

    /// <summary>Why this player's own move off another player's kill was refused (co-op rule).</summary>
    internal sealed class NotYourKillError : InventoryError
    {
        private readonly string _killerName;

        public NotYourKillError(string killerName)
        {
            _killerName = killerName;
        }

        public override string ToString()
        {
            return "(NotYourKillError) Only " + _killerName + " can loot this body while they are in the raid.";
        }

        public override string GetLocalizedDescription()
        {
            return "Not your kill. Only " + _killerName + " can loot this body.";
        }
    }
}
