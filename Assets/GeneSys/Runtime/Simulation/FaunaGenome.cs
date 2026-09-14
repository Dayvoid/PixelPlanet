using System;
using System.Text;
using UnityEngine;

namespace GeneSys.Simulation
{
    /// <summary>
    /// 12-gene, 8-bit-per-gene packing shared with HLSL. Expression matches flora:
    /// 1 + (gene/255 - 0.5) * 2 * geneExpressionRange.
    /// </summary>
    public static class FaunaGenome
    {
        public const int GeneCount = 12;
        public const uint CricketId = 129;
        public const uint EggId = 130;
        public const uint WaspId = 132;
        public const uint WaspEggId = 133;

        public const uint ArchetypeNone = 0;
        public const uint ArchetypeCricket = 1;
        public const uint ArchetypeWasp = 2;

        public const int VitalsSlice = 0;
        public const int MotionSlice = 1;
        public const int GenomeSlice = 2;
        public const int PartnerSlice = 3;

        public const int CricketVitalsSlice = 0;
        public const int CricketMotionSlice = 1;
        public const int CricketGenomeSlice = 2;
        public const int CricketPartnerSlice = 3;

        public const int WaspVitalsSlice = 4;
        public const int WaspMotionSlice = 5;
        public const int WaspGenomeSlice = 6;
        public const int WaspPartnerSlice = 7;
        public const int WaspCargoSlice = 8;
        public const int WaspCargoSlots = 3;
        public const int FaunaSliceCount = 11;
        public const int SliceCount = 11;
        public const int ClaimSliceCount = 5;

        public const uint StageEmpty = 0;
        public const uint StageEgg = 1;
        public const uint StageJuvenile = 2;
        public const uint StageAdult = 3;
        public const uint StageDead = 4;

        public const uint BehaviorIdle = 0;
        public const uint BehaviorWander = 1;
        public const uint BehaviorForage = 2;
        public const uint BehaviorMateSeek = 3;
        public const uint BehaviorFlee = 4;
        public const uint BehaviorAirborne = 5;

        public const int GeneJumpStrength = 0;
        public const int GeneDryMass = 1;
        public const int GeneDrag = 2;
        public const int GeneMoistureLoad = 3;
        public const int GeneHydrationRetention = 4;
        public const int GeneMetabolism = 5;
        public const int GeneCalorieCapacity = 6;
        public const int GeneNutrientSense = 7;
        public const int GeneHearing = 8;
        public const int GeneThreatResponse = 9;
        public const int GeneFertility = 10;
        public const int GeneMutation = 11;

        public static readonly string[] GeneNames =
        {
            "Jump strength", "Dry mass", "Drag", "Moisture load",
            "Hydration retention", "Metabolism", "Calorie capacity", "Nutrient sense",
            "Hearing", "Threat response", "Fertility", "Mutation rate"
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
        public static uint Behavior(Packed genome) => (genome.W >> 8) & 255u;
        public static uint Generation(Packed genome) => (genome.W >> 16) & 255u;
        public static uint Lineage(Packed genome) => (genome.W >> 24) & 255u;

        public static Packed PackMeta(Packed genome, uint stage, uint behavior, uint generation, uint lineage)
        {
            genome.W = (stage & 255u) | ((behavior & 255u) << 8) | ((generation & 255u) << 16) | ((lineage & 255u) << 24);
            return genome;
        }

        public static Packed FromFloatBits(Vector4 bits) => Packed.FromFloatBits(bits);

        public static Packed Sanitize(Packed genome)
        {
            uint stage = Stage(genome);
            if (stage > StageDead) stage = StageEmpty;
            uint behavior = Behavior(genome);
            if (behavior > BehaviorAirborne) behavior = BehaviorIdle;
            return PackMeta(genome, stage, behavior, Generation(genome), Lineage(genome));
        }

        public static bool IsValidStage(uint stage) => stage <= StageDead;
        public static bool IsLivingStage(uint stage) =>
            stage == StageEgg || stage == StageJuvenile || stage == StageAdult;

        public static float ExpressFactor(byte gene, float range)
        {
            float r = UnityEngine.Mathf.Clamp01(range);
            return 1f + (gene / 255f - 0.5f) * 2f * r;
        }

        public static float ExpressShift(byte gene, float range) =>
            (gene / 255f - 0.5f) * 2f * range;

        public static int ClutchSize(Packed genome, int minClutch, int maxClutch, float expressionRange)
        {
            int lo = System.Math.Min(minClutch, maxClutch);
            int hi = System.Math.Max(minClutch, maxClutch);
            float factor = ExpressFactor(DecodeGene(genome, GeneFertility), expressionRange);
            float t = UnityEngine.Mathf.Clamp01((factor - (1f - expressionRange)) / UnityEngine.Mathf.Max(1e-4f, 2f * UnityEngine.Mathf.Max(0.05f, expressionRange)));
            return UnityEngine.Mathf.Clamp(lo + Mathf.RoundToInt(t * (hi - lo)), lo, hi);
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

        public static Packed Mutate(Packed genome, float baseMutationRate, uint salt)
        {
            genome = Sanitize(genome);
            float mutationGene = ExpressFactor(DecodeGene(genome, GeneMutation), 1f);
            float rate = UnityEngine.Mathf.Max(0f, baseMutationRate) * mutationGene;
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

        public static Packed Inherit(Packed mother, Packed partner, bool hasPartner, float baseMutationRate, uint salt)
        {
            Packed child = hasPartner ? Combine(mother, partner, salt) : mother;
            child = Mutate(child, baseMutationRate, salt + 17u);
            return PackMeta(child, StageEgg, BehaviorIdle, (Generation(mother) + 1u) & 255u, Lineage(mother));
        }

        public static string DescribeStage(uint stage) => stage switch
        {
            StageEgg => "Egg",
            StageJuvenile => "Juvenile",
            StageAdult => "Adult",
            StageDead => "Dead",
            _ => "Empty"
        };

        public static string DescribeBehavior(uint behavior) => behavior switch
        {
            BehaviorWander => "Wander",
            BehaviorForage => "Forage",
            BehaviorMateSeek => "Mate seek",
            BehaviorFlee => "Flee",
            BehaviorAirborne => "Airborne",
            _ => "Idle"
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
