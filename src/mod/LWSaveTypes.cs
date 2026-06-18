using System;
using System.Collections.Generic;

namespace SolarExpanseLaunchWindows
{
    [Serializable]
    internal class LWSaveData
    {
        public int          version          = 3;
        public string       originId         = "";
        public string       selectedCraftName = "";
        public List<string> destIds     = new List<string>(); // v1 compat
        public List<LWOriginDestsSave>  originDests   = new List<LWOriginDestsSave>();
        public List<LWAlarmSave>        alarms        = new List<LWAlarmSave>();
        public List<LWDestCacheSave>    windowCache   = new List<LWDestCacheSave>();
        public List<LWOriginCacheSave>  originCaches  = new List<LWOriginCacheSave>();
    }

    [Serializable]
    internal class LWOriginCacheSave
    {
        public string                originId = "";
        public List<LWDestCacheSave> cache    = new List<LWDestCacheSave>();
    }

    [Serializable]
    public class LWOriginDestsSave
    {
        public string       originId = "";
        public List<string> destIds  = new List<string>();
    }

    [Serializable]
    internal class LWAlarmSave
    {
        public string originId  = "";
        public string destId    = "";
        public int    year;
        public int    month;
        public bool   isFastest = false;
    }

    [Serializable]
    internal class LWWindowSave
    {
        public double dep; // DepartureEpoch
        public double arr; // ArrivalEpoch
        public double dv;  // DeltaVKmS
    }

    [Serializable]
    internal class LWDestCacheSave
    {
        public string       destId = "";
        public LWWindowSave opt1;  // null = no window found
        public LWWindowSave fst1;
        public LWWindowSave opt2;
        public LWWindowSave fst2;
    }

    internal static class LWSaveConvert
    {
        internal static LWWindowSave ToSave(LaunchWindow w) =>
            new LWWindowSave { dep = w.DepartureEpoch, arr = w.ArrivalEpoch, dv = w.DeltaVKmS };

        internal static LaunchWindow FromSave(LWWindowSave s) =>
            new LaunchWindow(s.dep, s.arr, s.dv);
    }
}
