namespace SolarExpanseLaunchWindows
{
    // Finite-burn feasibility, mirroring the game's PMMissionParameter.CheckCanLaunchThrust:
    //   acceleration = thrust[N] / (mass[t] × 1000)             (m/s²)
    //   burnTime     = Δv[km/s] × 1000 × multiplier / acceleration
    //   ok           = burnTime ≤ travel time (game seconds)
    // The Lambert solution assumes impulsive burns; a low-thrust craft may hold enough
    // Δv (fuel) yet be unable to deliver it within the transfer's duration.
    internal static class ThrustCheck
    {
        internal static double BurnTimeSeconds(double dvKmS, double thrustN, double massTons, double multiplier)
        {
            if (thrustN <= 0 || massTons <= 0) return double.PositiveInfinity;
            double accel = thrustN / (massTons * 1000.0);
            return dvKmS * 1000.0 * multiplier / accel;
        }

        internal static bool HasEnoughThrust(double dvKmS, double thrustN, double massTons,
                                             double travelGameSeconds, double multiplier)
        {
            if (dvKmS <= 0) return true;
            return BurnTimeSeconds(dvKmS, thrustN, massTons, multiplier) <= travelGameSeconds;
        }
    }
}
