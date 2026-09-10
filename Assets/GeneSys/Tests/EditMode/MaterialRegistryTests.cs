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
            water.gasPhaseId = (int)MaterialIds.Air;
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
            Assert.That(MaterialGpuData.Stride, Is.EqualTo(128));

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
        public void GraniteLimestoneAndClayHaveExpectedIdentityAndThermalContrast()
        {
            MaterialDefinition granite = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/004_Granite.asset");
            MaterialDefinition limestone = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/014_Limestone.asset");
            MaterialDefinition clay = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/015_Clay.asset");
            Assert.That(granite, Is.Not.Null);
            Assert.That(limestone, Is.Not.Null);
            Assert.That(clay, Is.Not.Null);
            Assert.That(granite.stableId, Is.EqualTo((int)MaterialIds.Granite));
            Assert.That(MaterialIds.Granite, Is.EqualTo(MaterialIds.Rock));
            Assert.That(granite.displayName, Is.EqualTo("Granite"));
            Assert.That(limestone.stableId, Is.EqualTo((int)MaterialIds.Limestone));
            Assert.That(clay.stableId, Is.EqualTo((int)MaterialIds.Clay));
            Assert.That(limestone.thermalConductivity, Is.LessThan(granite.thermalConductivity));
            Assert.That(limestone.porosity, Is.GreaterThan(granite.porosity));
            Assert.That(clay.absorbency, Is.GreaterThan(0.8f));
            Assert.That(clay.thermalConductivity, Is.GreaterThan(0.2f));
            Assert.That(granite.latentHeat, Is.GreaterThan(0f));
            Assert.That(limestone.latentHeat, Is.GreaterThan(0f));
            Assert.That(clay.latentHeat, Is.GreaterThan(0f));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry.Get((int)MaterialIds.Granite), Is.Not.Null);
            Assert.That(registry.Get((int)MaterialIds.Limestone), Is.Not.Null);
            Assert.That(registry.Get((int)MaterialIds.Clay), Is.Not.Null);
            MaterialGpuData[] gpu = registry.BuildGpuData();
            Assert.That(gpu[(int)MaterialIds.Granite].motion.y, Is.EqualTo(granite.latentHeat).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Limestone].transport.x, Is.EqualTo(limestone.thermalConductivity).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Clay].transport.w, Is.EqualTo(clay.absorbency).Within(0.01f));
        }

        [Test]
        public void CombustionMaterialPackingIsConfigured()
        {
            MaterialDefinition soil = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/007_Soil.asset");
            MaterialDefinition water = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/009_Water.asset");
            Assert.That(soil, Is.Not.Null);
            Assert.That(soil.ignitionTemperature, Is.EqualTo(180f).Within(0.01f));
            Assert.That(soil.oxygenDemand, Is.GreaterThan(0f));
            Assert.That(water.flashPoint, Is.EqualTo(80f).Within(0.01f));
            Assert.That(water.flashPoint, Is.LessThan(water.boilingTemperature));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            MaterialGpuData[] gpu = registry.BuildGpuData();
            Assert.That(gpu[(int)MaterialIds.Soil].combustion.x, Is.EqualTo(180f).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Water].combustion.y, Is.EqualTo(80f).Within(0.01f));
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
    }
}
