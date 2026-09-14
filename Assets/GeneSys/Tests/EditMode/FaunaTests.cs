using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GeneSys.Tests
{
    public sealed class FaunaTests
    {
        [Test]
        public void FaunaOnValidateEnforcesRanges()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.faunaClutchMin = 8;
            config.faunaClutchMax = 1;
            config.faunaHatchTicksMin = 5000;
            config.faunaHatchTicksMax = 10;
            config.faunaHungerThreshold = 0.9f;
            config.faunaFullThreshold = 0.2f;
            config.faunaSurvivalTempMin = 40f;
            config.faunaSurvivalTempMax = -10f;
            config.faunaDecisionInterval = 0;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.faunaClutchMin, Is.LessThanOrEqualTo(config.faunaClutchMax));
            Assert.That(config.faunaHatchTicksMin, Is.LessThanOrEqualTo(config.faunaHatchTicksMax));
            Assert.That(config.faunaHungerThreshold, Is.LessThanOrEqualTo(config.faunaFullThreshold));
            Assert.That(config.faunaSurvivalTempMin, Is.LessThan(config.faunaSurvivalTempMax));
            Assert.That(config.faunaDecisionInterval, Is.InRange(1, 16));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void GeneEncodeDecodeRoundTripAndSexualInheritance()
        {
            var mother = new FaunaGenome.Packed();
            var partner = new FaunaGenome.Packed();
            for (int i = 0; i < FaunaGenome.GeneCount; i++)
            {
                mother = FaunaGenome.EncodeGene(mother, i, (byte)(i * 10));
                partner = FaunaGenome.EncodeGene(partner, i, (byte)(255 - i * 10));
            }
            mother = FaunaGenome.PackMeta(mother, FaunaGenome.StageAdult, FaunaGenome.BehaviorForage, 3, 17);
            partner = FaunaGenome.PackMeta(partner, FaunaGenome.StageAdult, FaunaGenome.BehaviorMateSeek, 4, 21);
            mother = FaunaGenome.Sanitize(mother);
            Assert.That(FaunaGenome.Stage(mother), Is.EqualTo(FaunaGenome.StageAdult));
            Assert.That(FaunaGenome.Behavior(mother), Is.EqualTo(FaunaGenome.BehaviorForage));
            Assert.That(FaunaGenome.DecodeGene(mother, 2), Is.EqualTo((byte)20));

            var child = FaunaGenome.Inherit(mother, partner, true, 0.05f, 42u);
            Assert.That(FaunaGenome.Stage(child), Is.EqualTo(FaunaGenome.StageEgg));
            Assert.That(FaunaGenome.Generation(child), Is.EqualTo(4u));
            Assert.That(FaunaGenome.Lineage(child), Is.EqualTo(17u));
            for (int i = 0; i < FaunaGenome.GeneCount; i++)
                Assert.That(FaunaGenome.DecodeGene(child, i), Is.InRange(0, 255));

            var clone = FaunaGenome.Inherit(mother, default, false, 0f, 7u);
            Assert.That(FaunaGenome.DecodeGene(clone, 1), Is.EqualTo(FaunaGenome.DecodeGene(mother, 1)));
            Assert.That(FaunaGenome.ExpressFactor(128, 0.45f), Is.EqualTo(1f).Within(0.02f));
        }

        [Test]
        public void ClutchSizeScalesWithFertilityGene()
        {
            var low = FaunaGenome.EncodeGene(default, FaunaGenome.GeneFertility, 0);
            var high = FaunaGenome.EncodeGene(default, FaunaGenome.GeneFertility, 255);
            Assert.That(FaunaGenome.ClutchSize(low, 2, 4, 0.45f), Is.EqualTo(2));
            Assert.That(FaunaGenome.ClutchSize(high, 2, 4, 0.45f), Is.EqualTo(4));
        }

        [Test]
        public void InvalidStageSanitizesAndHistoryLabelsFaunaCauses()
        {
            var genome = FaunaGenome.PackMeta(default, 99u, 99u, 0, 0);
            genome = FaunaGenome.Sanitize(genome);
            Assert.That(FaunaGenome.Stage(genome), Is.EqualTo(FaunaGenome.StageEmpty));
            Assert.That(FaunaGenome.IsValidStage(99u), Is.False);
            Assert.That(FaunaGenome.DescribeStage(FaunaGenome.StageJuvenile), Does.Contain("Juvenile"));
            var eaten = new OrganismHistoryLog.Entry(12, OrganismHistoryKind.Death, 1, 3, OrganismHistoryCause.Consumed);
            Assert.That(eaten.Format(), Does.Contain("consumed"));
            var mated = new OrganismHistoryLog.Entry(13, OrganismHistoryKind.Mate, 2, 3, OrganismHistoryCause.Mated);
            Assert.That(mated.Format(), Does.Contain("Mate"));
        }

        [Test]
        public void FaunaResourcesUseExpectedFormats()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.FaunaRead.dimension, Is.EqualTo(UnityEngine.Rendering.TextureDimension.Tex2DArray));
            Assert.That(resources.FaunaRead.volumeDepth, Is.EqualTo(FaunaGenome.SliceCount));
            Assert.That(resources.FaunaRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.AcousticRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32_SFloat));
            Assert.That(resources.AcousticPrev.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32_SFloat));
            Assert.That(resources.FaunaClaims.volumeDepth, Is.EqualTo(FaunaGenome.ClaimSliceCount));
            Assert.That(resources.FaunaClaims.graphicsFormat, Is.EqualTo(GraphicsFormat.R32_UInt));
        }

        [Test]
        public void CricketMaterialsHaveBiologicalIdentityWithoutGenericMotion()
        {
            MaterialDefinition cricket = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/129_Cricket.asset");
            MaterialDefinition egg = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/130_CricketEgg.asset");
            Assert.That(cricket, Is.Not.Null);
            Assert.That(egg, Is.Not.Null);
            Assert.That(cricket.stableId, Is.EqualTo((int)MaterialIds.Cricket));
            Assert.That(egg.stableId, Is.EqualTo((int)MaterialIds.CricketEgg));
            Assert.That(cricket.category, Is.EqualTo(MaterialCategory.Biological));
            Assert.That(cricket.category, Is.EqualTo(MaterialCategory.Biological));
            Assert.That(egg.category, Is.EqualTo(MaterialCategory.Biological));
            Assert.That(cricket.caloricContent, Is.GreaterThan(0f));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry.Validate(out string error), Is.True, error);
            Assert.That(registry.Get((int)MaterialIds.Cricket), Is.Not.Null);
            Assert.That(registry.Get((int)MaterialIds.CricketEgg), Is.Not.Null);
        }
    }
}
