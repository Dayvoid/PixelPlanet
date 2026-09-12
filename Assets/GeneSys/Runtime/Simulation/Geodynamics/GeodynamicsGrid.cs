using UnityEngine;

namespace GeneSys.Simulation.Geodynamics
{
    public static class GeodynamicsGrid
    {
        public const int MinAngularBins = 16;
        public const int MaxAngularBins = 128;
        public const int DefaultAngularBins = 64;
        public const int MinRadialBins = 8;
        public const int MaxRadialBins = 32;
        public const int DefaultRadialBins = 16;
        public const int StateSlotsPerCell = 2;
        public const int SlotReservoir = 0;
        public const int SlotKinematics = 1;
        public const int EventTypeNone = 0;
        public const int EventTypeEarthquake = 1;
        public const int EventTypeVolcanic = 2;
        public const int EventTypeHydrothermal = 3;
        public const int MaxEventSeeds = 8;
        public const int EventSeedStride = 16;

        public static int ClampAngularBins(int bins) => Mathf.Clamp(bins, MinAngularBins, MaxAngularBins);

        public static int ClampRadialBins(int bins) => Mathf.Clamp(bins, MinRadialBins, MaxRadialBins);

        public static int WrapBin(int bin, int bins)
        {
            bins = Mathf.Max(1, bins);
            int wrapped = bin % bins;
            return wrapped < 0 ? wrapped + bins : wrapped;
        }

        public static int AngularBinOf(int theta, int width, int bins)
        {
            bins = ClampAngularBins(bins);
            width = Mathf.Max(1, width);
            theta = WrapBin(theta, width);
            return (int)((long)theta * bins / width);
        }

        public static void ThetaRange(int bin, int width, int bins, out int start, out int endExclusive)
        {
            bins = ClampAngularBins(bins);
            width = Mathf.Max(1, width);
            bin = WrapBin(bin, bins);
            start = (int)((long)bin * width / bins);
            endExclusive = (int)((long)(bin + 1) * width / bins);
        }

        public static int RadialBinOf(float radius01, float atmosphereStart, int bins)
        {
            bins = ClampRadialBins(bins);
            float outer = Mathf.Max(0.05f, atmosphereStart);
            float t = Mathf.Clamp01(radius01 / outer);
            return Mathf.Clamp((int)(t * bins), 0, bins - 1);
        }

        public static void RadialRange(int bin, int height, float atmosphereStart, int bins, out int start, out int endExclusive)
        {
            bins = ClampRadialBins(bins);
            height = Mathf.Max(1, height);
            bin = Mathf.Clamp(bin, 0, bins - 1);
            float outer = Mathf.Max(0.05f, atmosphereStart);
            int atmosphereY = Mathf.Clamp(Mathf.RoundToInt(outer * (height - 1)), 1, height);
            start = (int)((long)bin * atmosphereY / bins);
            endExclusive = (int)((long)(bin + 1) * atmosphereY / bins);
            if (endExclusive <= start)
                endExclusive = Mathf.Min(height, start + 1);
        }

        public static int StateIndex(int angularBin, int radialBin, int slot, int angularBins, int radialBins)
        {
            angularBins = ClampAngularBins(angularBins);
            radialBins = ClampRadialBins(radialBins);
            angularBin = WrapBin(angularBin, angularBins);
            radialBin = Mathf.Clamp(radialBin, 0, radialBins - 1);
            slot = Mathf.Clamp(slot, 0, StateSlotsPerCell - 1);
            return ((angularBin * radialBins) + radialBin) * StateSlotsPerCell + slot;
        }

        public static int EventIndex(int angularBin, int radialBin, int angularBins, int radialBins) =>
            StateIndex(angularBin, radialBin, SlotReservoir, angularBins, radialBins) / StateSlotsPerCell;

        public static int StateBufferCount() => MaxAngularBins * MaxRadialBins * StateSlotsPerCell;

        public static int EventBufferCount() => MaxAngularBins * MaxRadialBins;

        public static int ColumnBufferCount() => MaxAngularBins * MaxRadialBins;
    }
}
