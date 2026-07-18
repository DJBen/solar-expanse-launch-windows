using System.Collections.Generic;
using System.Linq;
using Data;
using Game.Info;
using UnityEngine;

namespace SolarExpanseLaunchWindows
{
    internal class GameBodyEphemeris : IBodyEphemeris
    {
        private readonly Dictionary<string, OrbitUniversal> orbitsById;
        private readonly Dictionary<string, string>         namesById;
        private readonly Dictionary<string, EObjectTypes>   typesById;
        private readonly double                              sunMu;
        private readonly Dictionary<string, OrbitPropagator> propCache
            = new Dictionary<string, OrbitPropagator>();

        // OrbitEllipse bodies (asteroids etc.) — store NBody for GE snapshot, period from component.
        private readonly Dictionary<string, NBody>   ellipseNBodiesById = new Dictionary<string, NBody>();
        private readonly Dictionary<string, double>  ellipsePeriodsById = new Dictionary<string, double>();

        public GameBodyEphemeris(
            Dictionary<string, OrbitUniversal> orbits,
            Dictionary<string, string>         names,
            Dictionary<string, EObjectTypes>   types,
            double                             sunMu,
            Dictionary<string, NBody>          ellipseNBodies = null,
            Dictionary<string, double>         ellipsePeriods = null)
        {
            this.orbitsById = orbits;
            this.namesById  = names;
            this.typesById  = types;
            this.sunMu      = sunMu;
            if (ellipseNBodies != null)
                foreach (var kv in ellipseNBodies) ellipseNBodiesById[kv.Key] = kv.Value;
            if (ellipsePeriods != null)
                foreach (var kv in ellipsePeriods) ellipsePeriodsById[kv.Key] = kv.Value;
        }

        public double SunMu => sunMu;

        // ── Synthetic "Solar Orbit" origin ────────────────────────────────────────
        // The game's Solar Orbit is a virtual location with no NBody of its own
        // (ObjectInfo.Position returns the Sun); the game's convention for its
        // distance is ObjectInfo.distanceSolarOrbitAU = 0.01 AU. Model it as a
        // circular heliocentric orbit at 0.01 × Earth's orbital radius, in Earth's
        // orbital plane. Basis is captured on the main thread (SetSolarOrbitBasis);
        // GetState is then pure math, safe on the calc thread.
        public const string SolarOrbitId = "SOLAR_ORBIT";
        private double _soR;              // orbit radius, game units; 0 = not initialized
        private double _soN, _soVc, _soT0; // mean motion, circular speed, basis epoch
        private double _soE1x, _soE1y, _soE1z, _soE2x, _soE2y, _soE2z;

        public bool SolarOrbitReady => _soR > 0;

        private void SetSolarOrbitBasis(double rx, double ry, double rz,
                                        double vx, double vy, double vz, double time0)
        {
            double rMag = System.Math.Sqrt(rx * rx + ry * ry + rz * rz);
            if (rMag <= 0 || sunMu <= 0) return;
            // e1 = r̂; e2 = (h × r)̂ with h = r × v — spans Earth's orbital plane.
            double hx = ry * vz - rz * vy, hy = rz * vx - rx * vz, hz = rx * vy - ry * vx;
            double e2x = hy * rz - hz * ry, e2y = hz * rx - hx * rz, e2z = hx * ry - hy * rx;
            double e2Mag = System.Math.Sqrt(e2x * e2x + e2y * e2y + e2z * e2z);
            if (e2Mag <= 0) return;
            _soE1x = rx / rMag;  _soE1y = ry / rMag;  _soE1z = rz / rMag;
            _soE2x = e2x / e2Mag; _soE2y = e2y / e2Mag; _soE2z = e2z / e2Mag;
            _soR  = 0.01 * rMag;
            _soN  = System.Math.Sqrt(sunMu / (_soR * _soR * _soR));
            _soVc = System.Math.Sqrt(sunMu / _soR);
            _soT0 = time0;
        }

        // Derive the solar-orbit basis from Earth's current state. Main thread only.
        public void TryInitSolarOrbit()
        {
            if (SolarOrbitReady) return;
            var ge = GravityEngine.Instance();
            if (ge == null) return;
            var earthId = namesById.FirstOrDefault(kv =>
                string.Equals(kv.Value, "Earth", System.StringComparison.OrdinalIgnoreCase)).Key;
            if (earthId == null || !orbitsById.TryGetValue(earthId, out var earthOrbit)) return;
            var nb = earthOrbit.GetComponent<NBody>();
            if (nb == null) return;
            var r0 = ge.GetPositionDoubleV3(nb);
            var v0 = ge.GetVelocityDoubleV3(nb);
            SetSolarOrbitBasis(r0.x, r0.y, r0.z, v0.x, v0.y, v0.z, ge.GetPhysicalTimeDouble());
        }

        public double GetPeriod(string bodyId)
        {
            if (bodyId == SolarOrbitId) return SolarOrbitReady ? 2.0 * System.Math.PI / _soN : 0.0;
            if (orbitsById.TryGetValue(bodyId, out var orbit)) return orbit.GetPeriod();
            if (ellipsePeriodsById.TryGetValue(bodyId, out var p)) return p;
            return 0.0;
        }

        public IEnumerable<string> AllBodyIds
            => orbitsById.Keys.Concat(ellipseNBodiesById.Keys)
               .Concat(SolarOrbitReady ? new[] { SolarOrbitId } : System.Array.Empty<string>());

        // All independent heliocentric bodies — excludes [ORBIT] companion bodies only.
        // Moons are already excluded by the mu threshold (OrbitUniversal) or type filter (OrbitEllipse).
        public IEnumerable<string> ValidBodyIds => AllBodyIds
            .Where(id => !typesById.TryGetValue(id, out var t) || t != EObjectTypes.Orbit);

        // Call from the main thread before using GetState on a background thread.
        public void SnapshotPropagators()
        {
            TryInitSolarOrbit();
            propCache.Clear();
            foreach (var kv in orbitsById)
                propCache[kv.Key] = OrbitPropagator.GetPropagator(kv.Value);

            var ge = GravityEngine.Instance();
            if (ge == null) return;
            double time0 = ge.GetPhysicalTimeDouble();
            foreach (var kv in ellipseNBodiesById)
            {
                var r0 = ge.GetPositionDoubleV3(kv.Value);
                var v0 = ge.GetVelocityDoubleV3(kv.Value);
                propCache[kv.Key] = new OrbitPropagator(r0, v0, time0, sunMu);
            }
        }

        public BodyState GetState(string bodyId, double epochSeconds)
        {
            if (bodyId == SolarOrbitId)
            {
                if (!SolarOrbitReady) return new BodyState(default, default);
                double th = _soN * (epochSeconds - _soT0);
                double c = System.Math.Cos(th), s = System.Math.Sin(th);
                return new BodyState(
                    new Vec3d(_soR * (c * _soE1x + s * _soE2x),
                              _soR * (c * _soE1y + s * _soE2y),
                              _soR * (c * _soE1z + s * _soE2z)),
                    new Vec3d(_soVc * (-s * _soE1x + c * _soE2x),
                              _soVc * (-s * _soE1y + c * _soE2y),
                              _soVc * (-s * _soE1z + c * _soE2z)));
            }

            bool known = orbitsById.ContainsKey(bodyId) || ellipseNBodiesById.ContainsKey(bodyId);
            if (!known) return new BodyState(default, default);

            if (!propCache.TryGetValue(bodyId, out var prop))
            {
                if (orbitsById.TryGetValue(bodyId, out var orbit))
                    prop = OrbitPropagator.GetPropagator(orbit);
                else
                    return new BodyState(default, default);
            }

            var (pos, vel) = prop.PropagateToTime(epochSeconds);
            return new BodyState(
                new Vec3d(pos.x, pos.y, pos.z),
                new Vec3d(vel.x, vel.y, vel.z));
        }

        public string GetDisplayName(string bodyId)
            => namesById.TryGetValue(bodyId, out var n) ? n : bodyId;

        public bool IsPlanet(string bodyId)
            => typesById.TryGetValue(bodyId, out var t) && t == EObjectTypes.Planet;

        public bool IsPlanetOrAsteroid(string bodyId)
            => typesById.TryGetValue(bodyId, out var t) && (t == EObjectTypes.Planet || t == EObjectTypes.Asteroid);

        // Heliocentric player locations ("Solar Orbit") — valid transfer origins.
        public bool IsSolarOrbit(string bodyId)
            => bodyId == SolarOrbitId
            || (typesById.TryGetValue(bodyId, out var t) && t == EObjectTypes.SolarOrbit);

        // Returns planet body IDs sorted by current orbital radius ascending.
        public List<string> GetSortedPlanetIds()
        {
            var ge = GravityEngine.Instance();
            if (ge == null) return new List<string>();
            double physNow = ge.GetPhysicalTimeDouble();
            return orbitsById.Keys
                .Where(IsPlanet)
                .OrderBy(id => GetState(id, physNow).Position.Magnitude)
                .ToList();
        }

        // Returns planets + asteroids + solar-orbit stations sorted by orbital radius —
        // used for the "From" dropdown.
        public List<string> GetSortedOriginIds()
        {
            var ge = GravityEngine.Instance();
            if (ge == null) return new List<string>();
            double physNow = ge.GetPhysicalTimeDouble();
            return AllBodyIds
                .Where(id => IsPlanetOrAsteroid(id) || IsSolarOrbit(id))
                .OrderBy(id => GetState(id, physNow).Position.Magnitude)
                .ToList();
        }

        public NBody GetNBodyForId(string bodyId)
        {
            if (ellipseNBodiesById.TryGetValue(bodyId, out var nb)) return nb;
            if (orbitsById.TryGetValue(bodyId, out var orbit)) return orbit.GetComponent<NBody>();
            return null;
        }

        public static GameBodyEphemeris BuildFromScene()
        {
            // ── OrbitUniversal bodies (planets, comets, etc.) ─────────────────────
            var all = new List<(NBody nb, OrbitUniversal orbit, double mu)>();
            double maxMu = 0;
            foreach (var nb in Object.FindObjectsOfType<NBody>())
            {
                var orbit = nb.GetComponent<OrbitUniversal>();
                if (orbit == null) continue;
                double m = orbit.GetMu();
                all.Add((nb, orbit, m));
                if (m > maxMu) maxMu = m;
            }

            var orbits = new Dictionary<string, OrbitUniversal>();
            var names  = new Dictionary<string, string>();
            var types  = new Dictionary<string, EObjectTypes>();
            double muThreshold = maxMu * 0.99;
            foreach (var entry in all)
            {
                if (entry.mu < muThreshold) continue;
                var id = entry.nb.GetInstanceID().ToString();
                orbits[id] = entry.orbit;
                var info = entry.nb.GetObjectInfo();
                types[id] = info != null ? info.objectTypes : EObjectTypes.None;
                names[id] = entry.nb.name ?? id;
            }

            // ── OrbitEllipse bodies (asteroids etc.) ──────────────────────────────
            var ellipseNBodies = new Dictionary<string, NBody>();
            var ellipsePeriods = new Dictionary<string, double>();
            foreach (var nb in Object.FindObjectsOfType<NBody>())
            {
                if (nb.GetComponent<OrbitUniversal>() != null) continue; // already handled
                var ellipse = nb.GetComponent<OrbitEllipse>();
                if (ellipse == null) continue;
                var info    = nb.GetObjectInfo();
                var objType = info != null ? info.objectTypes : EObjectTypes.None;
                if (objType == EObjectTypes.Moons || objType == EObjectTypes.Orbit) continue;
                var id = nb.GetInstanceID().ToString();
                names[id]            = nb.name ?? id;
                types[id]            = objType;
                ellipseNBodies[id]   = nb;
                ellipsePeriods[id]   = ellipse.GetPeriod();
            }

            // Synthetic Solar Orbit origin — game-localized name, initialized from Earth.
            string soName = "Solar Orbit";
            try
            {
                var loc = Language.LEManager.Get("CelestialBodiesNames.SunOrbit");
                if (!string.IsNullOrEmpty(loc)) soName = loc;
            }
            catch { }
            names[SolarOrbitId] = soName;

            var ephem = new GameBodyEphemeris(orbits, names, types, maxMu, ellipseNBodies, ellipsePeriods);
            ephem.TryInitSolarOrbit();
            return ephem;
        }
    }
}
