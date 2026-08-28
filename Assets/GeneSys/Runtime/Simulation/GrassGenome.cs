using System;
using System.Text;
using UnityEngine;

namespace GeneSys.Simulation
{
    /// <summary>
    /// 12-gene, 8-bit-per-gene packing shared with HLSL. Expression matches flora:
    /// 1 + (gene/255 - 0.5) * 2 * geneExpressionRange. Meta packing matches flora
    /// (stage | generation | lineage | toxinDose). Recombination matches fauna Combine.
    /// </summary>
    public static class GrassGenome
    {
        public const int GeneCount = 12;
        public const int SlotCount = 3;
        public const int SlicesPerSlot = 4;
        public const int GrassSliceCount = SlotCount * SlicesPerSlot;
        public const int PropaguleSliceCount = 3;

        public const int LifeOffset = 0;
        public const int GenomeOffset = 1;
        public const int TimingOffset = 2;
        public const int DonorOffset = 3;

        public const uint StageEmpty = 0;
        public const uint StageJuvenile = 1;
        public const uint StageAdult = 2;

        public const int GeneTempOptimum = 0;
        public const int GeneTempTolerance = 1;
        public const int GeneMoistureOptimum = 2;
        public const int GeneMoistureTolerance = 3;
        public const int GeneLightAffinity = 4;
        public const int GeneBladeHeight = 5;
        public const int GeneBladeColor = 6;
        public const int GeneFlowerSize = 7;
        public const int GeneFlowerColor = 8;
        public const int GeneRootArchitecture = 9;
        public const int GeneFloweringCadence = 10;
        public const int GeneMutation = 11;

        public const uint TimingFloweringBit = 1u << 3;
        public const uint TimingPollinatedBit = 1u << 4;
        public const uint TimingReleasedBit = 1u << 5;
        public const uint TimingRootMask = 7u;
        public const uint DonorValidBit = 1u;

        public static readonly string[] GeneNames =
        {
            "Temp optimum", "Temp tolerance", "Moisture optimum", "Moisture tolerance",
            "Light affinity", "Blade height", "Blade color", "Flower size",
            "Flower color", "Root architecture", "Flowering cadence", "Mutation rate"
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

        public static int Slice(int slot, int field) =>
            System.Math.Clamp(slot, 0, SlotCount - 1) * SlicesPerSlot + System.Math.Clamp(field, 0, SlicesPerSlot - 1);

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
            if (stage > StageAdult) stage = StageEmpty;
            return PackMeta(genome, stage, Generation(genome), Lineage(genome), ToxinDose(genome));
        }

        public static bool IsValidStage(uint stage) => stage <= StageAdult;
        public static bool IsLivingStage(uint stage) => stage == StageJuvenile || stage == StageAdult;

        public static float ExpressFactor(byte gene, float range)
        {
            float r = UnityEngine.Mathf.Clamp01(range);
            return 1f + (gene / 255f - 0.5f) * 2f * r;
        }

        public static float ExpressShift(byte gene, float range) =>
            (gene / 255f - 0.5f) * 2f * range;

        public static uint SelectRootMask(Packed genome, int cellX, int cellY, int slot)
        {
            byte gene = DecodeGene(genome, GeneRootArchitecture);
            uint count = 1u + (uint)(gene % 3);
            uint start = (uint)((gene / 3 + cellX * 3 + cellY * 5 + slot * 7) % 3);
            uint mask = 0u;
            for (uint i = 0; i < count; i++)
                mask |= 1u << (int)((start + i) % 3u);
            return mask & TimingRootMask;
        }

        public static uint TimingFlags(float packed) => (uint)Mathf.Round(Mathf.Max(0f, packed));
        public static uint RootMask(uint packedTiming) => packedTiming & TimingRootMask;
        public static bool IsFlowering(uint packedTiming) => (packedTiming & TimingFloweringBit) != 0u;
        public static bool IsPollinated(uint packedTiming) => (packedTiming & TimingPollinatedBit) != 0u;
        public static bool HasReleased(uint packedTiming) => (packedTiming & TimingReleasedBit) != 0u;
        public static bool HasDonor(Packed donor) => (donor.W & DonorValidBit) != 0u;

        public static uint PackTimingFlags(uint rootMask, bool flowering, bool pollinated, bool released)
        {
            uint packed = rootMask & TimingRootMask;
            if (flowering) packed |= TimingFloweringBit;
            if (pollinated) packed |= TimingPollinatedBit;
            if (released) packed |= TimingReleasedBit;
            return packed;
        }

        public static uint DominantRare(uint traitsA, uint traitsB = 0u, uint traitsC = 0u)
        {
            uint combined = MycologyTraits.Sanitize(traitsA | traitsB | traitsC);
            if (combined == 0u) return 0u;
            uint[] flags =
            {
                MycologyTraits.DroughtResistant, MycologyTraits.HeatResistant, MycologyTraits.ElectricResistant,
                MycologyTraits.DroughtProne, MycologyTraits.HeatProne, MycologyTraits.ElectricProne
            };
            uint best = 0u;
            uint bestRank = 0u;
            for (int i = 0; i < flags.Length; i++)
            {
                uint rank = 6u - (uint)i;
                if ((combined & flags[i]) != 0u && rank > bestRank)
                {
                    best = flags[i];
                    bestRank = rank;
                }
            }
            return best;
        }

        public static float FloweringDays(Packed genome)
        {
            return 3f + DecodeGene(genome, GeneFloweringCadence) / 255f;
        }

        public static float SeedReleaseDays(Packed genome)
        {
            return 1f + DecodeGene(genome, GeneFloweringCadence) / 255f;
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

        public static Packed Combine(Packed mother, Packed partner, uint salt)
        {
            mother = Sanitize(mother);
            partner = Sanitize(partner);
            var child = new Packed();
            for (int i = 0; i < GeneCount; i++)
            {
                float unit = (Hash(salt + (uint)i * 83492791u) & 0x00ffffff) / 16777215f;
                byte gene = unit < 0.5f ? DecodeGene(mother, i) : DecodeGene(partner, i);
                child = EncodeGene(child, i, gene);
            }
            return child;
        }

        public static Packed Inherit(Packed mother, Packed partner, bool hasPartner, float baseMutationRate, float toxinMutationScale, uint salt)
        {
            Packed child = hasPartner ? Combine(mother, partner, salt) : mother;
            child = Mutate(child, baseMutationRate, toxinMutationScale, salt + 17u);
            uint generation = (Generation(mother) + 1u) & 255u;
            return PackMeta(child, StageJuvenile, generation, Lineage(mother), ToxinDose(mother) / 2u);
        }

        public static Packed SaturateToxin(Packed genome, float addedDose)
        {
            uint dose = (uint)UnityEngine.Mathf.Clamp(ToxinDose(genome) + addedDose, 0f, 255f);
            return PackMeta(genome, Stage(genome), Generation(genome), Lineage(genome), dose);
        }

        public static string DescribeStage(uint stage) => stage switch
        {
            StageJuvenile => "Juvenile",
            StageAdult => "Adult",
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
