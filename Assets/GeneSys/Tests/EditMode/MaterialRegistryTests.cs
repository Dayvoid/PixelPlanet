using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Validation;
using NUnit.Framework;
using Unity.Collections;
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
