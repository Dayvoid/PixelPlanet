using System.IO;
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
    public sealed class GrassTests
    {
        [Test]
        public void GrassOnValidateEnforcesSurvivalOutsideGrowth()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.grassGrowthTempMin = 20f;
            config.grassGrowthTempMax = 10f;
            config.grassSurvivalTempMin = 12f;
            config.grassSurvivalTempMax = 15f;
            config.grassGrowthMoistureMin = 0.8f;
            config.grassGrowthMoistureMax = 0.2f;
            config.grassPollenEmitRate = 2f;
            config.grassRootCohesionBonus = 4f;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.grassGrowthTempMin, Is.LessThan(config.grassGrowthTempMax));
            Assert.That(config.grassSurvivalTempMin, Is.LessThanOrEqualTo(config.grassGrowthTempMin));
            Assert.That(config.grassSurvivalTempMax, Is.GreaterThanOrEqualTo(config.grassGrowthTempMax));
            Assert.That(config.grassPollenEmitRate, Is.LessThanOrEqualTo(0.5f));
            Assert.That(config.grassRootCohesionBonus, Is.InRange(0f, 1f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void GeneEncodeDecodeCombineAndMutationStayInRange()
        {
            var mother = new GrassGenome.Packed();
            var partner = new GrassGenome.Packed();
            for (int i = 0; i < GrassGenome.GeneCount; i++)
            {
                mother = GrassGenome.EncodeGene(mother, i, (byte)(i * 18));
                partner = GrassGenome.EncodeGene(partner, i, (byte)(255 - i * 10));
            }
            mother = GrassGenome.PackMeta(mother, GrassGenome.StageAdult, 4, 21, 180);
            partner = GrassGenome.PackMeta(partner, GrassGenome.StageAdult, 2, 9, 40);
            mother = GrassGenome.Sanitize(mother);
            Assert.That(GrassGenome.Stage(mother), Is.EqualTo(GrassGenome.StageAdult));
            Assert.That(GrassGenome.DecodeGene(mother, 2), Is.EqualTo((byte)36));

            var mixed = GrassGenome.Combine(mother, partner, 42u);
            for (int i = 0; i < GrassGenome.GeneCount; i++)
            {
                byte gene = GrassGenome.DecodeGene(mixed, i);
                Assert.That(gene == GrassGenome.DecodeGene(mother, i) || gene == GrassGenome.DecodeGene(partner, i), Is.True);
            }

            var child = GrassGenome.Inherit(mother, partner, true, 0.2f, 1.2f, 7u);
            Assert.That(GrassGenome.Stage(child), Is.EqualTo(GrassGenome.StageJuvenile));
            Assert.That(GrassGenome.Lineage(child), Is.EqualTo(GrassGenome.Lineage(mother)));
            Assert.That(GrassGenome.Generation(child), Is.EqualTo(5u));
            for (int i = 0; i < GrassGenome.GeneCount; i++)
                Assert.That(GrassGenome.DecodeGene(child, i), Is.InRange(0, 255));
            Assert.That(GrassGenome.FloweringDays(mother), Is.InRange(3f, 4f));
            Assert.That(GrassGenome.SeedReleaseDays(mother), Is.InRange(1f, 2f));
        }

        [Test]
        public void RootMaskIsStableAndOccupiesOneToThreeTaps()
        {
            var genome = GrassGenome.EncodeGene(default, GrassGenome.GeneRootArchitecture, 200);
            uint maskA = GrassGenome.SelectRootMask(genome, 4, 9, 1);
            uint maskB = GrassGenome.SelectRootMask(genome, 4, 9, 1);
            Assert.That(maskA, Is.EqualTo(maskB));
            int bits = 0;
            for (int i = 0; i < 3; i++)
                if ((maskA & (1u << i)) != 0) bits++;
            Assert.That(bits, Is.InRange(1, 3));
            uint flags = GrassGenome.PackTimingFlags(maskA, true, true, false);
            Assert.That(GrassGenome.IsFlowering(flags), Is.True);
            Assert.That(GrassGenome.TimingFlags(5f), Is.EqualTo(5u));
            Assert.That(GrassGenome.RootMask(GrassGenome.TimingFlags(5f)), Is.EqualTo(5u));
            Assert.That(GrassGenome.IsPollinated(flags), Is.True);
            Assert.That(GrassGenome.HasReleased(flags), Is.False);
            Assert.That(GrassGenome.DominantRare(MycologyTraits.Basic), Is.EqualTo(0u));
            Assert.That(GrassGenome.DominantRare(MycologyTraits.DroughtResistant | MycologyTraits.HeatProne),
                Is.EqualTo(MycologyTraits.DroughtResistant));
            Assert.That(GrassGenome.DominantRare(MycologyTraits.HeatResistant, MycologyTraits.ElectricResistant),
                Is.EqualTo(MycologyTraits.HeatResistant));
        }

        [Test]
        public void InvalidStageSanitizesAndSliceLayoutMatchesResources()
        {
            var genome = GrassGenome.PackMeta(default, 99u, 0, 0, 0);
            genome = GrassGenome.Sanitize(genome);
            Assert.That(GrassGenome.Stage(genome), Is.EqualTo(GrassGenome.StageEmpty));
            Assert.That(GrassGenome.Slice(2, GrassGenome.DonorOffset), Is.EqualTo(11));
            Assert.That(GrassGenome.GrassSliceCount, Is.EqualTo(12));
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.GrassRead.volumeDepth, Is.EqualTo(12));
            Assert.That(resources.PropaguleRead.volumeDepth, Is.EqualTo(3));
            Assert.That(resources.GrassRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.GrassRootFlux.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.GrassDropClaims.graphicsFormat, Is.EqualTo(GraphicsFormat.R32_UInt));
        }

        [Test]
        public void DetritusMaterialHasPorousOrganicIdentity()
        {
            Assert.That(MaterialIds.Detritus, Is.EqualTo(131u));
            MaterialDefinition detritus = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/131_Detritus.asset");
            Assert.That(detritus, Is.Not.Null);
            Assert.That(detritus.stableId, Is.EqualTo((int)MaterialIds.Detritus));
            Assert.That(detritus.category, Is.EqualTo(MaterialCategory.Granular));
            Assert.That(detritus.porosity, Is.GreaterThan(0.05f));
            Assert.That(detritus.caloricContent, Is.GreaterThan(0f));
            Assert.That(detritus.densityDisplaceable, Is.True);

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry.Validate(out string error), Is.True, error);
            Assert.That(registry.Get((int)MaterialIds.Detritus), Is.Not.Null);
            MaterialGpuData[] gpu = registry.BuildGpuData();
            Assert.That(gpu[(int)MaterialIds.Detritus].biology.z, Is.GreaterThan(0.05f));
        }

        [Test]
        public void ShaderConstantsMatchCSharpLayouts()
        {
            Assert.That(GrassGenome.SlotCount, Is.EqualTo(3));
            Assert.That(GrassGenome.SlicesPerSlot, Is.EqualTo(4));
            Assert.That(GrassGenome.GeneBladeHeight, Is.EqualTo(5));
            Assert.That(GrassGenome.GeneMutation, Is.EqualTo(11));
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Grass.compute");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.FindKernel("RootDemand"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("SoilDebit"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("PhotosynthesisLifecycle"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("PollenTransport"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("SeedTransport"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("Germination"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("PaintGrass"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ClaimFlowerDrop"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ApplyFlowerDrop"), Is.GreaterThanOrEqualTo(0));
            ComputeShader hydrology = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Hydrology.compute");
            Assert.That(hydrology.FindKernel("ApplyFlowerDrop"), Is.GreaterThanOrEqualTo(0));
            Assert.That(hydrology.FindKernel("DetritusExchange"), Is.GreaterThanOrEqualTo(0));
            Assert.That(hydrology.FindKernel("ApplyHydrostaticColumns"), Is.GreaterThanOrEqualTo(0));
            Assert.That(File.ReadAllText("Assets/GeneSys/Compute/Simulation/Hydrology.compute"), Does.Not.Contain("WaterMaterialization"));
            ComputeShader weather = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Weather.compute");
            Assert.That(weather.FindKernel("Precipitation"), Is.GreaterThanOrEqualTo(0));
            ComputeShader hydrostatic = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Hydrostatic.compute");
            Assert.That(hydrostatic, Is.Not.Null);
            Assert.That(hydrostatic.FindKernel("BuildSurfaceWaterColumns"), Is.GreaterThanOrEqualTo(0));
            Assert.That(hydrostatic.FindKernel("ComputeHydrostaticFaceFlux"), Is.GreaterThanOrEqualTo(0));
            string structs = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl");
            Assert.That(structs.Contains("SampleGrassNectar"));
            Assert.That(structs.Contains("DominantRareMycology"));
            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display.Contains("FlowerHue"));
            Assert.That(display.Contains("if (rare == 2u)"));
        }
    }
}
