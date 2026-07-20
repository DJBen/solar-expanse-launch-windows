using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace SolarExpanseLaunchWindows
{
    [BepInPlugin("com.stockmaj.solar-expanse-launch-windows", "Solar Expanse Launch Windows", "1.3.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }
        internal static string Location { get; private set; }

        // Display options (Options dropdown in the panel header); persisted by BepInEx.
        internal static ConfigEntry<bool> CfgShowDv;
        internal static ConfigEntry<bool> CfgShowNextWindow;
        internal static ConfigEntry<bool> CfgShowFastest;
        internal static ConfigEntry<bool> CfgShowReturn;
        internal static ConfigEntry<bool> CfgShowUndiscovered;
        internal static ConfigEntry<int>  CfgAlertDaysBefore;

        void Awake()
        {
            Log = base.Logger;
            Location = Info.Location;
            CfgShowDv = Config.Bind("UI", "ShowDeltaV", false,
                "Show the Δv column in the launch windows table.");
            CfgShowNextWindow = Config.Bind("UI", "ShowNextWindow", false,
                "Show (and compute) the second, next-synodic transfer window row per destination. Off is faster.");
            CfgShowFastest = Config.Bind("UI", "ShowFastest", false,
                "Show the Fastest (Δv-capped) transfer window section.");
            CfgShowReturn = Config.Bind("UI", "ShowReturn", true,
                "Show (and compute on demand) the Return section: the first optimal window from the destination back to the origin after arrival.");
            CfgShowUndiscovered = Config.Bind("UI", "ShowUndiscovered", false,
                "Show bodies the player has not discovered yet (greyed out) in the table, search, and presets.");
            CfgAlertDaysBefore = Config.Bind("Alerts", "AlertDaysBefore", 0,
                "Fire armed launch-window alerts this many days before departure.");
            Log.LogInfo("Solar Expanse Launch Windows loaded");
            new Harmony("com.stockmaj.solar-expanse-launch-windows").PatchAll();
        }
    }
}
