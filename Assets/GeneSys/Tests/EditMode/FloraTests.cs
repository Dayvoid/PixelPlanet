using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using GeneSys.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GeneSys.Tests
{
    public sealed class FloraTests
    {
        [Test]
        public void FloraDefaultsAreConfiguredAndOrdered()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.floraSeedAtWorldgen, Is.False);
            Assert.That(config.floraInitialSporeLoad, Is.GreaterThan(0f));
            Assert.That(config.floraAirTransportRate, Is.GreaterThan(0f));
            Assert.That(config.floraWaterTransportRate, Is.GreaterThan(0f));
            Assert.That(config.floraGrowthRate, Is.GreaterThan(0f));
            Assert.That(config.floraPhotosynthesisRate, Is.GreaterThan(0f));
            Assert.That(config.floraGrowthTempMin, Is.LessThan(config.floraGrowthTempMax));
            Assert.That(config.floraSurvivalTempMin, Is.LessThanOrEqualTo(config.floraGrowthTempMin));
            Assert.That(config.floraSurvivalTempMax, Is.GreaterThanOrEqualTo(config.floraGrowthTempMax));
            Assert.That(config.floraGrowthMoistureMin, Is.LessThan(config.floraGrowthMoistureMax));
            Assert.That(config.floraSurvivalMoistureMin, Is.LessThanOrEqualTo(config.floraGrowthMoistureMin));
            Assert.That(config.floraSurvivalMoistureMax, Is.GreaterThanOrEqualTo(config.floraGrowthMoistureMax));
            Assert.That(config.floraReproductionThreshold, Is.InRange(0.05f, 1f));
            Assert.That(config.floraGeneExpressionRange, Is.InRange(0f, 1f));
            Assert.That(config.floraPoleDriftRate, Is.GreaterThan(0f));
            Assert.That(config.floraWindShearRate, Is.GreaterThan(0f));
            Assert.That(config.floraRainShearRate, Is.GreaterThan(0f));
            Assert.That(config.floraFragmentYield, Is.InRange(0f, 1f));
            Assert.That(config.floraAnchorGrip, Is.GreaterThan(0f));
            Assert.That(config.transportPassInterval, Is.EqualTo(2));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void FloraOnValidateEnforcesSurvivalOutsideGrowth()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.floraGrowthTempMin = 20f;
            config.floraGrowthTempMax = 10f;
            config.floraSurvivalTempMin = 12f;
            config.floraSurvivalTempMax = 15f;
            config.floraGrowthMoistureMin = 0.8f;
            config.floraGrowthMoistureMax = 0.2f;
            config.floraSurvivalMoistureMin = 0.4f;
            config.floraSurvivalMoistureMax = 0.5f;
            config.floraPoleDriftRate = -1f;
            config.floraFragmentYield = 2f;
            config.transportPassInterval = 0;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.floraGrowthTempMin, Is.LessThan(config.floraGrowthTempMax));
            Assert.That(config.floraSurvivalTempMin, Is.LessThanOrEqualTo(config.floraGrowthTempMin));
            Assert.That(config.floraSurvivalTempMax, Is.GreaterThanOrEqualTo(config.floraGrowthTempMax));
            Assert.That(config.floraSurvivalMoistureMin, Is.LessThanOrEqualTo(config.floraGrowthMoistureMin));
            Assert.That(config.floraSurvivalMoistureMax, Is.GreaterThanOrEqualTo(config.floraGrowthMoistureMax));
            Assert.That(config.floraPoleDriftRate, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.floraFragmentYield, Is.InRange(0f, 1f));
            Assert.That(config.transportPassInterval, Is.InRange(1, 8));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void RestoreDefaultsResetsFloraFields()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.floraGrowthRate = 0f;
            config.floraPhotosynthesisRate = 0f;
            config.floraInitialSporeLoad = 0f;
            config.floraPoleDriftRate = 0f;
            config.floraAnchorGrip = 0f;
            config.transportPassInterval = 8;
            config.RestoreDefaults();
            Assert.That(config.floraGrowthRate, Is.EqualTo(0.16f).Within(0.001f));
            Assert.That(config.floraPhotosynthesisRate, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(config.floraInitialSporeLoad, Is.EqualTo(0.06f).Within(0.001f));
            Assert.That(config.floraPoleDriftRate, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(config.floraAnchorGrip, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.transportPassInterval, Is.EqualTo(2));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void GeneEncodeDecodeRoundTripAndMutationStaysInRange()
        {
            var genome = new FloraGenome.Packed();
            for (int i = 0; i < FloraGenome.GeneCount; i++)
                genome = FloraGenome.EncodeGene(genome, i, (byte)(i * 20));
            genome = FloraGenome.PackMeta(genome, FloraGenome.StageActive, 3, 17, 200);
            genome = FloraGenome.Sanitize(genome);
            Assert.That(FloraGenome.Stage(genome), Is.EqualTo(FloraGenome.StageActive));
            Assert.That(FloraGenome.Generation(genome), Is.EqualTo(3u));
            Assert.That(FloraGenome.Lineage(genome), Is.EqualTo(17u));
            Assert.That(FloraGenome.ToxinDose(genome), Is.EqualTo(200u));
            Assert.That(FloraGenome.DecodeGene(genome, 2), Is.EqualTo((byte)40));

            var mutated = FloraGenome.Mutate(genome, 0.2f, 1.5f, 42u);
            for (int i = 0; i < FloraGenome.GeneCount; i++)
                Assert.That(FloraGenome.DecodeGene(mutated, i), Is.InRange(0, 255));
            Assert.That(FloraGenome.IsValidStage(FloraGenome.Stage(mutated)), Is.True);
            Assert.That(FloraGenome.ExpressFactor(128, 0.45f), Is.EqualTo(1f).Within(0.02f));
        }

        [Test]
        public void PoleDriftGeneIsSilentAtLowAlleles()
        {
            var genome = new FloraGenome.Packed();
            genome = FloraGenome.EncodeGene(genome, FloraGenome.GenePoleDrift, 0);
            Assert.That(FloraGenome.PoleDrift(genome), Is.EqualTo(0f));
            genome = FloraGenome.EncodeGene(genome, FloraGenome.GenePoleDrift, 16);
            Assert.That(FloraGenome.PoleDrift(genome), Is.EqualTo(0f));
            genome = FloraGenome.EncodeGene(genome, FloraGenome.GenePoleDrift, 17);
            Assert.That(FloraGenome.PoleDrift(genome), Is.GreaterThan(0f));
            genome = FloraGenome.EncodeGene(genome, FloraGenome.GenePoleDrift, 255);
            Assert.That(FloraGenome.PoleDrift(genome), Is.EqualTo(1f).Within(0.001f));
            Assert.That(FloraGenome.DescribeGenes(genome, 0.45f), Does.Contain("Pole drift"));
        }

        [Test]
        public void ToxinDoseSaturatesAndInvalidStageSanitizes()
        {
            var genome = FloraGenome.PackMeta(default, FloraGenome.StageActive, 0, 1, 250);
            genome = FloraGenome.SaturateToxin(genome, 20f);
            Assert.That(FloraGenome.ToxinDose(genome), Is.EqualTo(255u));
            genome = FloraGenome.PackMeta(genome, 99u, 0, 0, 0);
            genome = FloraGenome.Sanitize(genome);
            Assert.That(FloraGenome.Stage(genome), Is.EqualTo(FloraGenome.StageSpore));
            Assert.That(FloraGenome.IsValidStage(99u), Is.False);
            Assert.That(FloraGenome.DescribeStage(FloraGenome.StageDormant), Does.Contain("Dormant"));
        }

        [Test]
        public void LifeGenomeAndLightResourcesUseExpectedFormats()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.LifeGenomeRead.dimension, Is.EqualTo(UnityEngine.Rendering.TextureDimension.Tex2DArray));
            Assert.That(resources.LifeGenomeRead.volumeDepth, Is.EqualTo(2));
            Assert.That(resources.LifeGenomeRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.LifeGenomeWrite.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.LightField.graphicsFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(resources.LifeGenomeRead.width, Is.EqualTo(PolarGridDefinition.Validation.angularResolution));
        }

        [Test]
        public void GenomeFloatBitsRoundTripPreservesPackedWords()
        {
            var packed = FloraGenome.PackMeta(
                FloraGenome.Packed.FromUint4(0x01020304u, 0x05060708u, 0x090A0B0Cu, 0u),
                FloraGenome.StageActive, 3, 7, 11);
            var bits = new Vector4(
                System.BitConverter.Int32BitsToSingle(unchecked((int)packed.X)),
                System.BitConverter.Int32BitsToSingle(unchecked((int)packed.Y)),
                System.BitConverter.Int32BitsToSingle(unchecked((int)packed.Z)),
                System.BitConverter.Int32BitsToSingle(unchecked((int)packed.W)));
            FloraGenome.Packed restored = FloraGenome.FromFloatBits(bits);
            Assert.That(restored.X, Is.EqualTo(packed.X));
            Assert.That(restored.Y, Is.EqualTo(packed.Y));
            Assert.That(restored.Z, Is.EqualTo(packed.Z));
            Assert.That(restored.W, Is.EqualTo(packed.W));
            Assert.That(FloraGenome.Stage(restored), Is.EqualTo(FloraGenome.StageActive));
        }

        [Test]
        public void OverlayModeIncludesFloraLightAndGenome()
        {
            var go = new GameObject("Flora Overlay Test");
            var renderer = go.AddComponent<PlanetoidDisplayRenderer>();
            renderer.SetOverlay(20);
            Assert.That(renderer.OverlayMode, Is.EqualTo(FloraVisuals.OverlayMode));
            renderer.SetOverlay(21);
            Assert.That(renderer.OverlayMode, Is.EqualTo(FloraVisuals.LightOverlayMode));
            renderer.SetOverlay(22);
            Assert.That(renderer.OverlayMode, Is.EqualTo(FloraVisuals.GenomeOverlayMode));
            renderer.SetOverlay(99);
            Assert.That(renderer.OverlayMode, Is.EqualTo(25));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void AlgaeMaterialHasBiologicalIdentityAndGpuPacking()
        {
            MaterialDefinition algae = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/128_AlgaeMoss.asset");
            Assert.That(algae, Is.Not.Null);
            Assert.That(algae.stableId, Is.EqualTo((int)MaterialIds.Algae));
            Assert.That(algae.category, Is.EqualTo(MaterialCategory.Biological));
            Assert.That(algae.density, Is.LessThan(1f));
            Assert.That(algae.densityDisplaceable, Is.True);
            Assert.That(algae.rigidity, Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(algae.caloricContent, Is.GreaterThan(0f));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry.Validate(out string error), Is.True, error);
            Assert.That(registry.Get((int)MaterialIds.Algae), Is.Not.Null);
            MaterialGpuData[] gpu = registry.BuildGpuData();
            Assert.That(gpu[(int)MaterialIds.Algae].metadata.x, Is.EqualTo((float)MaterialCategory.Biological).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Algae].motion.x, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void FloraVisualsStageColorsAndSizeTiers()
        {
            Assert.That(FloraVisuals.StageColor(FloraGenome.StageActive).g, Is.GreaterThan(FloraVisuals.StageColor(FloraGenome.StageDesiccated).g));
            Assert.That(FloraVisuals.SizeTier(0.1f), Is.EqualTo(0));
            Assert.That(FloraVisuals.SizeTier(0.5f), Is.EqualTo(1));
            Assert.That(FloraVisuals.SizeTier(0.9f), Is.EqualTo(2));
        }

        [Test]
        public void LifeBrushModeExistsAndMapsAfterIgnite()
        {
            Assert.That(System.Enum.GetNames(typeof(BrushMode)), Does.Contain("Off"));
            Assert.That(System.Enum.GetNames(typeof(BrushMode)), Does.Contain("Life"));
            Assert.That((int)BrushMode.Off, Is.EqualTo(0));
            Assert.That((int)BrushMode.Life, Is.EqualTo((int)BrushMode.Ignite + 1));
        }

        [Test]
        public void OrganismHistoryFormatsTickKindAndLineage()
        {
            var birth = new OrganismHistoryLog.Entry(12480, OrganismHistoryKind.Birth, 0, 42, OrganismHistoryCause.None);
            Assert.That(birth.Format(), Is.EqualTo("Tick 12480  Birth  gen 0 / lineage 42"));
            var death = new OrganismHistoryLog.Entry(12612, OrganismHistoryKind.Death, 1, 42, OrganismHistoryCause.Toxin);
            Assert.That(death.Format(), Is.EqualTo("Tick 12612  Death (toxin)  gen 1 / lineage 42"));
            var painted = new OrganismHistoryLog.Entry(10, OrganismHistoryKind.Birth, 0, 17, OrganismHistoryCause.Painted);
            Assert.That(painted.Format(), Is.EqualTo("Tick 10  Birth (painted)  gen 0 / lineage 17"));
        }

        [Test]
        public void OrganismHistoryCapsOldestEntries()
        {
            var log = new OrganismHistoryLog();
            var batch = new OrganismHistoryLog.GpuEvent[OrganismHistoryLog.Capacity + 8];
            for (int i = 0; i < batch.Length; i++)
            {
                batch[i].Tick = (uint)(i + 1);
                batch[i].Kind = (uint)OrganismHistoryKind.Birth;
                batch[i].Lineage = (uint)i;
            }
            log.AppendFromGpu(batch, batch.Length);
            Assert.That(log.Entries.Count, Is.EqualTo(OrganismHistoryLog.Capacity));
            Assert.That(log.Entries[0].Tick, Is.EqualTo(9));
            Assert.That(log.Entries[log.Entries.Count - 1].Tick, Is.EqualTo(batch.Length));
        }
    }
}
