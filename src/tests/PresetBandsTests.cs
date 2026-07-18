using NUnit.Framework;
using SolarExpanseLaunchWindows;

namespace SolarExpanseLaunchWindowsTests
{
    [TestFixture]
    public class PresetBandsTests
    {
        // Periods in arbitrary units; Earth = 1.0. a = (T/T_E)^(2/3).
        const double EarthT = 1.0;

        static double PeriodForAu(double au) => System.Math.Pow(au, 1.5);

        [Test]
        public void SemiMajorAxis_EarthPeriod_IsOneAu()
        {
            Assert.That(PresetBands.SemiMajorAxisAU(EarthT, EarthT), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void SemiMajorAxis_MarsLikePeriod_MatchesKepler()
        {
            // Mars: T ≈ 1.881 yr → a ≈ 1.524 AU
            Assert.That(PresetBands.SemiMajorAxisAU(1.881, EarthT), Is.EqualTo(1.524).Within(0.01));
        }

        [Test]
        public void SemiMajorAxis_NonPositiveInputs_ReturnNaN()
        {
            Assert.That(double.IsNaN(PresetBands.SemiMajorAxisAU(0, EarthT)));
            Assert.That(double.IsNaN(PresetBands.SemiMajorAxisAU(1.0, 0)));
            Assert.That(double.IsNaN(PresetBands.SemiMajorAxisAU(-1.0, EarthT)));
        }

        [TestCase(0.9,  true)]   // Aten-like
        [TestCase(1.1,  true)]   // Apollo-like
        [TestCase(1.29, true)]   // just under cutoff
        [TestCase(1.3,  false)]  // at cutoff — excluded
        [TestCase(2.2,  false)]
        public void NearEarth_Band(double au, bool expected)
        {
            Assert.That(PresetBands.InBand(PresetBand.NearEarth, PeriodForAu(au), EarthT), Is.EqualTo(expected));
        }

        [TestCase(1.3,  true)]   // lower edge inclusive
        [TestCase(2.2,  true)]   // Vesta-like
        [TestCase(2.49, true)]
        [TestCase(2.5,  false)]  // upper edge — excluded
        [TestCase(1.0,  false)]
        public void InnerBelt_Band(double au, bool expected)
        {
            Assert.That(PresetBands.InBand(PresetBand.InnerBelt, PeriodForAu(au), EarthT), Is.EqualTo(expected));
        }

        [TestCase(2.5,  true)]   // lower edge inclusive
        [TestCase(2.77, true)]   // Ceres-like
        [TestCase(3.3,  true)]   // Hilda-adjacent
        [TestCase(4.19, true)]
        [TestCase(4.21, false)]  // beyond belt (Trojans, comets); exact 4.2 is FP-roundtrip ambiguous
        [TestCase(5.2,  false)]  // Jupiter Trojan — excluded
        [TestCase(2.2,  false)]
        public void OuterBelt_Band(double au, bool expected)
        {
            Assert.That(PresetBands.InBand(PresetBand.OuterBelt, PeriodForAu(au), EarthT), Is.EqualTo(expected));
        }

        [Test]
        public void Bands_ArePartitions_NoOverlap()
        {
            foreach (var au in new[] { 0.5, 1.0, 1.3, 2.0, 2.5, 3.0, 4.0, 4.2, 5.2 })
            {
                int hits = 0;
                foreach (PresetBand b in System.Enum.GetValues(typeof(PresetBand)))
                    if (PresetBands.InBand(b, PeriodForAu(au), EarthT)) hits++;
                Assert.That(hits, Is.LessThanOrEqualTo(1), $"a={au} AU matched {hits} bands");
            }
        }

        [Test]
        public void InBand_NaNPeriod_False()
        {
            Assert.That(PresetBands.InBand(PresetBand.NearEarth, 0, EarthT), Is.False);
            Assert.That(PresetBands.InBand(PresetBand.OuterBelt, 1.0, 0), Is.False);
        }
    }
}
