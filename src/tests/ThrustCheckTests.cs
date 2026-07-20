using NUnit.Framework;
using SolarExpanseLaunchWindows;

namespace SolarExpanseLaunchWindowsTests
{
    [TestFixture]
    public class ThrustCheckTests
    {
        // 1 km/s at 1000 N on a 1 t craft: a = 1 m/s² → burn = 1000 s (multiplier 1).
        [Test]
        public void BurnTime_UnitCase()
        {
            Assert.That(ThrustCheck.BurnTimeSeconds(1.0, 1000.0, 1.0, 1.0), Is.EqualTo(1000.0).Within(1e-9));
        }

        [Test]
        public void BurnTime_MultiplierScalesLinearly()
        {
            Assert.That(ThrustCheck.BurnTimeSeconds(1.0, 1000.0, 1.0, 2.5), Is.EqualTo(2500.0).Within(1e-9));
        }

        [Test]
        public void BurnTime_HeavierCraftBurnsLonger()
        {
            Assert.That(ThrustCheck.BurnTimeSeconds(1.0, 1000.0, 10.0, 1.0), Is.EqualTo(10000.0).Within(1e-6));
        }

        [Test]
        public void HasEnoughThrust_BoundaryInclusive()
        {
            Assert.That(ThrustCheck.HasEnoughThrust(1.0, 1000.0, 1.0, 1000.0, 1.0), Is.True);
            Assert.That(ThrustCheck.HasEnoughThrust(1.0, 1000.0, 1.0, 999.0, 1.0), Is.False);
        }

        [Test]
        public void ZeroThrust_NeverEnough()
        {
            Assert.That(ThrustCheck.HasEnoughThrust(1.0, 0.0, 1.0, double.MaxValue, 1.0), Is.False);
        }

        [Test]
        public void ZeroDv_AlwaysEnough()
        {
            Assert.That(ThrustCheck.HasEnoughThrust(0.0, 0.0, 1.0, 0.0, 1.0), Is.True);
        }
    }
}
