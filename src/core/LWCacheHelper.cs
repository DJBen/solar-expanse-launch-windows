using System;
using System.Collections.Generic;
using System.Linq;

namespace SolarExpanseLaunchWindows
{
    internal static class LWCacheHelper
    {
        internal static (
            Dictionary<string, (LaunchWindow?, LaunchWindow?, LaunchWindow?, LaunchWindow?)> cache,
            HashSet<string> needsOpt2Recalc
        ) PromoteWindowCache(
            IEnumerable<LWDestCacheSave> entries,
            IEnumerable<string> validBodyIds,
            double physNow)
        {
            var cache         = new Dictionary<string, (LaunchWindow?, LaunchWindow?, LaunchWindow?, LaunchWindow?)>();
            var needsOpt2     = new HashSet<string>();
            var allIds        = new HashSet<string>(validBodyIds);

            foreach (var e in entries ?? Enumerable.Empty<LWDestCacheSave>())
            {
                if (string.IsNullOrEmpty(e.destId) || !allIds.Contains(e.destId)) continue;
                if (e.opt1 != null && e.opt1.dep > physNow)
                {
                    cache[e.destId] = (
                        (LaunchWindow?)LWSaveConvert.FromSave(e.opt1),
                        e.fst1 != null ? (LaunchWindow?)LWSaveConvert.FromSave(e.fst1) : null,
                        e.opt2 != null ? (LaunchWindow?)LWSaveConvert.FromSave(e.opt2) : null,
                        e.fst2 != null ? (LaunchWindow?)LWSaveConvert.FromSave(e.fst2) : null
                    );
                }
                else if (e.opt2 != null && e.opt2.dep > physNow)
                {
                    // opt1 stale, opt2 still valid — promote; schedule one scan for new opt2
                    cache[e.destId] = (
                        (LaunchWindow?)LWSaveConvert.FromSave(e.opt2),
                        e.fst2 != null ? (LaunchWindow?)LWSaveConvert.FromSave(e.fst2) : null,
                        null,
                        null
                    );
                    needsOpt2.Add(e.destId);
                }
                // else: both stale — absent from cache, full recalc will run
            }

            return (cache, needsOpt2);
        }

        internal static List<AlarmKey> GetAlarmsToFire(
            IEnumerable<AlarmKey> alarms, string currentOriginId, DateTime now)
        {
            var result = new List<AlarmKey>();
            foreach (var key in alarms)
                if (key.OriginId == currentOriginId && key.Year == now.Year && key.Month == now.Month)
                    result.Add(key);
            return result;
        }

        internal static string StripSaveExtension(string name)
        {
            foreach (var ext in new[] { ".json.gz", ".info.gz", ".json", ".gz" })
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(0, name.Length - ext.Length);
            return name;
        }
    }
}
