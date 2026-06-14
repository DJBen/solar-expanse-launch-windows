using System;
using System.Collections.Generic;
using NUnit.Framework;
using SolarExpanseLaunchWindows;

namespace SolarExpanseLaunchWindowsTests
{
    [TestFixture]
    internal class AlarmTests
    {
        private static AlarmKey Key(string origin, string dest, int year, int month) =>
            new AlarmKey { OriginId = origin, DestId = dest, Year = year, Month = month };

        // ── GetAlarmsToFire ──────────────────────────────────────────────────────

        [Test]
        public void MatchingYearAndMonth_ReturnsKey()
        {
            var alarms = new[] { Key("earth", "mars", 2030, 3) };
            var result = LWCacheHelper.GetAlarmsToFire(alarms, "earth", new DateTime(2030, 3, 15));
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DestId, Is.EqualTo("mars"));
        }

        [Test]
        public void WrongOrigin_Empty()
        {
            var alarms = new[] { Key("earth", "mars", 2030, 3) };
            var result = LWCacheHelper.GetAlarmsToFire(alarms, "venus", new DateTime(2030, 3, 15));
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void WrongMonth_Empty()
        {
            var alarms = new[] { Key("earth", "mars", 2030, 3) };
            var result = LWCacheHelper.GetAlarmsToFire(alarms, "earth", new DateTime(2030, 4, 1));
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void WrongYear_Empty()
        {
            var alarms = new[] { Key("earth", "mars", 2030, 3) };
            var result = LWCacheHelper.GetAlarmsToFire(alarms, "earth", new DateTime(2031, 3, 1));
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void MultipleAlarms_OnlyMatchingOnesReturned()
        {
            var alarms = new[]
            {
                Key("earth", "mars",    2030, 3),
                Key("earth", "jupiter", 2030, 3),
                Key("earth", "venus",   2031, 3),
            };
            var result = LWCacheHelper.GetAlarmsToFire(alarms, "earth", new DateTime(2030, 3, 1));
            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void EmptyAlarmSet_ReturnsEmpty()
        {
            var result = LWCacheHelper.GetAlarmsToFire(new AlarmKey[0], "earth", new DateTime(2030, 3, 1));
            Assert.That(result, Is.Empty);
        }

        // ── AlarmKey equality / hash ─────────────────────────────────────────────

        [Test]
        public void AlarmKey_SameValues_Equal()
        {
            var a = Key("earth", "mars", 2030, 3);
            var b = Key("earth", "mars", 2030, 3);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void AlarmKey_DifferentDest_NotEqual()
        {
            var a = Key("earth", "mars",    2030, 3);
            var b = Key("earth", "jupiter", 2030, 3);
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void AlarmKey_UsableInHashSet()
        {
            var set = new HashSet<AlarmKey> { Key("earth", "mars", 2030, 3) };
            Assert.That(set.Contains(Key("earth", "mars", 2030, 3)), Is.True);
            Assert.That(set.Remove(Key("earth", "mars", 2030, 3)), Is.True);
            Assert.That(set, Is.Empty);
        }
    }
}
