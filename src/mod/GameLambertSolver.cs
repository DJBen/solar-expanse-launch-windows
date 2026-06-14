using System.Reflection;
using UnityEngine;

namespace SolarExpanseLaunchWindows
{
    // Wraps LambertPorkchop.ComputeLambert2 — the exact method the game uses for its porkchop grid.
    // ComputeLambert2 retries with reverse=true when v1 is retrograde (dot(v1,departVel)<0),
    // matching the game's arc-selection logic.
    internal class GameLambertSolver : ILambertSolver
    {
        private readonly MethodInfo computeLambert2;
        private readonly FieldInfo item1, item2, item3;

        public GameLambertSolver()
        {
            var t = typeof(global::LambertPorkchop);
            computeLambert2 = t.GetMethod(
                "ComputeLambert2",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(Vector3d), typeof(Vector3d), typeof(double), typeof(double), typeof(Vector3d) },
                null)
                ?? throw new System.InvalidOperationException("LambertPorkchop.ComputeLambert2 not found");

            var tupleType = computeLambert2.ReturnType;
            item1 = tupleType.GetField("Item1") ?? throw new System.InvalidOperationException("ValueTuple.Item1 missing");
            item2 = tupleType.GetField("Item2") ?? throw new System.InvalidOperationException("ValueTuple.Item2 missing");
            item3 = tupleType.GetField("Item3") ?? throw new System.InvalidOperationException("ValueTuple.Item3 missing");
        }

        // r1=dep position, r2=arr position, departVel=dep body velocity (for retrograde check), mu=GM_sun, tof=flight time
        public LambertResult Solve(Vec3d r1, Vec3d r2, Vec3d departVel, double mu, double tof)
        {
            var r1u  = new Vector3d(r1.X,        r1.Y,        r1.Z);
            var r2u  = new Vector3d(r2.X,        r2.Y,        r2.Z);
            var depV = new Vector3d(departVel.X, departVel.Y, departVel.Z);
            // ComputeLambert2(r1, r2, mu, dtsec, departVel)
            var ret  = computeLambert2.Invoke(null, new object[] { r1u, r2u, mu, tof, depV });
            var err  = (int)item1.GetValue(ret);
            if (err != 0) return LambertResult.Fail();
            var v1 = (Vector3d)item2.GetValue(ret);
            var v2 = (Vector3d)item3.GetValue(ret);
            return new LambertResult(true, new Vec3d(v1.x, v1.y, v1.z), new Vec3d(v2.x, v2.y, v2.z));
        }
    }
}
