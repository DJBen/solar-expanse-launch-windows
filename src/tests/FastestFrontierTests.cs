using System.Collections.Generic;
using NUnit.Framework;
using SolarExpanseLaunchWindows;

namespace SolarExpanseLaunchWindowsTests
{
    [TestFixture]
    public class FastestFrontierTests
    {
        // (arrival, dv) pairs in ascending-arrival order, as the scan produces them.
        private static List<FastestCandidate> Cands(params (double arr, double dv)[] items)
        {
            var list = new List<FastestCandidate>();
            foreach (var (arr, dv) in items) list.Add(new FastestCandidate(arr - 1.0, arr, dv));
            return list;
        }

        [Test]
        public void Build_KeepsOnlyNonDominated()
        {
            // arrival 3 (dv 8) is dominated by arrival 1 (dv 5): later AND pricier.
            var f = FastestFrontier.Build(Cands((1, 5), (3, 8), (5, 4), (7, 6), (9, 2)));
            Assert.That(f.Count, Is.EqualTo(3));
            Assert.That(f[0].ArrivalEpoch, Is.EqualTo(1));
            Assert.That(f[1].ArrivalEpoch, Is.EqualTo(5));
            Assert.That(f[2].ArrivalEpoch, Is.EqualTo(9));
        }

        [Test]
        public void Build_DvStrictlyDecreases()
        {
            var f = FastestFrontier.Build(Cands((1, 5), (2, 5), (3, 4.999)));
            Assert.That(f.Count, Is.EqualTo(2), "equal-Δv later arrival is dominated");
            Assert.That(f[1].DeltaVKmS, Is.LessThan(f[0].DeltaVKmS));
        }

        [Test]
        public void Select_PicksEarliestAffordable()
        {
            var f = FastestFrontier.Build(Cands((1, 5), (5, 4), (9, 2)));
            Assert.That(FastestFrontier.Select(f, 5.0)?.ArrivalEpoch, Is.EqualTo(1));
            Assert.That(FastestFrontier.Select(f, 4.5)?.ArrivalEpoch, Is.EqualTo(5));
            Assert.That(FastestFrontier.Select(f, 2.0)?.ArrivalEpoch, Is.EqualTo(9));
        }

        [Test]
        public void Select_BudgetBelowEverything_ReturnsNull()
        {
            var f = FastestFrontier.Build(Cands((1, 5), (5, 4), (9, 2)));
            Assert.That(FastestFrontier.Select(f, 1.9), Is.Null);
        }

        [Test]
        public void Select_ZeroOrNegativeCap_ReturnsNull()
        {
            // Solar sails pass a zero cap: impulsive Fastest windows don't apply.
            var f = FastestFrontier.Build(Cands((1, 5)));
            Assert.That(FastestFrontier.Select(f, 0.0), Is.Null);
        }

        [Test]
        public void Select_EmptyOrNullFrontier_ReturnsNull()
        {
            Assert.That(FastestFrontier.Select(new List<FastestCandidate>(), 10.0), Is.Null);
            Assert.That(FastestFrontier.Select(null, 10.0), Is.Null);
        }

        [Test]
        public void Select_CarriesDepartureAndDv()
        {
            var f = FastestFrontier.Build(Cands((5, 4)));
            var w = FastestFrontier.Select(f, 10.0);
            Assert.That(w.HasValue);
            Assert.That(w.Value.DepartureEpoch, Is.EqualTo(4));
            Assert.That(w.Value.DeltaVKmS, Is.EqualTo(4));
        }

        // The frontier must answer exactly as a brute-force scan would, for any budget.
        [Test]
        public void Select_MatchesBruteForceOverManyBudgets()
        {
            var all = Cands((1, 9), (2, 7), (3, 7.5), (4, 3), (5, 6), (6, 2.5), (7, 8), (8, 1));
            var f = FastestFrontier.Build(all);
            for (double cap = 0.5; cap <= 10.0; cap += 0.25)
            {
                double bestArr = double.MaxValue;
                foreach (var c in all)
                    if (c.DeltaVKmS <= cap && c.ArrivalEpoch < bestArr) bestArr = c.ArrivalEpoch;
                var sel = FastestFrontier.Select(f, cap);
                if (bestArr == double.MaxValue) Assert.That(sel, Is.Null, $"cap {cap}");
                else Assert.That(sel?.ArrivalEpoch, Is.EqualTo(bestArr), $"cap {cap}");
            }
        }
    }
}
