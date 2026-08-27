using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GeneSys.Simulation
{
    public enum OrganismHistoryKind : uint
    {
        Birth = 0,
        Reproduce = 1,
        Death = 2,
        Mate = 3
    }

    public enum OrganismHistoryCause : uint
    {
        None = 0,
        Desiccation = 1,
        Toxin = 2,
        Fire = 3,
        Painted = 4,
        Consumed = 5,
        Starvation = 6,
        Dehydration = 7,
        Laid = 8,
        Mated = 9,
        Hatched = 10
    }

    public sealed class OrganismHistoryLog
    {
        public const int Capacity = 512;

        [StructLayout(LayoutKind.Sequential)]
        public struct GpuEvent
        {
            public uint Tick;
            public uint Kind;
            public uint Generation;
            public uint Lineage;
            public uint Cause;
            public uint Pad0;
            public uint Pad1;
            public uint Pad2;
            public const int Stride = 32;
        }

        public readonly struct Entry
        {
            public readonly int Tick;
            public readonly OrganismHistoryKind Kind;
            public readonly uint Generation;
            public readonly uint Lineage;
            public readonly OrganismHistoryCause Cause;

            public Entry(int tick, OrganismHistoryKind kind, uint generation, uint lineage, OrganismHistoryCause cause)
            {
                Tick = tick;
                Kind = kind;
                Generation = generation;
                Lineage = lineage;
                Cause = cause;
            }

            public string Format()
            {
                string kindLabel = Kind switch
                {
                    OrganismHistoryKind.Birth => "Birth",
                    OrganismHistoryKind.Reproduce => "Reproduce",
                    OrganismHistoryKind.Death => "Death",
                    OrganismHistoryKind.Mate => "Mate",
                    _ => Kind.ToString()
                };
                string causeLabel = Cause switch
                {
                    OrganismHistoryCause.None => null,
                    OrganismHistoryCause.Desiccation => "desiccation",
                    OrganismHistoryCause.Toxin => "toxin",
                    OrganismHistoryCause.Fire => "fire",
                    OrganismHistoryCause.Painted => "painted",
                    OrganismHistoryCause.Consumed => "consumed",
                    OrganismHistoryCause.Starvation => "starvation",
                    OrganismHistoryCause.Dehydration => "dehydration",
                    OrganismHistoryCause.Laid => "laid",
                    OrganismHistoryCause.Mated => "mated",
                    OrganismHistoryCause.Hatched => "hatched",
                    _ => Cause.ToString().ToLowerInvariant()
                };
                string action = causeLabel == null ? kindLabel : $"{kindLabel} ({causeLabel})";
                return $"Tick {Tick}  {action}  gen {Generation} / lineage {Lineage}";
            }
        }

        private readonly List<Entry> entries = new(Capacity);

        public int Version { get; private set; }
        public IReadOnlyList<Entry> Entries => entries;

        public void Clear()
        {
            if (entries.Count == 0) return;
            entries.Clear();
            Version++;
        }

        public void AppendFromGpu(GpuEvent[] events, int count)
        {
            if (events == null || count <= 0) return;
            int n = Math.Min(count, events.Length);
            for (int i = 0; i < n; i++)
            {
                GpuEvent gpu = events[i];
                entries.Add(new Entry(
                    (int)gpu.Tick,
                    (OrganismHistoryKind)gpu.Kind,
                    gpu.Generation,
                    gpu.Lineage,
                    (OrganismHistoryCause)gpu.Cause));
            }

            int overflow = entries.Count - Capacity;
            if (overflow > 0)
                entries.RemoveRange(0, overflow);
            Version++;
        }
    }
}
