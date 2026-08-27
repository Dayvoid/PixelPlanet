using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GeneSys.Tests
{
    public sealed class GrassTests
    {
        [Test]
        public void GrassDefaultsAreConfiguredAndOrdered()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.grassSeedAtWorldgen, Is.False);
            Assert.That(config.grassGrowthRate, Is.GreaterThan(0f));
            Assert.That(config.grassPhotosynthesisRate, Is.GreaterThan(0f));
            Assert.That(config.grassRootCohesionScale, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(config.grassPollenAirRate, Is.GreaterThan(0f));
            Assert.That(config.grassPollenAirRate, Is.LessThan(config.floraAirTransportRate));
            Assert.That(config.grassGrowthTempMin, Is.LessThan(config.grassGrowthTempMax));
            Assert.That(config.grassSurvivalTempMin, Is.LessThanOrEqualTo(config.grassGrowthTempMin));
            Assert.That(config.detritusEvaporationScale, Is.InRange(0f, 0.5f));
            Assert.That(config.detritusInitialNutrient, Is.GreaterThan(0f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void GrassOnValidateEnforcesSurvivalOutsideGrowth()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.grassGrowthTempMin = 20f;
            config.grassGrowthTempMax = 10f;
            config.grassSurvivalTempMin = 12f;
            config.grassSurvivalTempMax = 15f;
            config.grassRootCohesionScale = 2f;
            config.detritusEvaporationScale = 2f;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.grassGrowthTempMin, Is.LessThan(config.grassGrowthTempMax));
            Assert.That(config.grassSurvivalTempMin, Is.LessThanOrEqualTo(config.grassGrowthTempMin));
            Assert.That(config.grassRootCohesionScale, Is.InRange(0f, 1f));
            Assert.That(config.detritusEvaporationScale, Is.InRange(0f, 1f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void RestoreDefaultsResetsGrassFields()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.grassGrowthRate = 0f;
            config.grassRootCohesionScale = 0f;
            config.detritusDecayRate = 0f;
            config.RestoreDefaults();
            Assert.That(config.grassGrowthRate, Is.EqualTo(0.14f).Within(0.001f));
            Assert.That(config.grassRootCohesionScale, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(config.detritusDecayRate, Is.EqualTo(0.04f).Within(0.001f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void GeneEncodeDecodeRoundTripAndMutationStaysInRange()
        {
            var genome = new GrassGenome.Packed();
            for (int i = 0; i < GrassGenome.GeneCount; i++)
                genome = GrassGenome.EncodeGene(genome, i, (byte)(i * 18));
            genome = GrassGenome.PackMeta(genome, GrassGenome.StageFlowering, 2, 9, 40);
            genome = GrassGenome.Sanitize(genome);
            Assert.That(GrassGenome.Stage(genome), Is.EqualTo(GrassGenome.StageFlowering));
            Assert.That(GrassGenome.DecodeGene(genome, GrassGenome.GeneBladeHeight), Is.EqualTo((byte)126));
            var mutated = GrassGenome.Mutate(genome, 0.2f, 1.2f, 11u);
            for (int i = 0; i < GrassGenome.GeneCount; i++)
                Assert.That(GrassGenome.DecodeGene(mutated, i), Is.InRange(0, 255));
            Assert.That(GrassGenome.IsValidStage(GrassGenome.Stage(mutated)), Is.True);
        }

        [Test]
        public void RootCountAndMaskStayWithinThreeTaps()
        {
            var genome = new GrassGenome.Packed();
            genome = GrassGenome.EncodeGene(genome, GrassGenome.GeneRootAffinity, 0);
            Assert.That(GrassGenome.RootCount(genome), Is.EqualTo(1));
            genome = GrassGenome.EncodeGene(genome, GrassGenome.GeneRootAffinity, 255);
            Assert.That(GrassGenome.RootCount(genome), Is.EqualTo(3));
            uint mask = GrassGenome.RootMask(genome, 4, 9, 1, 12345);
            int bits = 0;
            for (int i = 0; i < 3; i++)
                if ((mask & (1u << i)) != 0) bits++;
            Assert.That(bits, Is.EqualTo(3));
            Assert.That(mask, Is.LessThanOrEqualTo(7u));
        }

        [Test]
        public void CombineMixesMaternalAndDonorGenes()
        {
            var mother = new GrassGenome.Packed();
            var donor = new GrassGenome.Packed();
            for (int i = 0; i < GrassGenome.GeneCount; i++)
            {
                mother = GrassGenome.EncodeGene(mother, i, 0);
                donor = GrassGenome.EncodeGene(donor, i, 255);
            }
            var child = GrassGenome.Combine(mother, donor, 99u);
            bool sawMother = false;
            bool sawDonor = false;
            for (int i = 0; i < GrassGenome.GeneCount; i++)
            {
                byte gene = GrassGenome.DecodeGene(child, i);
                if (gene == 0) sawMother = true;
                if (gene == 255) sawDonor = true;
            }
            Assert.That(sawMother, Is.True);
            Assert.That(sawDonor, Is.True);
        }

        [Test]
        public void InvalidStageSanitizesAndDescribeContainsFlowering()
        {
            var genome = GrassGenome.PackMeta(default, 99u, 0, 0, 0);
            genome = GrassGenome.Sanitize(genome);
            Assert.That(GrassGenome.Stage(genome), Is.EqualTo(GrassGenome.StageEmpty));
            Assert.That(GrassGenome.DescribeStage(GrassGenome.StageFlowering), Does.Contain("Flowering"));
            Assert.That(GrassGenome.IsLiving(GrassGenome.StageAdult), Is.True);
            Assert.That(GrassGenome.IsOccupied(GrassGenome.StageEmpty), Is.False);
        }

        [Test]
        public void GrassResourcesUseExpectedFormats()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.GrassRead.volumeDepth, Is.EqualTo(GrassGenome.SliceCount));
            Assert.That(resources.PropaguleRead.volumeDepth, Is.EqualTo(GrassGenome.PropaguleSliceCount));
            Assert.That(resources.GrassRootDemand.volumeDepth, Is.EqualTo(3));
            Assert.That(resources.GrassRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.GrassClaims.graphicsFormat, Is.EqualTo(GraphicsFormat.R32_UInt));
        }

        [Test]
        public void DetritusMaterialHasOrganicIdentityAndGpuPacking()
        {
            MaterialDefinition detritus = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/131_Detritus.asset");
            Assert.That(detritus, Is.Not.Null);
            Assert.That(detritus.stableId, Is.EqualTo((int)MaterialIds.Detritus));
            Assert.That(detritus.category, Is.EqualTo(MaterialCategory.Granular));
            Assert.That(detritus.porosity, Is.GreaterThan(0.7f));
            Assert.That(detritus.absorbency, Is.GreaterThan(0.7f));
            Assert.That(detritus.caloricContent, Is.GreaterThan(0f));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry.Validate(out string error), Is.True, error);
            Assert.That(registry.Get((int)MaterialIds.Detritus), Is.Not.Null);
            MaterialGpuData[] gpu = registry.BuildGpuData();
            Assert.That(gpu[(int)MaterialIds.Detritus].biology.z, Is.GreaterThan(0.7f));
        }

        [Test]
        public void GrassVisualsBladeAndFlowerColorsRespondToGenesAndTraits()
        {
            Assert.That(GrassVisuals.BladeColor(255).g, Is.GreaterThan(GrassVisuals.BladeColor(0).g));
            Assert.That(GrassVisuals.FlowerColor(32).r, Is.GreaterThan(GrassVisuals.Flower.r * 0.5f));
            Assert.That(GrassVisuals.Root.r, Is.GreaterThan(GrassVisuals.Root.g));
        }

        [Test]
        public void ShaderConstantsMatchCpuLayout()
        {
            Assert.That(GrassGenome.SlotCount, Is.EqualTo(3));
            Assert.That(GrassGenome.SlicesPerSlot, Is.EqualTo(4));
            Assert.That(GrassGenome.DetritusId, Is.EqualTo(MaterialIds.Detritus));
            Assert.That(GrassGenome.GeneRootAffinity, Is.EqualTo(10));
        }

        [Test]
        public void GrassComputeExposesRootShareAndLifecycleKernels()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Grass.compute");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.FindKernel("ResolveRootShare"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ApplyRootDebit"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("GrassLifecycle"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("GrassGermination"), Is.GreaterThanOrEqualTo(0));
        }
    }
}
