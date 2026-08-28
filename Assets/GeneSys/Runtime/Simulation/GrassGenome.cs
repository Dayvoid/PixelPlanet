using System;
using System.Text;
using UnityEngine;

namespace GeneSys.Simulation
{
    /// <summary>
    /// 12-gene, 8-bit-per-gene packing shared with HLSL. Expression matches flora:
    /// 1 + (gene/255 - 0.5) * 2 * geneExpressionRange.
    /// </summary>
    public static class GrassGenome
    {
        public const int GeneCount = 12;
        public const int SlotCount = 3;
        public const int SlicesPerSlot = 4;
        public const int SliceCount = SlotCount * SlicesPerSlot;
        public const int PropaguleSliceCount = 4;
        public const uint DetritusId = 131;

        public const int LifeOffset = 0;
        public const int GenomeOffset = 1;
        public const int PhenologyOffset = 2;
        public const int DonorOffset = 3;

        public const uint StageEmpty = 0;
        public const uint StageSeedling = 1;
        public const uint StageAdult = 2;
        public const uint StageFlowering = 3;
        public const uint StageSeeding = 4;
        public const uint StageDead = 5;

        public const int GeneTempOptimum = 0;
        public const int GeneTempTolerance = 1;
        public const int GeneMoistureOptimum = 2;
        public const int GeneMoistureTolerance = 3;
        public const int GeneLightAffinity = 4;
        public const int GeneReproduction = 5;
        public const int GeneMetabolic = 6;
        public const int GeneBladeHeight = 7;
        public const int GeneBladeColor = 8;
        public const int GeneFlowerSize = 9;
        public const int GeneRootAffinity = 10;
        public const int GeneMutation = 11;

        public static readonly string[] GeneNames =
        {
            "Temp optimum", "Temp tolerance", "Moisture optimum", "Moisture tolerance",
            "Light affinity", "Reproduction", "Metabolic rate", "Blade height",
            "Blade color", "Flower size", "Root affinity", "Mutation rate"
        };

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct Packed
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;

            public static Packed FromUint4(uint x, uint y, uint z, uint w) =>
                new() { X = x, Y = y, Z = z, W = w };

            public static Packed FromFloatBits(Vector4 bits) => new()
            {
                X = unchecked((uint)BitConverter.SingleToInt32Bits(bits.x)),
                Y = unchecked((uint)BitConverter.SingleToInt32Bits(bits.y)),
                Z = unchecked((uint)BitConverter.SingleToInt32Bits(bits.z)),
                W = unchecked((uint)BitConverter.SingleToInt32Bits(bits.w))
            };
        }

        public static byte DecodeGene(Packed genome, int index)
        {
            index = System.Math.Clamp(index, 0, GeneCount - 1);
            uint word = index < 4 ? genome.X : (index < 8 ? genome.Y : genome.Z);
            int shift = (index & 3) * 8;
            return (byte)((word >> shift) & 255u);
        }

        public static Packed EncodeGene(Packed genome, int index, byte value)
        {
            index = System.Math.Clamp(index, 0, GeneCount - 1);
            int shift = (index & 3) * 8;
            uint mask = ~(255u << shift);
            uint packed = (uint)value << shift;
            if (index < 4) genome.X = (genome.X & mask) | packed;
            else if (index < 8) genome.Y = (genome.Y & mask) | packed;
            else genome.Z = (genome.Z & mask) | packed;
            return genome;
        }

        public static uint Stage(Packed genome) => genome.W & 255u;
        public static uint Generation(Packed genome) => (genome.W >> 8) & 255u;
        public static uint Lineage(Packed genome) => (genome.W >> 16) & 255u;
        public static uint ToxinDose(Packed genome) => (genome.W >> 24) & 255u;

        public static Packed PackMeta(Packed genome, uint stage, uint generation, uint lineage, uint toxinDose)
        {
            genome.W = (stage & 255u) | ((generation & 255u) << 8) | ((lineage & 255u) << 16) | ((toxinDose & 255u) << 24);
            return genome;
        }

        public static Packed FromFloatBits(Vector4 bits) => Packed.FromFloatBits(bits);

        public static Packed Sanitize(Packed genome)
        {
            uint stage = Stage(genome);
            if (stage > StageDead) stage = StageEmpty;
            return PackMeta(genome, stage, Generation(genome), Lineage(genome), ToxinDose(genome));
        }

        public static bool IsValidStage(uint stage) => stage <= StageDead;
        public static bool IsOccupied(uint stage) => stage != StageEmpty && stage != StageDead;
        public static bool IsLiving(uint stage) => stage == StageSeedling || stage == StageAdult
            || stage == StageFlowering || stage == StageSeeding;

        public static float ExpressFactor(byte gene, float range)
        {
            float r = UnityEngine.Mathf.Clamp01(range);
            return 1f + (gene / 255f - 0.5f) * 2f * r;
        }

        public static float ExpressShift(byte gene, float range) =>
            (gene / 255f - 0.5f) * 2f * range;

        public static int RootCount(Packed genome)
        {
            byte gene = DecodeGene(genome, GeneRootAffinity);
            return 1 + (int)System.Math.Min(2, gene * 3 / 256);
        }

        public static uint RootMask(Packed genome, int theta, int radius, int slot, int seed)
        {
            int count = RootCount(genome);
            uint hash = Hash((uint)(theta * 73856093 + radius * 19349663 + slot * 83492791 + seed * 31337 + Lineage(genome)));
            uint order = hash % 6u;
            int[] permutation = order switch
            {
                0 => new[] { 0, 1, 2 },
                1 => new[] { 0, 2, 1 },
                2 => new[] { 1, 0, 2 },
                3 => new[] { 1, 2, 0 },
                4 => new[] { 2, 0, 1 },
                _ => new[] { 2, 1, 0 }
            };
            uint mask = 0;
            for (int i = 0; i < count; i++)
                mask |= 1u << permutation[i];
            return mask;
        }

        public static Packed Mutate(Packed genome, float baseMutationRate, float toxinMutationScale, uint salt)
        {
            genome = Sanitize(genome);
            float toxin = ToxinDose(genome) / 255f;
            float mutationGene = ExpressFactor(DecodeGene(genome, GeneMutation), 1f);
            float rate = UnityEngine.Mathf.Max(0f, baseMutationRate) * (1f + toxin * UnityEngine.Mathf.Max(0f, toxinMutationScale)) * mutationGene;
            for (int i = 0; i < GeneCount; i++)
            {
                byte next = MutateGene(DecodeGene(genome, i), rate, salt + (uint)i * 83492791u);
                genome = EncodeGene(genome, i, next);
            }
            return genome;
        }

        public static Packed Combine(Packed mother, Packed donor, uint salt)
        {
            var child = new Packed();
            for (int i = 0; i < GeneCount; i++)
            {
                uint hashed = Hash(salt + (uint)i * 83492791u);
                float unit = (hashed & 0x00ffffff) / 16777215f;
                byte gene = unit < 0.5f ? DecodeGene(mother, i) : DecodeGene(donor, i);
                child = EncodeGene(child, i, gene);
            }
            return child;
        }

        public static Packed SaturateToxin(Packed genome, float addedDose)
        {
            uint dose = (uint)UnityEngine.Mathf.Clamp(ToxinDose(genome) + addedDose, 0f, 255f);
            return PackMeta(genome, Stage(genome), Generation(genome), Lineage(genome), dose);
        }

        public static string DescribeStage(uint stage) => stage switch
        {
            StageSeedling => "Seedling",
            StageAdult => "Adult",
            StageFlowering => "Flowering",
            StageSeeding => "Seeding",
            StageDead => "Dead",
            _ => "Empty"
        };

        public static string DescribeGenes(Packed genome, float expressionRange)
        {
            var text = new StringBuilder();
            for (int i = 0; i < GeneCount; i++)
            {
                if (i > 0) text.Append("  ");
                float pct = (DecodeGene(genome, i) / 255f - 0.5f) * 2f * expressionRange * 100f;
                text.Append(GeneNames[i]);
                text.Append(' ');
                text.Append(pct >= 0f ? "+" : "");
                text.Append(pct.ToString("F0"));
                text.Append('%');
            }
            return text.ToString();
        }

        public static byte MutateGene(byte gene, float mutationRate, uint salt)
        {
            float rate = UnityEngine.Mathf.Max(0f, mutationRate);
            if (rate <= 0f) return gene;
            uint hashed = Hash(salt ^ (uint)(gene * 747796405 + 2891336453));
            float unit = (hashed & 0x00ffffff) / 16777215f;
            int step = (int)System.Math.Round((unit * 2f - 1f) * rate * 255f);
            return (byte)System.Math.Clamp(gene + step, 0, 255);
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return value;
        }
    }
}
