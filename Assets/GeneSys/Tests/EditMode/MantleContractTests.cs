using System.IO;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation.Geodynamics;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class MantleContractTests
    {
        [Test]
        public void ConfigDefaultsAreBounded()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.mantleLayerEnable, Is.True);
            Assert.That(config.mantleConvectionCells, Is.InRange(2, 8));
            Assert.That(config.mantleDriftRate, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.That(config.mantlePlumeHeat, Is.GreaterThan(0f).And.LessThanOrEqualTo(2f));
            Assert.That(config.mantleLidThinning, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(2f));
            Assert.That(config.geothermalSurfaceGain, Is.GreaterThan(0f).And.LessThanOrEqualTo(2f));
            Assert.That(config.geothermalClimateGain, Is.GreaterThan(0f).And.LessThanOrEqualTo(2f));
            Assert.That(config.mantleConduitMemory, Is.InRange(0f, 1f));
            Assert.That(config.mantleConduitReuse, Is.InRange(0f, 1f));
            Assert.That(config.eruptionTephraFraction, Is.InRange(0f, 1f));
            Assert.That(config.geothermalSurfaceGain, Is.LessThanOrEqualTo(0.25f),
                "Default surface geothermal gain must stay small so soak drift stays inside disabled-control tolerance.");
            Assert.That(config.geothermalClimateGain, Is.LessThanOrEqualTo(0.2f),
                "Default climate geothermal gain must stay small so soak drift stays inside disabled-control tolerance.");
            Assert.That(config.mantlePlumeHeat, Is.LessThanOrEqualTo(0.5f),
                "Default plume heat must stay below the solidus gap so volcanicMagmaFractionLimit remains the safety throttle.");

            config.mantleConvectionCells = 99;
            config.mantleDriftRate = -1f;
            config.mantlePlumeHeat = -2f;
            config.geothermalSurfaceGain = -1f;
            config.geothermalClimateGain = -1f;
            config.mantleConduitMemory = 4f;
            config.mantleConduitReuse = 4f;
            config.eruptionTephraFraction = 4f;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.mantleConvectionCells, Is.InRange(2, 8));
            Assert.That(config.mantleDriftRate, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.mantlePlumeHeat, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.geothermalSurfaceGain, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.geothermalClimateGain, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.mantleConduitMemory, Is.InRange(0f, 1f));
            Assert.That(config.mantleConduitReuse, Is.InRange(0f, 1f));
            Assert.That(config.eruptionTephraFraction, Is.InRange(0f, 1f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void LatticeUsesThreeSlotsIncludingMantle()
        {
            Assert.That(GeodynamicsGrid.StateSlotsPerCell, Is.EqualTo(3));
            Assert.That(GeodynamicsGrid.SlotMantle, Is.EqualTo(2));
            Assert.That(GeodynamicsGrid.StateBufferCount(),
                Is.EqualTo(GeodynamicsGrid.MaxAngularBins * GeodynamicsGrid.MaxRadialBins * 3));
            Assert.That(GeodynamicsGrid.LegacyStateBufferCount(),
                Is.EqualTo(GeodynamicsGrid.MaxAngularBins * GeodynamicsGrid.MaxRadialBins * 2));

            var legacy = new Vector4[GeodynamicsGrid.LegacyStateBufferCount()];
            var dest = new Vector4[GeodynamicsGrid.StateBufferCount()];
            legacy[0] = new Vector4(11f, 0.2f, 0.3f, 0.4f);
            legacy[1] = new Vector4(0.5f, 0.6f, 0.7f, 0.8f);
            GeodynamicsGrid.ExpandLegacyState(legacy, dest);
            Assert.That(dest[0], Is.EqualTo(legacy[0]));
            Assert.That(dest[1], Is.EqualTo(legacy[1]));
            Assert.That(dest[2], Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void TephraMaterialHasExpectedIdentityAndGpuPacking()
        {
            MaterialDefinition tephra = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/016_Tephra.asset");
            Assert.That(tephra, Is.Not.Null, "016_Tephra.asset should exist.");
            Assert.That(tephra.stableId, Is.EqualTo((int)MaterialIds.Tephra));
            Assert.That(tephra.category, Is.EqualTo(MaterialCategory.Granular));
            Assert.That(tephra.solidPhaseId, Is.EqualTo((int)MaterialIds.Tephra));
            Assert.That(tephra.liquidPhaseId, Is.EqualTo((int)MaterialIds.Tephra));
            Assert.That(tephra.gasPhaseId, Is.EqualTo((int)MaterialIds.Tephra));
            Assert.That(tephra.density, Is.EqualTo(1.6f).Within(0.01f));
            Assert.That(tephra.angleOfRepose, Is.EqualTo(35f).Within(0.01f));
            Assert.That(tephra.porosity, Is.GreaterThan(0.4f));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Validate(out string error), Is.True, error);
            Assert.That(registry.Get((int)MaterialIds.Tephra), Is.Not.Null);

            MaterialGpuData[] gpu = registry.BuildGpuData();
            Assert.That(gpu[(int)MaterialIds.Tephra].metadata.w, Is.EqualTo((float)MaterialIds.Tephra).Within(0.01f));
            Assert.That(gpu[(int)MaterialIds.Tephra].metadata.x, Is.EqualTo((float)MaterialCategory.Granular).Within(0.01f));
        }

        [Test]
        public void OverlayThirtyThreeExposesMantle()
        {
            Assert.That(GeodynamicsVisuals.MantleOverlay, Is.EqualTo(33));
            Assert.That(GeodynamicsVisuals.MaxOverlayMode, Is.EqualTo(33));
            string ui = File.ReadAllText("Assets/GeneSys/Runtime/UI/SimulationUIController.cs");
            Assert.That(ui, Does.Contain("\"Mantle\""));
            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display, Does.Contain("_MantleField"));
            Assert.That(display, Does.Contain("_OverlayMode >= 29"));
        }

        [Test]
        public void SnapshotV17KeepsPayloadCountAndExpandsLegacyLattice()
        {
            string snapshot = File.ReadAllText("Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs");
            Assert.That(snapshot, Does.Contain("private const int Version17 = 17;"));
            Assert.That(snapshot, Does.Contain("private const int PayloadCountV17 = PayloadCountV16;"));
            Assert.That(snapshot, Does.Contain("ExpandLegacyState"));
            Assert.That(snapshot, Does.Contain("RebuildMantleSlot"));
            Assert.That(snapshot, Does.Contain("ClearMantleField"));
            Assert.That(snapshot, Does.Contain("Save(host, path, Version17, completed)"));
        }

        [Test]
        public void MantleComputeAndResourcesArePresent()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.MantleFieldRead, Is.Not.Null);
            Assert.That(resources.MantleFieldWrite, Is.Not.Null);

            ComputeShader mantle = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Mantle.compute");
            Assert.That(mantle, Is.Not.Null);
            Assert.That(mantle.FindKernel("MantleConduits"), Is.GreaterThanOrEqualTo(0));

            ComputeShader geodynamics = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Geodynamics.compute");
            Assert.That(geodynamics, Is.Not.Null);
            Assert.That(geodynamics.FindKernel("InitializeMantle"), Is.GreaterThanOrEqualTo(0));

            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler, Does.Contain("DispatchMantleConduits"));
            Assert.That(scheduler, Does.Contain("DispatchMantleInitialize"));
            string structs = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl");
            Assert.That(structs, Does.Contain("MantleField (dedicated RGBA16F"));
        }
    }
}
