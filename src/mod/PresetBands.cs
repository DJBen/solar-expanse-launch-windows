using System;

namespace SolarExpanseLaunchWindows
{
    // Destination preset orbit bands.
    internal enum PresetBand { NearEarth, InnerBelt, OuterBelt }

    // Classifies bodies into orbit bands by semi-major axis, derived from orbital
    // period via Kepler's third law: a / a_E = (T / T_E)^(2/3). Pure logic — the
    // panel supplies periods from the ephemeris; Earth's period defines 1 AU.
    internal static class PresetBands
    {
        // Band edges in AU (semi-major axis).
        internal const double NearEarthMaxAU = 1.3;  // classic NEO cutoff
        internal const double BeltSplitAU    = 2.5;  // inner/outer split (3:1 Kirkwood gap)
        internal const double OuterBeltMaxAU = 4.2;  // beyond: Trojans, comets — not "belt"

        internal static double SemiMajorAxisAU(double periodSeconds, double earthPeriodSeconds)
        {
            if (periodSeconds <= 0 || earthPeriodSeconds <= 0) return double.NaN;
            return Math.Pow(periodSeconds / earthPeriodSeconds, 2.0 / 3.0);
        }

        internal static bool InBand(PresetBand band, double periodSeconds, double earthPeriodSeconds)
        {
            double a = SemiMajorAxisAU(periodSeconds, earthPeriodSeconds);
            if (double.IsNaN(a)) return false;
            switch (band)
            {
                case PresetBand.NearEarth: return a < NearEarthMaxAU;
                case PresetBand.InnerBelt: return a >= NearEarthMaxAU && a < BeltSplitAU;
                case PresetBand.OuterBelt: return a >= BeltSplitAU && a < OuterBeltMaxAU;
                default: return false;
            }
        }
    }
}
