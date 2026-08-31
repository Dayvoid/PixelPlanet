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

        public const int MaxTicksPerFrame = 2;

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

            float dt = Mathf.Min(unscaledDeltaTime, 0.1f);
            accumulator += dt * Speed;
            int count = 0;

            if (Speed <= 1f)
            {
                while (accumulator >= fixedDelta && count < MaxTicksPerFrame)
                {
                    accumulator -= fixedDelta;
                    tick(fixedDelta);
                    TickCount++;
                    count++;
                }
                if (count == MaxTicksPerFrame)
                    accumulator = Mathf.Min(accumulator, fixedDelta);
            }
            else
            {
                float targetTicks = accumulator / fixedDelta;
                if (targetTicks >= 1f)
                {
                    int stepsToRun = targetTicks >= 2f ? MaxTicksPerFrame : 1;
                    float stepDt = Mathf.Min(accumulator / stepsToRun, fixedDelta * Speed);
                    for (int i = 0; i < stepsToRun; i++)
                    {
                        tick(stepDt);
                        TickCount++;
                        count++;
                    }
                    accumulator = 0f;
                }
            }
            return count;
        }
    }
}
