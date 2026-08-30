using System;
using System.Text;
using UnityEngine;

namespace GeneSys.Simulation
{
    /// <summary>
    /// 12-gene packing shared with HLSL. Meta packing matches flora
    /// (stage | generation | lineage | toxinDose). Topology packing is separate.
    /// </summary>
    public static class TreeGenome
    {
        public const int GeneCount = 12;
        public const int SliceCount = 3;
        public const int PhysiologySlice = 0;
        public const int TopologySlice = 1;
        public const int GenomeSlice = 2;

        public const uint LeafId = 134;
        public const uint WoodId = 135;

        public const uint StageEmpty = 0;
        public const uint StageSprout = 1;
        public const uint StageSapling = 2;
        public const uint StageTree = 3;
        public const uint StageDead = 4;

        public const uint RoleNone = 0;
        public const uint RoleRoot = 1;
        public const uint RoleJuvenileShoot = 2;
        public const uint RoleTrunk = 3;
        public const uint RoleBranch = 4;
        public const uint RoleStem = 5;
        public const uint RoleLeaf = 6;

        public const int GeneTempOptimum = 0;
        public const int GeneTempTolerance = 1;
        public const int GeneMoistureOptimum = 2;
        public const int GeneMoistureTolerance = 3;
        public const int GeneLightAffinity = 4;
        public const int GeneMetabolic = 5;
        public const int GeneRootBranching = 6;
        public const int GeneMatureHeight = 7;
        public const int GeneBranchCadence = 8;
        public const int GeneLeafLongevity = 9;
        public const int GeneWindResponse = 10;
        public const int GeneMutation = 11;

        public const uint FlagAnchor = 1u;
        public const uint FlagShed = 2u;
        public const uint FlagConvertWood = 4u;
        public const uint FlagConvertDetritus = 8u;
        public const uint FlagConvertAsh = 16u;
        public const uint FlagExposed = 32u;

        public static readonly string[] GeneNames =
        {
            "Temp optimum", "Temp tolerance", "Moisture optimum", "Moisture tolerance",
            "Light affinity", "Metabolic rate", "Root branching", "Mature height",
            "Branch cadence", "Leaf longevity", "Wind response", "Mutation rate"
        };

        public static readonly string[] RoleNames =
        {
            "None", "Root", "Shoot", "Trunk", "Branch", "Stem", "Leaf"
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
        public static bool IsLivingStage(uint stage) => stage == StageSprout || stage == StageSapling || stage == StageTree;
        public static bool IsValidRole(uint role) => role <= RoleLeaf;

        public static uint Role(uint packedTopology) => (packedTopology >> 8) & 255u;
        public static uint TopologyStage(uint packedTopology) => packedTopology & 255u;
        public static uint Flags(uint packedTopology) => (packedTopology >> 16) & 255u;
        public static uint Cause(uint packedTopology) => (packedTopology >> 24) & 255u;

        public static uint PackTopology(uint stage, uint role, uint flags, uint cause) =>
            (stage & 255u) | ((role & 255u) << 8) | ((flags & 255u) << 16) | ((cause & 255u) << 24);

        public static float ExpressFactor(byte gene, float range)
        {
            float r = UnityEngine.Mathf.Clamp01(range);
            return 1f + (gene / 255f - 0.5f) * 2f * r;
        }

        public static float ExpressShift(byte gene, float range) =>
            (gene / 255f - 0.5f) * 2f * range;

        public static int SaplingBranchCount(Packed genome) =>
            SaplingBranchCount(genome, 2, 4);

        public static int SaplingBranchCount(Packed genome, int configuredMin, int configuredMax)
        {
            byte gene = DecodeGene(genome, GeneBranchCadence);
            int lo = UnityEngine.Mathf.Clamp(configuredMin, 1, 8);
            int hi = UnityEngine.Mathf.Clamp(configuredMax, lo, 8);
            return UnityEngine.Mathf.Clamp(lo + gene % 3, lo, hi);
        }

        public static float MatureHeightScale(Packed genome) =>
            0.75f + DecodeGene(genome, GeneMatureHeight) / 255f * 0.35f;

        public static float LeafLifeScale(Packed genome) =>
            0.5f + DecodeGene(genome, GeneLeafLongevity) / 255f;

        public static float WindResponse(Packed genome) =>
            DecodeGene(genome, GeneWindResponse) / 255f;

        public static int RootForkBudget(Packed genome) =>
            1 + DecodeGene(genome, GeneRootBranching) % 3;

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
            return PackMeta(child, StageSprout, generation, Lineage(mother), ToxinDose(mother) / 2u);
        }

        public static string DescribeStage(uint stage) => stage switch
        {
            StageSprout => "Sprout",
            StageSapling => "Sapling",
            StageTree => "Tree",
            StageDead => "Dead",
            _ => "Empty"
        };

        public static string DescribeRole(uint role) =>
            role < RoleNames.Length ? RoleNames[role] : "None";

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
