using System;
using System.Collections.Generic;
using SolarExpanseLaunchWindows;

namespace SolarExpanseLaunchWindowsTests
{
    // Circular coplanar orbits in the x-y plane.
    // Positions and velocities derived analytically from orbital radius and period.
    internal class FakeBodyEphemeris : IBodyEphemeris
    {
        private readonly Dictionary<string, (double radius, double period)> _bodies;
        private readonly double _sunMu;

        internal FakeBodyEphemeris(double sunMu, Dictionary<string, (double radius, double period)> bodies)
        {
            _sunMu = sunMu;
            _bodies = bodies;
        }

        public double SunMu => _sunMu;

        public double GetPeriod(string bodyId)
            => _bodies.TryGetValue(bodyId, out var b) ? b.period : 0;

        public BodyState GetState(string bodyId, double t)
        {
            if (!_bodies.TryGetValue(bodyId, out var b)) return new BodyState(default, default);
            double r = b.radius;
            double T = b.period;
            double w = 2 * Math.PI / T;   // angular velocity
            double angle = w * t;
            var pos = new Vec3d(r * Math.Cos(angle), r * Math.Sin(angle), 0);
            var vel = new Vec3d(-r * w * Math.Sin(angle), r * w * Math.Cos(angle), 0);
            return new BodyState(pos, vel);
        }

        public IEnumerable<string> AllBodyIds => _bodies.Keys;
        public IEnumerable<string> ValidBodyIds => _bodies.Keys;
        public string GetDisplayName(string bodyId) => bodyId;
        public void SnapshotPropagators() { }
        public List<string> GetSortedOriginIds() => new List<string>(_bodies.Keys);
    }

    // Returns Ok=true only when tof is in [tofLo, tofHi]; otherwise fails.
    // V1 = departVel (zero ejection dv), V2 = zero (arrival dv = arrBody.Velocity magnitude).
    internal class WindowedLambertSolver : ILambertSolver
    {
        public double TofLo;
        public double TofHi;
        public bool AlwaysFail;

        public LambertResult Solve(Vec3d r1, Vec3d r2, Vec3d departVel, double mu, double tof)
        {
            if (AlwaysFail || tof < TofLo || tof > TofHi)
                return LambertResult.Fail();
            // v1 = departVel → ejection dv = 0; v2 = 0 → capture dv = arrBody speed
            return new LambertResult(true, departVel, new Vec3d(0, 0, 0));
        }
    }

    internal class FakeGameClock : IGameClock
    {
        public DateTime CurrentTime { get; set; }
        public double PhysicalTime { get; set; }
        public double SecondsPerPhysicsSecond { get; set; } = 1.0;
        public double TimeScale { get; set; } = 1.0;
        public bool PauseGameCalled { get; private set; }
        public void PauseGame() => PauseGameCalled = true;
    }
}
