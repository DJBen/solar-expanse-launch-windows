using BepInEx;
using HarmonyLib;

namespace SolarExpanseLaunchWindows
{
    [BepInPlugin("com.stockmaj.solar-expanse-launch-windows", "Solar Expanse Launch Windows", "1.2.5")]
    public class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }
        internal static string Location { get; private set; }

        void Awake()
        {
            Log = base.Logger;
            Location = Info.Location;
            Log.LogInfo("Solar Expanse Launch Windows loaded");
            new Harmony("com.stockmaj.solar-expanse-launch-windows").PatchAll();
        }
    }
}
