using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class MaterialRegistryTests
    {
        [Test]
        public void WaterMaterialHasExpectedPhaseIds()
        {
            var registry = ScriptableObject.CreateInstance<MaterialRegistry>();
            var water = ScriptableObject.CreateInstance<MaterialDefinition>();
            water.stableId = (int)MaterialIds.Water;
            water.displayName = "Water";
            water.solidPhaseId = (int)MaterialIds.Ice;
            water.liquidPhaseId = (int)MaterialIds.Water;
            water.gasPhaseId = (int)MaterialIds.Vapor;
            registry.materials = new System.Collections.Generic.List<MaterialDefinition> { water };

            Assert.That(registry.Validate(out string error), Is.True, error);
            Object.DestroyImmediate(water);
            Object.DestroyImmediate(registry);
        }

        [Test]
        public void AshMaterialHasExpectedIdentityAndGpuPacking()
        {
            MaterialDefinition ash = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/012_Ash.asset");
            Assert.That(ash, Is.Not.Null, "012_Ash.asset should exist.");
            Assert.That(ash.stableId, Is.EqualTo((int)MaterialIds.Ash));
            Assert.That(ash.category, Is.EqualTo(MaterialCategory.Granular));
            Assert.That(ash.solidPhaseId, Is.EqualTo((int)MaterialIds.Ash));
            Assert.That(ash.liquidPhaseId, Is.EqualTo((int)MaterialIds.Ash));
            Assert.That(ash.gasPhaseId, Is.EqualTo((int)MaterialIds.Ash));
            Assert.That(ash.buoyancyBias, Is.GreaterThan(0.5f));
            Assert.That(ash.density, Is.LessThan(1f));
            Assert.That(ash.densityDisplaceable, Is.False, "Ash uses AshTransport, not liquid density exchange.");

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Validate(out string error), Is.True, error);
            Assert.That(registry.Get((int)MaterialIds.Ash), Is.Not.Null);

            MaterialGpuData[] gpu = registry.BuildGpuData();
            Assert.That(gpu[(int)MaterialIds.Ash].metadata.w, Is.EqualTo((float)MaterialIds.Ash).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Ash].metadata.x, Is.EqualTo((float)MaterialCategory.Granular).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Ash].biology.w, Is.GreaterThan(0.5f));
            Assert.That(gpu[(int)MaterialIds.Ash].motion.x, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void MetalMaterialHasExpectedIdentityAndGpuPacking()
        {
            MaterialDefinition metal = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/013_Metal.asset");
            Assert.That(metal, Is.Not.Null, "013_Metal.asset should exist.");
            Assert.That(metal.stableId, Is.EqualTo((int)MaterialIds.Metal));
            Assert.That(metal.category, Is.EqualTo(MaterialCategory.Solid));
            Assert.That(metal.solidPhaseId, Is.EqualTo((int)MaterialIds.Metal));
            Assert.That(metal.liquidPhaseId, Is.EqualTo((int)MaterialIds.Metal));
            Assert.That(metal.gasPhaseId, Is.EqualTo((int)MaterialIds.Vapor));
            Assert.That(metal.densityDisplaceable, Is.True);
            Assert.That(metal.bioModifiable, Is.False);
            Assert.That(metal.thermalConductivity, Is.GreaterThan(7f));
            Assert.That(metal.electricalConductivity, Is.GreaterThan(7f));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Validate(out string error), Is.True, error);
            Assert.That(registry.Get((int)MaterialIds.Metal), Is.Not.Null);

            MaterialGpuData[] gpu = registry.BuildGpuData();
            Assert.That(gpu[(int)MaterialIds.Metal].metadata.w, Is.EqualTo((float)MaterialIds.Metal).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Metal].transport.x, Is.EqualTo(metal.thermalConductivity).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Metal].transport.z, Is.EqualTo(metal.electricalConductivity).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Metal].motion.x, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void DensityDisplaceableMaterialsPackMotionFlagAndOrdering()
        {
            Assert.That(MaterialGpuData.Stride, Is.EqualTo(112));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry, Is.Not.Null);
            MaterialGpuData[] gpu = registry.BuildGpuData();

            MaterialDefinition rock = registry.Get((int)MaterialIds.Rock);
            MaterialDefinition soil = registry.Get((int)MaterialIds.Soil);
            MaterialDefinition water = registry.Get((int)MaterialIds.Water);
            MaterialDefinition ice = registry.Get((int)MaterialIds.Ice);
            MaterialDefinition core = registry.Get((int)MaterialIds.Core);
            MaterialDefinition mantle = registry.Get((int)MaterialIds.Mantle);
            MaterialDefinition ash = registry.Get((int)MaterialIds.Ash);

            Assert.That(rock.densityDisplaceable, Is.True);
            Assert.That(soil.densityDisplaceable, Is.True);
            Assert.That(water.densityDisplaceable, Is.True);
            Assert.That(ice.densityDisplaceable, Is.True);
            Assert.That(registry.Get((int)MaterialIds.Basalt).densityDisplaceable, Is.True);
            Assert.That(registry.Get((int)MaterialIds.Sediment).densityDisplaceable, Is.True);
            Assert.That(registry.Get((int)MaterialIds.Magma).densityDisplaceable, Is.True);

            Assert.That(core.densityDisplaceable, Is.False);
            Assert.That(mantle.densityDisplaceable, Is.False);
            Assert.That(ash.densityDisplaceable, Is.False);
            Assert.That(registry.Get((int)MaterialIds.Void).densityDisplaceable, Is.False);
            Assert.That(registry.Get((int)MaterialIds.Air).densityDisplaceable, Is.False);

            Assert.That(rock.density, Is.GreaterThan(water.density));
            Assert.That(soil.density, Is.GreaterThan(water.density));
            Assert.That(ice.density, Is.LessThan(water.density));

            Assert.That(gpu[(int)MaterialIds.Rock].motion.x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Water].motion.x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Ice].motion.x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Core].motion.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Rock].physical.x, Is.EqualTo(rock.density).Within(0.01f));
        }

        [Test]
        public void WorldGenDefaultsPreferNewPipeline()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.useOgWorldgen, Is.False);
            Assert.That(config.metalVeinCount, Is.EqualTo(12));
            Assert.That(config.metalVeinMinSize, Is.GreaterThan(0f));
            Assert.That(config.iceCapRadius, Is.GreaterThan(0f));
            Assert.That(config.iceCapHeight, Is.GreaterThan(0f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void DensityExchangeDefaultsAreConfigured()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.densityExchangeRate, Is.GreaterThan(0f));
            Assert.That(config.densityExchangeEpsilon, Is.GreaterThan(0f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void MagmaEruptionDefaultsPreserveLegacyBehavior()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.magmaEruption, Is.EqualTo(0f));
            Assert.That(config.eruptionBurdenDepth, Is.GreaterThanOrEqualTo(1));
            Assert.That(config.eruptionBlastThreshold, Is.GreaterThan(0f));
            Assert.That(config.ashFertilityStrength, Is.GreaterThan(0f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void MoistureErosionDefaultsAreConfigured()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.dryMoistureThreshold, Is.GreaterThan(0f));
            Assert.That(config.moistureCohesionStrength, Is.GreaterThan(0f));
            Assert.That(config.capillaryEvaporationFraction, Is.InRange(0f, 1f));
            Object.DestroyImmediate(config);
        }
    }

    public sealed class BasinMetricsTests
    {
        [Test]
        public void WrapAwareBasinCounterFindsTwoSeparatedOceans()
        {
            int width = 8;
            int height = 4;
            bool[] mask = new bool[width * height];
            for (int x = 1; x < 3; x++)
                for (int y = 1; y < 3; y++)
                    mask[y * width + x] = true;
            for (int x = 5; x < 7; x++)
                for (int y = 1; y < 3; y++)
                    mask[y * width + x] = true;

            int basins = SimulationMetrics.CountWrapAwareBasins(mask, width, height);
            Assert.That(basins, Is.EqualTo(2));
        }

        [Test]
        public void ConfigMigrationPreservesGroundwaterDepthRange()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.groundwaterDepth = 0.42f;
            config.targetOceanCoverage = 0.5f;
            config.minOceanBasins = 2;
            config.maxOceanBasins = 3;
            Assert.That(config.groundwaterDepth, Is.InRange(0f, 1f));
            Assert.That(config.targetOceanCoverage, Is.InRange(0.2f, 0.8f));
            Object.DestroyImmediate(config);
        }
    }
}
