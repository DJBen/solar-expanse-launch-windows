using System;

namespace SolarExpanseLaunchWindows
{
    internal interface IGameClock
    {
        DateTime CurrentTime { get; }
        double PhysicalTime { get; }
        double SecondsPerPhysicsSecond { get; }
        double TimeScale { get; }   // one year in physics seconds
        void PauseGame();
    }
}
