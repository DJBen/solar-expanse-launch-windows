using System;
using System.Reflection;
using Manager;
using UnityEngine;

namespace SolarExpanseLaunchWindows
{
    internal class GameClock : IGameClock
    {
        public DateTime CurrentTime
        {
            get
            {
                var tc = MonoBehaviourSingleton<TimeController>.Instance;
                return tc != null ? tc.CurrentTime : DateTime.MinValue;
            }
        }

        public double PhysicalTime
        {
            get
            {
                var ge = GravityEngine.Instance();
                return ge != null ? ge.GetPhysicalTimeDouble() : 0;
            }
        }

        public double SecondsPerPhysicsSecond
        {
            get
            {
                double spp = GravityScaler.GetGameSecondPerPhysicsSecond();
                return spp > 0 ? spp : 1;
            }
        }

        public double TimeScale => GravityEngine.Instance()?.timeScale ?? 0;

        public void PauseGame()
        {
            var tc = MonoBehaviourSingleton<TimeController>.Instance;
            if (tc == null) return;
            var m = tc.GetType().GetMethod("PauseGame",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            m?.Invoke(tc, null);
        }
    }
}
