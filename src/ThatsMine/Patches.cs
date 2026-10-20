using System;
using System.Reflection;
using Diz.LanguageExtensions;
using EFT;
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
    /// 3. <see cref="BotDeadBodyWork.UpdateWorks"/> is paused for a vanilla bot whose body is the
    ///    protected one. Without this it would step through its whole loot list with every move
    ///    refused, then give up on the body for good. Paused, it waits beside the body and carries
    ///    on where it was once you close the screen.
    /// </summary>
    internal static class Patches
    {
        public static void Apply(Harmony harmony)
        {
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
            if (!__result || !BodyProtection.Active)
            {
                return;
            }

            if (BodyProtection.ShouldBlock(item, controller))
            {
                __result = false;
                error = new BodyInUseError(item);
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
}
