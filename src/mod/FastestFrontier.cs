using System.Collections.Generic;

namespace SolarExpanseLaunchWindows
{
    // One porkchop solution, kept as a candidate for the Fastest window.
    internal readonly struct FastestCandidate
    {
        public readonly double DepartureEpoch;
        public readonly double ArrivalEpoch;
        public readonly double DeltaVKmS;

        public FastestCandidate(double dep, double arr, double dvKmS)
        {
            DepartureEpoch = dep;
            ArrivalEpoch   = arr;
            DeltaVKmS      = dvKmS;
        }
    }

    // The Fastest window is "earliest arrival whose Δv fits the craft's budget" — the
    // only cached quantity that depends on the selected craft (Optimal ignores the cap).
    // Rather than rescanning the Lambert grid on every craft switch, a scan keeps its
    // Pareto frontier: the solutions not beaten on BOTH arrival and Δv. Any dominated
    // solution is irrelevant — its dominator arrives no later at no greater Δv — so the
    // frontier answers the question for any budget without further Lambert solves.
    internal static class FastestFrontier
    {
        // Input is indexed by arrival grid slot (ascending arrival), holding the lowest
        // Δv found for that arrival; entries with no solution are omitted by the caller.
        // Output is ascending arrival with strictly decreasing Δv.
        internal static List<FastestCandidate> Build(IEnumerable<FastestCandidate> byAscendingArrival)
        {
            var frontier = new List<FastestCandidate>();
            double bestDv = double.MaxValue;
            foreach (var c in byAscendingArrival)
            {
                if (c.DeltaVKmS >= bestDv) continue; // dominated: something earlier is cheaper
                bestDv = c.DeltaVKmS;
                frontier.Add(c);
            }
            return frontier;
        }

        // Earliest-arriving candidate within the budget. Because Δv strictly decreases
        // along the frontier, the affordable entries form a suffix and the first match
        // is the earliest arrival among them.
        internal static LaunchWindow? Select(List<FastestCandidate> frontier, double dvCapKmS)
        {
            if (frontier == null || dvCapKmS <= 0) return null;
            foreach (var c in frontier)
                if (c.DeltaVKmS <= dvCapKmS)
                    return new LaunchWindow(c.DepartureEpoch, c.ArrivalEpoch, c.DeltaVKmS);
            return null;
        }
    }
}
