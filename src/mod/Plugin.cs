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

        void Awake()
        {
            Log = base.Logger;
            Location = Info.Location;
            CfgShowDv = Config.Bind("UI", "ShowDeltaV", false,
                "Show the Δv column in the launch windows table.");
            CfgShowNextWindow = Config.Bind("UI", "ShowNextWindow", false,
                "Show (and compute) the second, next-synodic transfer window row per destination. Off is faster.");
            Log.LogInfo("Solar Expanse Launch Windows loaded");
            new Harmony("com.stockmaj.solar-expanse-launch-windows").PatchAll();
        }
    }
}
