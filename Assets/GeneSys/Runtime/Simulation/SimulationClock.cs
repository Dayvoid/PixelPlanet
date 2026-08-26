using System;
using UnityEngine;

namespace GeneSys.Simulation
{
    [Serializable]
    public sealed class SimulationClock
    {
        private float accumulator;
        private bool stepRequested;

        public const float FastForwardSpeed = 5f;

        public bool IsRunning { get; private set; } = true;
        public float Speed { get; private set; } = 1f;
        public long TickCount { get; private set; }

        public void SetRunning(bool running) => IsRunning = running;
        public void Toggle() => IsRunning = !IsRunning;
        public void RequestStep() { stepRequested = true; IsRunning = false; }
        public void SetSpeed(float speed) => Speed = Mathf.Clamp(speed, 0.05f, 16f);
        public void Reset() { accumulator = 0f; stepRequested = false; TickCount = 0; }
        public void SetTickCount(long value) { TickCount = Math.Max(0L, value); accumulator = 0f; }

        public int Advance(float unscaledDeltaTime, float ticksPerSecond, Action<float> tick)
        {
            float fixedDelta = 1f / Mathf.Max(1f, ticksPerSecond);
            if (stepRequested)
            {
                stepRequested = false;
                tick(fixedDelta);
                TickCount++;
                return 1;
            }
            if (!IsRunning) return 0;

            accumulator += Mathf.Min(unscaledDeltaTime, 0.25f) * Speed;
            int count = 0;
            while (accumulator >= fixedDelta && count < 8)
            {
                accumulator -= fixedDelta;
                tick(fixedDelta);
                TickCount++;
                count++;
            }
            if (count == 8) accumulator = Mathf.Min(accumulator, fixedDelta);
            return count;
        }
    }
}
