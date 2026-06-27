using System.Collections.Generic;

namespace SolarExpanseLaunchWindows
{
    internal interface IBodyEphemeris
    {
        double SunMu { get; }
        double GetPeriod(string bodyId);
        BodyState GetState(string bodyId, double epochSeconds);
        IEnumerable<string> AllBodyIds { get; }
        IEnumerable<string> ValidBodyIds { get; }
        string GetDisplayName(string bodyId);
        bool IsPlanet(string bodyId);
        void SnapshotPropagators();
        List<string> GetSortedOriginIds();
    }
}
