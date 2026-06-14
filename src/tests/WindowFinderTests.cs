using System.Collections.Generic;
using NUnit.Framework;
using SolarExpanseLaunchWindows;

namespace SolarExpanseLaunchWindowsTests
{
    [TestFixture]
    internal class WindowFinderTests
    {
        // Earth period = 1.0, Mars period = 1.881 (Kepler: (1.524 AU)^1.5)
        // Synodic = 1 / |1/1 - 1/1.881| ≈ 2.135 years
        private const double EarthPeriod = 1.0;
        private const double MarsPeriod  = 1.881;
        private const double EarthRadius = 1.0;
        private const double MarsRadius  = 1.524;
        private const double SunMu       = 4 * System.Math.PI * System.Math.PI; // in AU^3/yr^2

        private static FakeBodyEphemeris MakeEphem() => new FakeBodyEphemeris(
            SunMu,
            new Dictionary<string, (double radius, double period)>
            {
                ["earth"] = (EarthRadius, EarthPeriod),
                ["mars"]  = (MarsRadius,  MarsPeriod),
            });

        [Test]
        public void GetSynodic_EarthToMars_ApproximatelyTwoPointOneYears()
        {
            var finder = new WindowFinder(new WindowedLambertSolver(), MakeEphem(), dvToKmS: 1.0);
            double syn = finder.GetSynodic("earth", "mars");
            Assert.That(syn, Is.EqualTo(2.135).Within(0.01), "synodic period");
        }

        [Test]
        public void GetSynodic_SamePeriod_ReturnsThatPeriod()
        {
            var ephem = new FakeBodyEphemeris(SunMu, new Dictionary<string, (double, double)>
            {
                ["a"] = (1.0, 1.0),
                ["b"] = (1.0, 1.0),
            });
            var finder = new WindowFinder(new WindowedLambertSolver(), ephem, dvToKmS: 1.0);
            double syn = finder.GetSynodic("a", "b");
            Assert.That(syn, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void GetSynodic_UnknownBody_ReturnsZero()
        {
            var finder = new WindowFinder(new WindowedLambertSolver(), MakeEphem(), dvToKmS: 1.0);
            Assert.That(finder.GetSynodic("earth", "pluto"), Is.EqualTo(0.0));
        }

        [Test]
        public void FindWindows_SolverAlwaysFails_ReturnsNullWindows()
        {
            var solver = new WindowedLambertSolver { AlwaysFail = true };
            var finder = new WindowFinder(solver, MakeEphem(), dvToKmS: 1.0);
            var (opt, fst, _) = finder.FindWindows("earth", "mars", physNow: 0);
            Assert.That(opt, Is.Null, "optimal should be null");
            Assert.That(fst, Is.Null, "fastest should be null");
        }

        [Test]
        public void FindWindows_SolverSucceeds_ReturnsBothWindows()
        {
            // Hohmann tof earth→mars ≈ 0.709 yr — put the window well inside the scan range
            var solver = new WindowedLambertSolver { TofLo = 0.5, TofHi = 0.9 };
            var finder = new WindowFinder(solver, MakeEphem(), dvToKmS: 1.0);
            var (opt, fst, syn) = finder.FindWindows("earth", "mars", physNow: 0);
            Assert.That(opt, Is.Not.Null, "optimal window expected");
            Assert.That(fst, Is.Not.Null, "fastest window expected");
            Assert.That(syn, Is.EqualTo(2.135).Within(0.01));
        }

        [Test]
        public void FindWindows_OptimalWindow_TofWithinBounds()
        {
            var solver = new WindowedLambertSolver { TofLo = 0.5, TofHi = 0.9 };
            var finder = new WindowFinder(solver, MakeEphem(), dvToKmS: 1.0);
            var (opt, _, _) = finder.FindWindows("earth", "mars", physNow: 0);
            Assert.That(opt, Is.Not.Null);
            double tof = opt.Value.TravelTimeSeconds;
            Assert.That(tof, Is.GreaterThan(0.5), "tof above window low");
            Assert.That(tof, Is.LessThanOrEqualTo(0.9), "tof below window high");
        }

        [Test]
        public void FindWindows_FastestArrival_IsBeforeOrEqualToOptimalArrival()
        {
            var solver = new WindowedLambertSolver { TofLo = 0.5, TofHi = 0.9 };
            var finder = new WindowFinder(solver, MakeEphem(), dvToKmS: 1.0);
            var (opt, fst, _) = finder.FindWindows("earth", "mars", physNow: 0);
            Assert.That(opt, Is.Not.Null);
            Assert.That(fst, Is.Not.Null);
            Assert.That(fst.Value.ArrivalEpoch, Is.LessThanOrEqualTo(opt.Value.ArrivalEpoch));
        }

        [Test]
        public void FindWindows_ZeroMu_ReturnsNullWindows()
        {
            var ephem = new FakeBodyEphemeris(0, new Dictionary<string, (double, double)>
            {
                ["a"] = (1.0, 1.0),
                ["b"] = (1.5, 1.5),
            });
            var finder = new WindowFinder(new WindowedLambertSolver(), ephem, dvToKmS: 1.0);
            var (opt, fst, _) = finder.FindWindows("a", "b", physNow: 0);
            Assert.That(opt, Is.Null);
            Assert.That(fst, Is.Null);
        }

        // ── Branch coverage: tPeriodMax cap ─────────────────────────────────────
        // Two near-identical orbits → giant synodic → 1.25*syn > 3*tPeriodMax → depSpan capped.
        // T1=1.0, T2=1.1: syn≈11; 1.25*11=13.75 > 3*1.1=3.3 → num3 capped to 1.1.
        [Test]
        public void FindWindows_NearIdenticalPeriods_TriggersTPeriodMaxCap()
        {
            var ephem = new FakeBodyEphemeris(SunMu, new Dictionary<string, (double, double)>
            {
                ["inner"] = (EarthRadius, 1.00),
                ["outer"] = (System.Math.Pow(1.1, 2.0 / 3.0), 1.10), // Kepler: r = T^(2/3)
            });
            // With capped depSpan ≈ 1.1 yr, tofMin ≈ 0.11, tofMax ≈ 1.65 — solver window fits.
            var solver = new WindowedLambertSolver { TofLo = 0.15, TofHi = 0.80 };
            var finder = new WindowFinder(solver, ephem, dvToKmS: 1.0);
            var (opt, fst, _) = finder.FindWindows("inner", "outer", physNow: 0);
            Assert.That(opt, Is.Not.Null, "window should be found even after tPeriodMax cap");
            Assert.That(fst, Is.Not.Null);
        }

        // ── Branch coverage: num5 * 1.5 > 600 hard tof cap ──────────────────────
        // Large distant orbits: T1=200, T2=400 (in normalized units).
        // syn=400, num3=500 (< 3*400=1200 so tPeriodMax cap does NOT apply),
        // num5 = 0.5*(400+500) = 450, 450*1.5 = 675 > 600 → num5 reset to 400.
        // After cap: tofMin = 40, tofMax = 600.
        [Test]
        public void FindWindows_LargeOrbitPair_TriggersTofHardCap()
        {
            const double mu = 4 * System.Math.PI * System.Math.PI;
            double r1 = System.Math.Pow(200.0, 2.0 / 3.0);
            double r2 = System.Math.Pow(400.0, 2.0 / 3.0);
            var ephem = new FakeBodyEphemeris(mu, new Dictionary<string, (double, double)>
            {
                ["a"] = (r1, 200.0),
                ["b"] = (r2, 400.0),
            });
            // tofMin=40, tofMax=600 after cap — solver window sits well inside.
            var solver = new WindowedLambertSolver { TofLo = 50.0, TofHi = 200.0 };
            var finder = new WindowFinder(solver, ephem, dvToKmS: 1.0);
            var (opt, fst, _) = finder.FindWindows("a", "b", physNow: 0);
            Assert.That(opt, Is.Not.Null, "window should be found even after tof hard cap");
        }

        // ── Branch coverage: dvCap filters fastest but not optimal ───────────────
        // Solver returns v2=zero, so dv = arrBody.Velocity.Magnitude ≈ 5 AU/yr.
        // dvCap=1.0 (in same units) excludes fastest; optimal is unconstrained.
        [Test]
        public void FindWindows_DvCapExceeded_FastestNullOptimalNot()
        {
            var solver = new WindowedLambertSolver { TofLo = 0.5, TofHi = 0.9 };
            var finder = new WindowFinder(solver, MakeEphem(), dvToKmS: 1.0);
            const double tinyDvCap = 0.001; // far below any real dv in these units
            var (opt, fst, _) = finder.FindWindows("earth", "mars", physNow: 0, dvCap: tinyDvCap);
            Assert.That(opt, Is.Not.Null, "optimal ignores dvCap");
            Assert.That(fst, Is.Null,     "fastest excluded when all dv > dvCap");
        }
    }
}
