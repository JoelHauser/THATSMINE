using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ThatsMine
{
    /// <summary>
    /// While you have a body's loot screen open, AI cannot take anything off that body.
    ///
    /// Protection starts when the loot screen for the body opens and ends when it closes, and
    /// covers only that one body. See <see cref="BodyProtection"/> for the rule and
    /// <see cref="Patches"/> for where it is enforced. In a Fika raid, ThatsMine.Fika carries
    /// each player's open body to the host, which runs the bots.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ThatsMinePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.thatsmine";
        public const string PluginName = "THATSMINE";

        /// <summary>Must match the csproj's Version. Two places, and they have to agree.</summary>
        public const string PluginVersion = "0.2.1";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> OnlyAsScav;
        internal static ConfigEntry<bool> OnlyMyKills;
        internal static ConfigEntry<bool> DebugLogging;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind(
                "1. General",
                "Protect the body you are looting",
                true,
                "While a body's loot screen is open, AI cannot take items off that body. Takes "
                + "effect the next time you open a body.");

            OnlyAsScav = Config.Bind(
                "1. General",
                "Only when playing as a scav",
                true,
                "Protect bodies only during scav raids. Off: also during PMC raids.");

            OnlyMyKills = Config.Bind(
                "1. General",
                "Only bodies you killed",
                true,
                "Protect only bodies whose killing shot was yours. Off: any body you open.");

            DebugLogging = Config.Bind(
                "2. Debug",
                "Log blocked AI moves",
                false,
                "Write each item an AI was stopped from taking to LogOutput.log, once per item "
                + "per loot screen.");

            Harmony harmony = new Harmony(PluginGuid);
            try
            {
                Patches.Apply(harmony);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Logger.LogError(PluginName + " " + PluginVersion
                                + " could not apply its patches, so the game is unmodified: " + e);
                return;
            }

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        internal static void Debug(string message)
        {
            if (DebugLogging != null && DebugLogging.Value)
            {
                Log.LogInfo(message);
            }
        }
    }
}
