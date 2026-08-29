using System;
using System.Text;
using UnityEngine;

namespace GeneSys.Simulation
{
    /// <summary>
    /// 12-gene, 8-bit-per-gene packing shared with HLSL. Layout matches the fauna packing
    /// so inheritance helpers behave identically, but the gene meanings are wasp specific:
    /// moisture load is folded into body mass to free an index for cruise altitude.
    /// </summary>
    public static class WaspGenome
    {
        public const int GeneCount = 12;
        public const uint WaspId = 132;
        public const uint EggId = 133;
        public const int CargoSlots = 3;

        public const int VitalsSlice = 0;
        public const int MotionSlice = 1;
        public const int GenomeSlice = 2;
        public const int PartnerSlice = 3;
        public const int CargoSlice = 4;
        public const int SliceCount = 7;

        public const uint StageEmpty = 0;
        public const uint StageEgg = 1;
        public const uint StageJuvenile = 2;
        public const uint StageAdult = 3;
        public const uint StageDead = 4;

        public const uint BehaviorIdle = 0;
        public const uint BehaviorCruise = 1;
        public const uint BehaviorHunt = 2;
        public const uint BehaviorNectar = 3;
        public const uint BehaviorSwoop = 4;
        public const uint BehaviorFlee = 5;

        public const int GeneFlightPower = 0;
        public const int GeneBodyMass = 1;
        public const int GeneDrag = 2;
        public const int GeneCruiseAltitude = 3;
        public const int GeneHydrationRetention = 4;
        public const int GeneMetabolism = 5;
        public const int GeneCalorieCapacity = 6;
        public const int GenePreySense = 7;
        public const int GeneHearing = 8;
        public const int GeneAggression = 9;
        public const int GeneFertility = 10;
        public const int GeneMutation = 11;

        public static readonly string[] GeneNames =
        {
            "Flight power", "Body mass", "Drag", "Cruise altitude",
            "Hydration retention", "Metabolism", "Calorie capacity", "Prey sense",
            "Hearing", "Aggression", "Fertility", "Mutation rate"
        };

        public static byte DecodeGene(FaunaGenome.Packed genome, int index) => FaunaGenome.DecodeGene(genome, index);

        public static FaunaGenome.Packed EncodeGene(FaunaGenome.Packed genome, int index, byte value) =>
            FaunaGenome.EncodeGene(genome, index, value);

        public static uint Stage(FaunaGenome.Packed genome) => genome.W & 255u;
        public static uint Behavior(FaunaGenome.Packed genome) => (genome.W >> 8) & 255u;
        public static uint Generation(FaunaGenome.Packed genome) => (genome.W >> 16) & 255u;
        public static uint Lineage(FaunaGenome.Packed genome) => (genome.W >> 24) & 255u;

        public static FaunaGenome.Packed PackMeta(FaunaGenome.Packed genome, uint stage, uint behavior, uint generation, uint lineage)
        {
            genome.W = (stage & 255u) | ((behavior & 255u) << 8) | ((generation & 255u) << 16) | ((lineage & 255u) << 24);
            return genome;
        }

        public static FaunaGenome.Packed FromFloatBits(Vector4 bits) => FaunaGenome.Packed.FromFloatBits(bits);

        public static FaunaGenome.Packed Sanitize(FaunaGenome.Packed genome)
        {
            uint stage = Stage(genome);
            if (stage > StageDead) stage = StageEmpty;
            uint behavior = Behavior(genome);
            if (behavior > BehaviorFlee) behavior = BehaviorIdle;
            return PackMeta(genome, stage, behavior, Generation(genome), Lineage(genome));
        }

        public static bool IsValidStage(uint stage) => stage <= StageDead;

        public static bool IsLivingStage(uint stage) =>
            stage == StageEgg || stage == StageJuvenile || stage == StageAdult;

        public static float ExpressFactor(byte gene, float range) => FaunaGenome.ExpressFactor(gene, range);

        public static int ClutchSize(FaunaGenome.Packed genome, int minClutch, int maxClutch, float expressionRange) =>
            FaunaGenome.ClutchSize(genome, minClutch, maxClutch, expressionRange);

        public static FaunaGenome.Packed Mutate(FaunaGenome.Packed genome, float baseMutationRate, uint salt)
        {
            genome = Sanitize(genome);
            return FaunaGenome.Mutate(genome, baseMutationRate, salt);
        }

        public static FaunaGenome.Packed Inherit(FaunaGenome.Packed mother, FaunaGenome.Packed partner, bool hasPartner, float baseMutationRate, uint salt)
        {
            FaunaGenome.Packed child = hasPartner ? FaunaGenome.Combine(mother, partner, salt) : mother;
            child = FaunaGenome.Mutate(child, baseMutationRate, salt + 17u);
            return PackMeta(child, StageEgg, BehaviorIdle, (Generation(mother) + 1u) & 255u, Lineage(mother));
        }

        /// <summary>
        /// Preferred clearance above the surface in cells, before clamping to the scan range.
        /// </summary>
        public static float CruiseAltitude(FaunaGenome.Packed genome, float baseAltitude, float expressionRange) =>
            Mathf.Max(1f, baseAltitude * ExpressFactor(DecodeGene(genome, GeneCruiseAltitude), expressionRange));

        /// <summary>
        /// Pollen cargo reuses the grass donor convention: low bit of W marks a live sample.
        /// </summary>
        public static bool CargoValid(FaunaGenome.Packed cargo) => (cargo.W & 1u) != 0u;

        public static int CargoCount(FaunaGenome.Packed cargo0, FaunaGenome.Packed cargo1, FaunaGenome.Packed cargo2)
        {
            int count = 0;
            if (CargoValid(cargo0)) count++;
            if (CargoValid(cargo1)) count++;
            if (CargoValid(cargo2)) count++;
            return count;
        }

        public static uint CargoLineage(FaunaGenome.Packed cargo) => (cargo.W >> 16) & 255u;

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
            BehaviorCruise => "Cruise",
            BehaviorHunt => "Hunt",
            BehaviorNectar => "Nectar",
            BehaviorSwoop => "Swoop",
            BehaviorFlee => "Flee",
            _ => "Idle"
        };

        public static string DescribeGenes(FaunaGenome.Packed genome, float expressionRange)
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
    }
}
