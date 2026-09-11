using UnityEngine;

namespace GeneSys.Simulation.Climate
{
    public static class ClimateGrid
    {
        public const int MinBins = 8;
        public const int MaxBins = 128;
        public const int DefaultBins = 32;
        public const int ShellCount = 3;
        public const int ColumnSlotsPerTheta = 5;
        public const int StateSlotsPerBin = 3;
        public const int ShellCrust = 0;
        public const int ShellLowerAir = 1;
        public const int ShellUpperAir = 2;
        public const int ColumnSurface = 3;
        public const int ColumnLand = 4;
        public const int StateMemory = 0;
        public const int StateLand = 1;
        public const int StateDiag = 2;

        public static int ClampBinCount(int bins) => Mathf.Clamp(bins, MinBins, MaxBins);

        public static int WrapTheta(int theta, int width)
        {
            width = Mathf.Max(1, width);
            int wrapped = theta % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }

        public static int BinOf(int theta, int width, int bins)
        {
            bins = ClampBinCount(bins);
            width = Mathf.Max(1, width);
            theta = WrapTheta(theta, width);
            return (int)((long)theta * bins / width);
        }

        public static void ThetaRange(int bin, int width, int bins, out int start, out int endExclusive)
        {
            bins = ClampBinCount(bins);
            width = Mathf.Max(1, width);
            bin = ((bin % bins) + bins) % bins;
            start = (int)((long)bin * width / bins);
            endExclusive = (int)((long)(bin + 1) * width / bins);
        }

        public static int ShellOf(float radius01, float atmosphereStart)
        {
            if (radius01 < atmosphereStart)
                return ShellCrust;
            float mid = (atmosphereStart + 1f) * 0.5f;
            return radius01 < mid ? ShellLowerAir : ShellUpperAir;
        }

        public static int ColumnIndex(int theta, int slot, int width) =>
            WrapTheta(theta, width) * ColumnSlotsPerTheta + slot;

        public static int StateIndex(int bin, int slot) =>
            bin * StateSlotsPerBin + slot;

        public static int ColumnBufferCount(int width) =>
            Mathf.Max(1, width) * ColumnSlotsPerTheta;

        public static int StateBufferCount() => MaxBins * StateSlotsPerBin;
    }
}
