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

        public double GetPeriod(string bodyId)
        {
            if (orbitsById.TryGetValue(bodyId, out var orbit)) return orbit.GetPeriod();
            if (ellipsePeriodsById.TryGetValue(bodyId, out var p)) return p;
            return 0.0;
        }

        public IEnumerable<string> AllBodyIds
            => orbitsById.Keys.Concat(ellipseNBodiesById.Keys);

        // All independent heliocentric bodies — excludes [ORBIT] companion bodies only.
        // Moons are already excluded by the mu threshold (OrbitUniversal) or type filter (OrbitEllipse).
        public IEnumerable<string> ValidBodyIds => AllBodyIds
            .Where(id => !typesById.TryGetValue(id, out var t) || t != EObjectTypes.Orbit);

        // Call from the main thread before using GetState on a background thread.
        public void SnapshotPropagators()
        {
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

        public bool IsAsteroid(string bodyId)
            => typesById.TryGetValue(bodyId, out var t) && t == EObjectTypes.Asteroid;

        public bool IsPlanetOrAsteroid(string bodyId)
            => typesById.TryGetValue(bodyId, out var t) && (t == EObjectTypes.Planet || t == EObjectTypes.Asteroid);

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

        // Returns planets + asteroids sorted by orbital radius — used for the "From" dropdown.
        public List<string> GetSortedOriginIds()
        {
            var ge = GravityEngine.Instance();
            if (ge == null) return new List<string>();
            double physNow = ge.GetPhysicalTimeDouble();
            return AllBodyIds
                .Where(IsPlanetOrAsteroid)
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

            return new GameBodyEphemeris(orbits, names, types, maxMu, ellipseNBodies, ellipsePeriods);
        }
    }
}
