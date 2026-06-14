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
        private readonly Dictionary<string, string> namesById;
        private readonly Dictionary<string, EObjectTypes> typesById;
        private readonly double sunMu;
        private readonly Dictionary<string, OrbitPropagator> propCache
            = new Dictionary<string, OrbitPropagator>();

        public GameBodyEphemeris(
            Dictionary<string, OrbitUniversal> orbits,
            Dictionary<string, string> names,
            Dictionary<string, EObjectTypes> types,
            double sunMu)
        {
            this.orbitsById = orbits;
            this.namesById = names;
            this.typesById = types;
            this.sunMu = sunMu;
        }

        public double SunMu => sunMu;

        public double GetPeriod(string bodyId)
            => orbitsById.TryGetValue(bodyId, out var orbit) ? orbit.GetPeriod() : 0.0;

        public IEnumerable<string> AllBodyIds => orbitsById.Keys;

        // Call from the main thread before using GetState on a background thread.
        public void SnapshotPropagators()
        {
            propCache.Clear();
            foreach (var kv in orbitsById)
                propCache[kv.Key] = OrbitPropagator.GetPropagator(kv.Value);
        }

        public BodyState GetState(string bodyId, double epochSeconds)
        {
            if (!orbitsById.TryGetValue(bodyId, out var orbit))
                return new BodyState(default, default);

            if (!propCache.TryGetValue(bodyId, out var prop))
                prop = OrbitPropagator.GetPropagator(orbit);

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
            return orbitsById.Keys
                .Where(IsPlanetOrAsteroid)
                .OrderBy(id => GetState(id, physNow).Position.Magnitude)
                .ToList();
        }

        public static GameBodyEphemeris BuildFromScene()
        {
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
            var names = new Dictionary<string, string>();
            var types = new Dictionary<string, EObjectTypes>();
            double muThreshold = maxMu * 0.99;
            foreach (var entry in all)
            {
                if (entry.mu < muThreshold) continue;
                var id = entry.nb.GetInstanceID().ToString();
                orbits[id] = entry.orbit;
                names[id] = entry.nb.name ?? id;
                var info = entry.nb.GetObjectInfo();
                types[id] = info != null ? info.objectTypes : EObjectTypes.None;
            }
            return new GameBodyEphemeris(orbits, names, types, maxMu);
        }
    }
}
