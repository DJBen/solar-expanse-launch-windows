using System;

namespace SolarExpanseLaunchWindows
{
    internal class WindowFinder
    {
        private readonly ILambertSolver solver;
        private readonly IBodyEphemeris ephem;
        private readonly double dvToKmS;

        // Scene-configured values (MySceneGame.unity, all 4 LambertPorkchop instances)
        private const int DepIntervals = 200;
        private const int ArrIntervals = 200;

        public WindowFinder(ILambertSolver solver, IBodyEphemeris ephem, double dvToKmS)
        {
            this.solver = solver;
            this.ephem = ephem;
            this.dvToKmS = dvToKmS;
        }

        internal double GetSynodic(string originId, string destId)
        {
            double tO = ephem.GetPeriod(originId);
            double tD = ephem.GetPeriod(destId);
            if (tO <= 0 || tD <= 0) return 0;
            double freqDiff = Math.Abs(1.0 / tO - 1.0 / tD);
            return freqDiff > 0 ? 1.0 / freqDiff : tO;
        }

        public (LaunchWindow? optimal, LaunchWindow? fastest, double synodicPeriod) FindWindows(
            string originId, string destId, double physNow, double dvCap = double.MaxValue)
        {
            double mu = ephem.SunMu;
            if (mu <= 0) return (null, null, 0);

            // Use orbit.GetPeriod() — matches LambertPorkchop.ConvertReltoAbsolute() exactly.
            double tOribit = ephem.GetPeriod(originId);
            double tDorbit = ephem.GetPeriod(destId);
            if (tOribit <= 0 || tDorbit <= 0) return (null, null, 0);
            double freqDiff = Math.Abs(1.0 / tOribit - 1.0 / tDorbit);
            double tSynodic = freqDiff > 0 ? 1.0 / freqDiff : tOribit;

            // Matches LambertPorkchop.ConvertReltoAbsolute() with SCENE-configured values.
            // Scene (MySceneGame.unity): departNumOrbits=1, minFlightTimeHohRel=0.1, maxFlightTimeHohRel=1.5
            // multiplayerSyndonicznyOkresObiegu=1.25f is field default (not overridden in scene).
            double num3 = 1.25 * tSynodic;
            double tPeriodMax = Math.Max(tOribit, tDorbit);
            if (num3 > 3.0 * tPeriodMax) num3 = tPeriodMax;

            double depSpan = 1.0 * num3;   // departNumOrbits = 1 (scene)

            double num5 = 0.5 * (tDorbit + num3);
            if (num5 * 1.5 > 600.0) num5 = 400.0;  // LambertPorkchop line 372, with maxFlightTimeHohRel=1.5

            double tofMin = 0.1 * num5;   // minFlightTimeHohRel = 0.1 (scene)
            double tofMax = 1.5 * num5;   // maxFlightTimeHohRel = 1.5 (scene)

            // Game's exact parameterization (LambertPorkchop.ConvertReltoAbsolute + Execute):
            // fixed arrival-time axis, independent of departure time.
            double depStart = physNow;
            double depEnd   = physNow + depSpan;
            double arrStart = depStart + tofMin;
            double arrEnd   = depEnd   + tofMax;
            double depStep  = depSpan / DepIntervals;
            double arrStep  = (arrEnd - arrStart) / ArrIntervals;

            // Pre-compute arrival states on the fixed arrival grid (game's inner pre-computation).
            var arrStates = new BodyState[ArrIntervals + 1];
            for (int k = 0; k <= ArrIntervals; k++)
                arrStates[k] = ephem.GetState(destId, arrStart + k * arrStep);

            // Game's DataGridToValueToSort3 (deltaVPickerButtonOptimalRoundResult=0.9 from Economic.asset):
            // score = dv if |bestOptJ-j|<5 (close); else dv/0.9 (far candidates need 10% margin to displace).
            double bestDv     = double.MaxValue;
            double bestDepOpt = 0, bestArrOpt = 0;
            int    bestOptJ   = 0;

            // Fastest: earliest arrival within dvCap.
            double earliestArr = double.MaxValue;
            double fastDv = 0, fastDep = 0, fastArr = 0;

            for (int j = 0; j <= DepIntervals; j++)
            {
                double tDep   = depStart + j * depStep;
                var fromState = ephem.GetState(originId, tDep);

                for (int k = 0; k <= ArrIntervals; k++)
                {
                    double tArr = arrStart + k * arrStep;
                    double tof  = tArr - tDep;
                    // Game's filter: num2 > num + minFlightTime
                    if (tof <= tofMin) continue;

                    var sol = solver.Solve(fromState.Position, arrStates[k].Position,
                                          fromState.Velocity, mu, tof);
                    if (!sol.Ok) continue;

                    double v1 = (sol.V1 - fromState.Velocity).Magnitude;
                    double v2 = (arrStates[k].Velocity - sol.V2).Magnitude;
                    double dv = v1 + v2;

                    // Optimal: game's DataGridToValueToSort3 (deltaVPickerButtonOptimalRoundResult=0.9)
                    double score3 = (Math.Abs(bestOptJ - j) < 5) ? dv : dv / 0.9;
                    if (score3 < bestDv)
                    {
                        bestDv     = dv;
                        bestDepOpt = tDep;
                        bestArrOpt = tArr;
                        bestOptJ   = j;
                    }

                    // Fastest: earliest arrival within dvCap; arrivals are in ascending order
                    // within each departure column so we track the global minimum arrival.
                    if (dv <= dvCap && tArr < earliestArr)
                    {
                        earliestArr = tArr;
                        fastDv  = dv;
                        fastDep = tDep;
                        fastArr = tArr;
                    }
                }
            }

            LaunchWindow? optimal = bestDv < double.MaxValue
                ? new LaunchWindow(bestDepOpt, bestArrOpt, bestDv * dvToKmS)
                : (LaunchWindow?)null;

            LaunchWindow? fastest = earliestArr < double.MaxValue
                ? new LaunchWindow(fastDep, fastArr, fastDv * dvToKmS)
                : (LaunchWindow?)null;

            return (optimal, fastest, tSynodic);
        }
    }
}
