using System;

namespace SolarExpanseLaunchWindows
{
    internal readonly struct Vec3d
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }

        public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.X * s, a.Y * s, a.Z * s);
    }

    internal readonly struct BodyState
    {
        public readonly Vec3d Position;
        public readonly Vec3d Velocity;

        public BodyState(Vec3d p, Vec3d v) { Position = p; Velocity = v; }
    }

    internal readonly struct LambertResult
    {
        public readonly bool Ok;
        public readonly Vec3d V1;
        public readonly Vec3d V2;

        public LambertResult(bool ok, Vec3d v1, Vec3d v2) { Ok = ok; V1 = v1; V2 = v2; }

        public static LambertResult Fail() => new LambertResult(false, default, default);
    }

    internal readonly struct LaunchWindow
    {
        public readonly double DepartureEpoch;
        public readonly double ArrivalEpoch;
        public readonly double DeltaVKmS;

        public LaunchWindow(double departureEpoch, double arrivalEpoch, double deltaVKmS)
        {
            DepartureEpoch = departureEpoch;
            ArrivalEpoch = arrivalEpoch;
            DeltaVKmS = deltaVKmS;
        }

        public double TravelTimeSeconds => ArrivalEpoch - DepartureEpoch;
    }
}
