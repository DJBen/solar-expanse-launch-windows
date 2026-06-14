using System.Collections.Generic;
using NUnit.Framework;
using SolarExpanseLaunchWindows;

namespace SolarExpanseLaunchWindowsTests
{
    [TestFixture]
    internal class CachePromotionTests
    {
        private static LWDestCacheSave Entry(string id,
            double? opt1Dep = null, double? opt2Dep = null,
            double? fst1Dep = null, double? fst2Dep = null)
        {
            return new LWDestCacheSave
            {
                destId = id,
                opt1 = opt1Dep.HasValue ? new LWWindowSave { dep = opt1Dep.Value, arr = opt1Dep.Value + 0.7, dv = 3.6 } : null,
                fst1 = fst1Dep.HasValue ? new LWWindowSave { dep = fst1Dep.Value, arr = fst1Dep.Value + 0.6, dv = 4.0 } : null,
                opt2 = opt2Dep.HasValue ? new LWWindowSave { dep = opt2Dep.Value, arr = opt2Dep.Value + 0.7, dv = 3.8 } : null,
                fst2 = fst2Dep.HasValue ? new LWWindowSave { dep = fst2Dep.Value, arr = fst2Dep.Value + 0.6, dv = 4.2 } : null,
            };
        }

        private static readonly string[] ValidIds = { "mars", "venus" };
        private const double Now = 10.0;

        [Test]
        public void BothWindows_Opt1Valid_FullEntryRestored()
        {
            var entries = new[] { Entry("mars", opt1Dep: 15.0, fst1Dep: 14.0, opt2Dep: 17.0, fst2Dep: 16.5) };
            var (cache, needsRecalc) = LWCacheHelper.PromoteWindowCache(entries, ValidIds, Now);

            Assert.That(cache.ContainsKey("mars"), Is.True);
            var e = cache["mars"];
            Assert.That(e.Item1.HasValue, Is.True, "opt1 present");
            Assert.That(e.Item3.HasValue, Is.True, "opt2 present");
            Assert.That(e.Item1.Value.DepartureEpoch, Is.EqualTo(15.0));
            Assert.That(e.Item3.Value.DepartureEpoch, Is.EqualTo(17.0));
            Assert.That(needsRecalc, Does.Not.Contain("mars"));
        }

        [Test]
        public void Opt1Stale_Opt2Valid_Promoted()
        {
            var entries = new[] { Entry("mars", opt1Dep: 5.0, fst1Dep: 4.5, opt2Dep: 15.0, fst2Dep: 14.5) };
            var (cache, needsRecalc) = LWCacheHelper.PromoteWindowCache(entries, ValidIds, Now);

            Assert.That(cache.ContainsKey("mars"), Is.True);
            var e = cache["mars"];
            // opt2 becomes new opt1
            Assert.That(e.Item1.HasValue, Is.True, "promoted opt1 present");
            Assert.That(e.Item1.Value.DepartureEpoch, Is.EqualTo(15.0), "opt2 promoted to opt1");
            // fst2 becomes new fst1
            Assert.That(e.Item2.HasValue, Is.True, "promoted fst1 present");
            Assert.That(e.Item2.Value.DepartureEpoch, Is.EqualTo(14.5), "fst2 promoted to fst1");
            // opt2/fst2 slots are empty (awaiting recalc)
            Assert.That(e.Item3.HasValue, Is.False, "opt2 cleared for recalc");
            Assert.That(e.Item4.HasValue, Is.False, "fst2 cleared for recalc");
            Assert.That(needsRecalc, Does.Contain("mars"));
        }

        [Test]
        public void BothStale_EntryAbsentFromCache()
        {
            var entries = new[] { Entry("mars", opt1Dep: 5.0, opt2Dep: 8.0) };
            var (cache, needsRecalc) = LWCacheHelper.PromoteWindowCache(entries, ValidIds, Now);

            Assert.That(cache.ContainsKey("mars"), Is.False);
            Assert.That(needsRecalc, Does.Not.Contain("mars"));
        }

        [Test]
        public void Opt1Stale_NoOpt2_EntryAbsent()
        {
            var entries = new[] { Entry("mars", opt1Dep: 5.0) };
            var (cache, _) = LWCacheHelper.PromoteWindowCache(entries, ValidIds, Now);
            Assert.That(cache.ContainsKey("mars"), Is.False);
        }

        [Test]
        public void UnknownBodyId_Skipped()
        {
            var entries = new[] { Entry("pluto", opt1Dep: 15.0) };
            var (cache, _) = LWCacheHelper.PromoteWindowCache(entries, ValidIds, Now);
            Assert.That(cache.ContainsKey("pluto"), Is.False);
        }

        [Test]
        public void NullEntries_ReturnsEmpty()
        {
            var (cache, needsRecalc) = LWCacheHelper.PromoteWindowCache(null, ValidIds, Now);
            Assert.That(cache.Count, Is.EqualTo(0));
            Assert.That(needsRecalc.Count, Is.EqualTo(0));
        }

        [Test]
        public void MultipleEntries_EachHandledIndependently()
        {
            var entries = new[]
            {
                Entry("mars",  opt1Dep: 15.0, opt2Dep: 17.0),   // both valid
                Entry("venus", opt1Dep: 5.0,  opt2Dep: 12.0),   // opt1 stale, opt2 valid → promote
            };
            var (cache, needsRecalc) = LWCacheHelper.PromoteWindowCache(entries, ValidIds, Now);

            Assert.That(cache["mars"].Item1.Value.DepartureEpoch, Is.EqualTo(15.0));
            Assert.That(needsRecalc, Does.Not.Contain("mars"));

            Assert.That(cache["venus"].Item1.Value.DepartureEpoch, Is.EqualTo(12.0));
            Assert.That(needsRecalc, Does.Contain("venus"));
        }
    }
}
