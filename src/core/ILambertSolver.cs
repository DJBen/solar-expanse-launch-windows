namespace SolarExpanseLaunchWindows
{
    internal interface ILambertSolver
    {
        LambertResult Solve(Vec3d r1, Vec3d r2, Vec3d departVel, double mu, double tof);
    }
}
