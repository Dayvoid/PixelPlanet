using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GeneSys.Tests
{
    public sealed class WaspTests
    {
        [Test]
        public void WaspOnValidateEnforcesRanges()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.waspClutchMin = 6;
            config.waspClutchMax = 1;
            config.waspHatchTicksMin = 4000;
            config.waspHatchTicksMax = 12;
            config.waspFullThreshold = 0.2f;
            config.waspHungerThreshold = 0.9f;
            config.waspStarvationThreshold = 0.95f;
            config.waspSurvivalTempMin = 60f;
            config.waspSurvivalTempMax = -20f;
            config.waspDecisionInterval = 0;
            config.waspPollenCapacity = 9;
            config.waspCruiseAltitude = 40f;
            config.waspSurfaceScanRange = 99;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.waspClutchMin, Is.LessThanOrEqualTo(config.waspClutchMax));
            Assert.That(config.waspHatchTicksMin, Is.LessThanOrEqualTo(config.waspHatchTicksMax));
            Assert.That(config.waspHungerThreshold, Is.LessThanOrEqualTo(config.waspFullThreshold));
            Assert.That(config.waspStarvationThreshold, Is.LessThanOrEqualTo(config.waspHungerThreshold));
            Assert.That(config.waspSurvivalTempMin, Is.LessThan(config.waspSurvivalTempMax));
            Assert.That(config.waspDecisionInterval, Is.InRange(1, 16));
            Assert.That(config.waspPollenCapacity, Is.InRange(1, WaspGenome.CargoSlots));
            Assert.That(config.waspCruiseAltitude, Is.InRange(1f, 16f));
            Assert.That(config.waspSurfaceScanRange, Is.InRange(1, 24));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void WaspGeneRoundTripAndInheritanceStayInBounds()
        {
            var mother = new FaunaGenome.Packed();
            var partner = new FaunaGenome.Packed();
            for (int i = 0; i < WaspGenome.GeneCount; i++)
            {
                mother = WaspGenome.EncodeGene(mother, i, (byte)(i * 12));
                partner = WaspGenome.EncodeGene(partner, i, (byte)(255 - i * 12));
            }
            mother = WaspGenome.PackMeta(mother, WaspGenome.StageAdult, WaspGenome.BehaviorNectar, 5, 23);
            partner = WaspGenome.PackMeta(partner, WaspGenome.StageAdult, WaspGenome.BehaviorHunt, 6, 31);
            mother = WaspGenome.Sanitize(mother);
            Assert.That(WaspGenome.Stage(mother), Is.EqualTo(WaspGenome.StageAdult));
            Assert.That(WaspGenome.Behavior(mother), Is.EqualTo(WaspGenome.BehaviorNectar));
            Assert.That(WaspGenome.DecodeGene(mother, WaspGenome.GeneCruiseAltitude), Is.EqualTo((byte)36));

            var child = WaspGenome.Inherit(mother, partner, true, 0.05f, 91u);
            Assert.That(WaspGenome.Stage(child), Is.EqualTo(WaspGenome.StageEgg));
            Assert.That(WaspGenome.Behavior(child), Is.EqualTo(WaspGenome.BehaviorIdle));
            Assert.That(WaspGenome.Generation(child), Is.EqualTo(6u));
            Assert.That(WaspGenome.Lineage(child), Is.EqualTo(23u));
            for (int i = 0; i < WaspGenome.GeneCount; i++)
                Assert.That(WaspGenome.DecodeGene(child, i), Is.InRange(0, 255));

            var clone = WaspGenome.Inherit(mother, default, false, 0f, 5u);
            Assert.That(WaspGenome.DecodeGene(clone, WaspGenome.GeneBodyMass),
                Is.EqualTo(WaspGenome.DecodeGene(mother, WaspGenome.GeneBodyMass)));
        }

        [Test]
        public void WaspBehaviorsSurviveSanitizeAndReadBackAsWaspStates()
        {
            var swooping = WaspGenome.PackMeta(default, WaspGenome.StageAdult, WaspGenome.BehaviorSwoop, 0, 0);
            Assert.That(WaspGenome.Behavior(WaspGenome.Sanitize(swooping)), Is.EqualTo(WaspGenome.BehaviorSwoop));
            Assert.That(WaspGenome.DescribeBehavior(WaspGenome.BehaviorSwoop),
                Is.Not.EqualTo(FaunaGenome.DescribeBehavior(WaspGenome.BehaviorSwoop)),
                "The wasp reuses the fauna packing but reassigns what each behavior index means.");

            var nonsense = WaspGenome.PackMeta(default, 77u, 88u, 0, 0);
            nonsense = WaspGenome.Sanitize(nonsense);
            Assert.That(WaspGenome.Stage(nonsense), Is.EqualTo(WaspGenome.StageEmpty));
            Assert.That(WaspGenome.Behavior(nonsense), Is.EqualTo(WaspGenome.BehaviorIdle));
            Assert.That(WaspGenome.IsValidStage(77u), Is.False);
            Assert.That(WaspGenome.DescribeBehavior(WaspGenome.BehaviorSwoop), Does.Contain("Swoop"));
        }

        [Test]
        public void CruiseAltitudeAndPollenCargoTrackGenes()
        {
            var low = WaspGenome.EncodeGene(default, WaspGenome.GeneCruiseAltitude, 0);
            var high = WaspGenome.EncodeGene(default, WaspGenome.GeneCruiseAltitude, 255);
            float lowAltitude = WaspGenome.CruiseAltitude(low, 5f, 0.45f);
            float highAltitude = WaspGenome.CruiseAltitude(high, 5f, 0.45f);
            Assert.That(lowAltitude, Is.LessThan(highAltitude));
            Assert.That(lowAltitude, Is.GreaterThanOrEqualTo(1f));
            Assert.That(WaspGenome.CruiseAltitude(WaspGenome.EncodeGene(default, WaspGenome.GeneCruiseAltitude, 128), 5f, 0.45f),
                Is.EqualTo(5f).Within(0.15f));

            var empty = default(FaunaGenome.Packed);
            var sample = WaspGenome.PackMeta(default, 1u, 0u, 0u, 44u);
            Assert.That(WaspGenome.CargoValid(empty), Is.False);
            Assert.That(WaspGenome.CargoValid(sample), Is.True);
            Assert.That(WaspGenome.CargoLineage(sample), Is.EqualTo(0u));
            var donor = sample;
            donor.W = 1u | (57u << 16);
            Assert.That(WaspGenome.CargoLineage(donor), Is.EqualTo(57u));
            Assert.That(WaspGenome.CargoCount(donor, empty, sample), Is.EqualTo(2));
        }

        [Test]
        public void WaspResourcesUseExpectedFormats()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.WaspRead.dimension, Is.EqualTo(UnityEngine.Rendering.TextureDimension.Tex2DArray));
            Assert.That(resources.WaspRead.volumeDepth, Is.EqualTo(FaunaGenome.SliceCount));
            Assert.That(resources.WaspWrite.volumeDepth, Is.EqualTo(FaunaGenome.SliceCount));
            Assert.That(resources.WaspRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.WaspClaims.volumeDepth, Is.EqualTo(FaunaGenome.ClaimSliceCount));
            Assert.That(resources.WaspClaims.graphicsFormat, Is.EqualTo(GraphicsFormat.R32_UInt));
            Assert.That(resources.GrassVisit, Is.Null);
        }

        [Test]
        public void WaspMaterialsHaveBiologicalIdentityAndAreRegistered()
        {
            MaterialDefinition wasp = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/132_Wasp.asset");
            MaterialDefinition egg = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/133_WaspEgg.asset");
            Assert.That(wasp, Is.Not.Null);
            Assert.That(egg, Is.Not.Null);
            Assert.That(wasp.stableId, Is.EqualTo((int)MaterialIds.Wasp));
            Assert.That(egg.stableId, Is.EqualTo((int)MaterialIds.WaspEgg));
            Assert.That(wasp.category, Is.EqualTo(MaterialCategory.Biological));
            Assert.That(egg.category, Is.EqualTo(MaterialCategory.Biological));
            Assert.That(wasp.category, Is.EqualTo(MaterialCategory.Biological));
            Assert.That(egg.category, Is.EqualTo(MaterialCategory.Biological));
            Assert.That(wasp.caloricContent, Is.GreaterThan(0f));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry.Validate(out string error), Is.True, error);
            Assert.That(registry.Get((int)MaterialIds.Wasp), Is.Not.Null);
            Assert.That(registry.Get((int)MaterialIds.WaspEgg), Is.Not.Null);
        }

        [Test]
        public void WaspMetricsCountStagesAndPollenCarriers()
        {
            var materials = new uint[] { MaterialIds.Wasp, MaterialIds.Wasp, MaterialIds.WaspEgg, MaterialIds.Cricket };
            var vitals = new[]
            {
                new Vector4(0.8f, 0.6f, 40f, 0f),
                new Vector4(0.4f, 0.5f, 20f, 0f),
                new Vector4(0.2f, 0.9f, 5f, 0f),
                new Vector4(0.9f, 0.9f, 90f, 0f)
            };
            var genomes = new[]
            {
                WaspGenome.PackMeta(default, WaspGenome.StageAdult, WaspGenome.BehaviorCruise, 2, 1),
                WaspGenome.PackMeta(default, WaspGenome.StageJuvenile, WaspGenome.BehaviorIdle, 4, 1),
                WaspGenome.PackMeta(default, WaspGenome.StageEgg, WaspGenome.BehaviorIdle, 3, 1),
                WaspGenome.PackMeta(default, WaspGenome.StageAdult, WaspGenome.BehaviorHunt, 9, 2)
            };
            var carrying = new Vector4(0f, 0f, 0f, AsFloatBits(1u));
            var emptySlot = Vector4.zero;
            var cargo0 = new[] { carrying, emptySlot, emptySlot, carrying };
            var cargo1 = new[] { carrying, emptySlot, emptySlot, emptySlot };
            var cargo2 = new[] { emptySlot, emptySlot, emptySlot, emptySlot };

            WaspMetrics metrics = SimulationMetrics.ComputeWaspMetrics(materials, vitals, genomes, cargo0, cargo1, cargo2);
            Assert.That(metrics.AdultCount, Is.EqualTo(1));
            Assert.That(metrics.JuvenileCount, Is.EqualTo(1));
            Assert.That(metrics.EggCount, Is.EqualTo(1));
            Assert.That(metrics.PollenCarrierCount, Is.EqualTo(1));
            Assert.That(metrics.PollenSampleCount, Is.EqualTo(2));
            Assert.That(metrics.TotalCalories, Is.EqualTo(1.4d).Within(0.001d));
            Assert.That(metrics.MeanGeneration, Is.EqualTo(3f).Within(0.001f));
        }

        private static float AsFloatBits(uint bits) => System.BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }
}
