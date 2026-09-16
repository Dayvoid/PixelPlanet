using System;
using System.Collections.Generic;

namespace GeneSys.Simulation.Scenarios
{
    public interface IScenarioCue
    {
        bool IsSpent { get; }
        void Reset();
        void Sync(long tick);
        void TryFire(SimulationHost host, long tick);
    }

    public sealed class TickCue : IScenarioCue
    {
        private readonly long tick;
        private readonly Action<SimulationHost> action;
        private bool spent;

        public TickCue(long tick, Action<SimulationHost> action)
        {
            this.tick = tick;
            this.action = action;
        }

        public bool IsSpent => spent;

        public void Reset() => spent = false;

        public void Sync(long currentTick)
        {
            if (tick <= currentTick)
                spent = true;
        }

        public void TryFire(SimulationHost host, long currentTick)
        {
            if (spent || currentTick != tick) return;
            spent = true;
            action?.Invoke(host);
        }
    }

    public sealed class ConditionCue : IScenarioCue
    {
        private readonly Func<SimulationHost, long, bool> when;
        private readonly Action<SimulationHost> action;
        private readonly bool once;
        private bool spent;

        public ConditionCue(Func<SimulationHost, long, bool> when, Action<SimulationHost> action, bool once = true)
        {
            this.when = when;
            this.action = action;
            this.once = once;
        }

        public bool IsSpent => spent;

        public void Reset() => spent = false;

        public void Sync(long tick)
        {
        }

        public void TryFire(SimulationHost host, long currentTick)
        {
            if (spent || when == null || !when(host, currentTick)) return;
            if (once) spent = true;
            action?.Invoke(host);
        }
    }

    public sealed class ScenarioTimeline
    {
        private readonly List<IScenarioCue> cues = new();

        public int CueCount => cues.Count;

        public void Add(IScenarioCue cue)
        {
            if (cue != null)
                cues.Add(cue);
        }

        public void Reset()
        {
            for (int i = 0; i < cues.Count; i++)
                cues[i].Reset();
        }

        public void Sync(long tick)
        {
            for (int i = 0; i < cues.Count; i++)
                cues[i].Sync(tick);
        }

        public void Advance(SimulationHost host, long tick)
        {
            for (int i = 0; i < cues.Count; i++)
                cues[i].TryFire(host, tick);
        }
    }
}
