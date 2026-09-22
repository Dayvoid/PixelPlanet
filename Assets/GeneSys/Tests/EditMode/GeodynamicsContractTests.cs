using System.IO;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Persistence;
using GeneSys.Rendering;
using GeneSys.Simulation.Geodynamics;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class GeodynamicsContractTests
    {
        [Test]
        public void GridBinsCoverEveryThetaAndWrap()
        {
            const int width = 64;
            const int bins = 16;
            var used = new int[bins];
            for (int theta = 0; theta < width; theta++)
            {
                int bin = GeodynamicsGrid.AngularBinOf(theta, width, bins);
                Assert.That(bin, Is.InRange(0, bins - 1));
                used[bin]++;
            }

            for (int bin = 0; bin < bins; bin++)
                Assert.That(used[bin], Is.GreaterThan(0));

            Assert.That(GeodynamicsGrid.AngularBinOf(-1, width, bins),
                Is.EqualTo(GeodynamicsGrid.AngularBinOf(width - 1, width, bins)));
        }

        [Test]
        public void ConfigDefaultsAreBoundedAndEnabled()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.geodynamicsLayerEnable, Is.True);
            Assert.That(config.geodynamicsAngularBins, Is.EqualTo(64));
            Assert.That(config.geodynamicsRadialBins, Is.EqualTo(16));
            Assert.That(config.tectonicReleaseFraction, Is.LessThanOrEqualTo(0.25f));
            Assert.That(config.volcanicReleaseFraction, Is.LessThanOrEqualTo(0.25f));
            Assert.That(config.volcanicMeltRate, Is.GreaterThan(0f).And.LessThanOrEqualTo(4f));
            Assert.That(config.volcanicMagmaFractionLimit, Is.InRange(0.05f, 1f));
            Assert.That(config.tectonicEventFootprint, Is.LessThanOrEqualTo(0.1f));
            Assert.That(config.tectonicMaxConcurrentEvents, Is.EqualTo(2));
            Assert.That(config.tectonicKinematicCoupling, Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(config.tectonicUpliftScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(config.tectonicDisplacementScale, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(config.tectonicIsostasyScale, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(config.mantleLayerEnable, Is.True);
            Assert.That(config.mantleConvectionCells, Is.InRange(2, 8));
            Assert.That(config.mantlePlumeHeat, Is.GreaterThan(0f).And.LessThanOrEqualTo(2f));
            Assert.That(config.geothermalSurfaceGain, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.eruptionTephraFraction, Is.InRange(0f, 1f));
            Assert.That(config.eruptionDriveScale, Is.InRange(0.2f, 0.8f));
            config.tectonicReleaseFraction = 0.9f;
            config.geodynamicsAngularBins = 4;
            config.tectonicKinematicCoupling = 2f;
            config.volcanicMagmaFractionLimit = 4f;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.tectonicReleaseFraction, Is.LessThanOrEqualTo(0.25f));
            Assert.That(config.geodynamicsAngularBins, Is.InRange(16, 128));
            Assert.That(config.tectonicKinematicCoupling, Is.InRange(0f, 1f));
            Assert.That(config.volcanicMagmaFractionLimit, Is.InRange(0.05f, 1f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void LegacyPropertyAliasesWriteCanonicalFields()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.mantlePressure = 1.25f;
            config.fractureRate = 0.4f;
            config.magmaEruption = 0.7f;
            config.stressDecayRate = 0.11f;
            config.faultCount = 9;
            Assert.That(config.geodynamicsPressureBuildRate, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(config.tectonicStrainGain, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(config.eruptionDriveScale, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(config.surfaceStressRecoveryRate, Is.EqualTo(0.11f).Within(0.0001f));
            Assert.That(config.tectonicFaultSeedCount, Is.EqualTo(9));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void LegacyJsonAliasesRestoreRenamedFields()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            const string json = "{\"mantlePressure\":1.5,\"fractureRate\":0.33,\"magmaEruption\":0.2,\"faultCount\":7}";
            WorldSnapshotService.ApplyLegacyGeologyJsonAliases(json, config);
            Assert.That(config.geodynamicsPressureBuildRate, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(config.tectonicStrainGain, Is.EqualTo(0.33f).Within(0.0001f));
            Assert.That(config.eruptionDriveScale, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(config.tectonicFaultSeedCount, Is.EqualTo(7));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void ResourcesAndKernelsMatchLatticeContract()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.GeodynamicsStateRead, Is.Not.Null);
            Assert.That(resources.GeodynamicsStateWrite, Is.Not.Null);
            Assert.That(resources.GeodynamicsEvents, Is.Not.Null);
            Assert.That(resources.GeodynamicsColumns, Is.Not.Null);
            Assert.That(resources.GeodynamicsEventCounter, Is.Not.Null);
            Assert.That(resources.GeodynamicsStateRead.count, Is.EqualTo(GeodynamicsGrid.StateBufferCount()));
            Assert.That(resources.GeodynamicsEvents.count, Is.EqualTo(GeodynamicsGrid.EventBufferCount()));
            Assert.That(resources.GeodynamicsEventCounter.count, Is.EqualTo(GeodynamicsGrid.StatsBufferCount()));
            Assert.That(GeodynamicsGrid.StateSlotsPerCell, Is.EqualTo(3));
            Assert.That(resources.MantleFieldRead, Is.Not.Null);
            Assert.That(resources.MantleFieldWrite, Is.Not.Null);

            string swap = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs");
            Assert.That(swap, Does.Contain("SwapGeodynamics"));
            Assert.That(swap, Does.Contain("Geodynamics lattice buffers are also excluded"));

            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Geodynamics.compute");
            Assert.That(shader, Is.Not.Null);
            foreach (string kernel in new[] { "InitializeFaults", "InitializeMantle", "AggregateInterior", "StepGeodynamics", "SelectEvents", "CommitEvent" })
                Assert.That(shader.FindKernel(kernel), Is.GreaterThanOrEqualTo(0), kernel);

            ComputeShader geology = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Geology.compute");
            Assert.That(geology, Is.Not.Null);
            foreach (string kernel in new[] { "TectonicDisplacement", "TectonicVertical" })
                Assert.That(geology.FindKernel(kernel), Is.GreaterThanOrEqualTo(0), kernel);
        }

        [Test]
        public void SchedulerAndShadersWireExclusiveTransport()
        {
            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler, Does.Contain("DispatchGeodynamics"));
            Assert.That(scheduler, Does.Contain("DispatchTectonicKinematics"));
            Assert.That(scheduler, Does.Contain("FindKernel(\"TectonicVertical\")"));
            Assert.That(scheduler, Does.Not.Contain("maceMagma"));
            Assert.That(scheduler, Does.Not.Contain("maceAsh"));
            Assert.That(scheduler, Does.Contain("CoreHeatSource"));
            Assert.That(scheduler, Does.Contain("HydrothermalRelease"));
            Assert.That(scheduler, Does.Not.Contain("FindKernel(\"CoreReaction\")"));
            Assert.That(scheduler, Does.Not.Contain("GeothermalDischarge"));

            string geology = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Geology.compute");
            Assert.That(geology, Does.Contain("Geodynamics.hlsl"));
            Assert.That(geology, Does.Contain("CoreHeatSource"));
            Assert.That(geology, Does.Contain("GeodynamicsDikeNucleation"));
            Assert.That(geology, Does.Contain("TectonicDisplacement"));
            Assert.That(geology, Does.Contain("TectonicVertical"));
            Assert.That(geology, Does.Not.Contain("weakness > 0.4 && (overpressure > 0.28"));
            Assert.That(geology, Does.Not.Contain("if (_MaceExtra.y > 0.5)"));

            string geoHlsl = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/Geodynamics.hlsl");
            Assert.That(geoHlsl, Does.Contain("GeodynamicsDikeNucleation"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsSampleReservoir"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsAngularCoord"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsVolcanoScore"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsVolcanoScoreAt"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsRisingColumn"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsNearestVolcanic"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsPlume"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsLidBase"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsGeothermalFlux"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsSampleMantle"));
            Assert.That(geoHlsl, Does.Not.Contain("step(0.97, pocket)"));
            Assert.That(geology, Does.Contain("injection"));
            Assert.That(geology, Does.Contain("effectiveSolidus"));
            Assert.That(geology, Does.Contain("IsEruptionPermeable"));
            Assert.That(geology, Does.Not.Contain("eventMelt"));
            Assert.That(geology, Does.Not.Contain("state.x = max(state.x, 950"));
            Assert.That(geology, Does.Not.Contain("forcedUp"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsVerticalDrive"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsAngularDrive"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsKinematicTriggered"));
            Assert.That(geoHlsl, Does.Contain("GeodynamicsKinematicSpacing"));
            Assert.That(geology, Does.Not.Contain("forcedDown"));
            Assert.That(geoHlsl, Does.Not.Contain("GeodynamicsFaultWeakness(theta, radius01) * 0.55"));

            string geodynamics = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Geodynamics.compute");
            Assert.That(geodynamics, Does.Contain("maxConcurrent"));
            Assert.That(geodynamics, Does.Contain("GeoNearOccupied"));
            Assert.That(geodynamics, Does.Contain("headroom"));
            Assert.That(geodynamics, Does.Contain("float peak"));
            Assert.That(geodynamics, Does.Contain("0.04 + saturate(prevKin.z)"));
            Assert.That(geology, Does.Contain("coolScale"));
            Assert.That(scheduler, Does.Contain("config.volcanicMeltRate"));
            Assert.That(scheduler, Does.Contain("DispatchMantleConduits"));
            Assert.That(scheduler, Does.Contain("config.mantlePlumeHeat"));

            string hydrology = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Hydrology.compute");
            Assert.That(hydrology, Does.Contain("HydrothermalRelease"));
            Assert.That(hydrology, Does.Contain("GeodynamicsSeismicEnvelope"));

            string phase = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MaterialSimulation.compute");
            Assert.That(phase, Does.Contain("if (material == 3u || (geologyToMagma"));
            Assert.That(phase, Does.Not.Contain("GroundwaterBoilMass"));
        }

        [Test]
        public void SnapshotV15ReservesGeodynamicsPayloads()
        {
            string snapshot = File.ReadAllText("Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs");
            Assert.That(snapshot, Does.Contain("private const int Version15 = 15;"));
            Assert.That(snapshot, Does.Contain("private const int Version16 = 16;"));
            Assert.That(snapshot, Does.Contain("private const int Version17 = 17;"));
            Assert.That(snapshot, Does.Contain("private const int PayloadCountV15 = PayloadCountV14 + 2;"));
            Assert.That(snapshot, Does.Contain("private const int PayloadCountV16 = PayloadCountV12 + 2;"));
            Assert.That(snapshot, Does.Contain("private const int PayloadCountV17 = PayloadCountV16;"));
            Assert.That(snapshot, Does.Contain("ExpandLegacyState"));
            Assert.That(snapshot, Does.Contain("RequestGeodynamicsBatch"));
            Assert.That(snapshot, Does.Contain("ApplyLegacyGeologyJsonAliases"));
            Assert.That(snapshot, Does.Contain("RebuildGeodynamics(true)"));
        }

        [Test]
        public void DisplayExposesGeodynamicsOverlays()
        {
            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display, Does.Contain("_GeodynamicsState"));
            Assert.That(display, Does.Contain("_OverlayMode >= 29"));
            Assert.That(GeodynamicsVisuals.HeatFlowOverlay, Is.EqualTo(29));
            Assert.That(GeodynamicsVisuals.ReleaseOverlay, Is.EqualTo(32));
            Assert.That(GeodynamicsVisuals.MantleOverlay, Is.EqualTo(33));
            Assert.That(GeodynamicsVisuals.MaxOverlayMode, Is.EqualTo(33));

            string renderer = File.ReadAllText("Assets/GeneSys/Runtime/Rendering/PlanetoidDisplayRenderer.cs");
            Assert.That(renderer, Does.Contain("GeodynamicsVisuals.MaxOverlayMode"));
            Assert.That(renderer, Does.Not.Contain("Clamp(mode, 0, ClimateVisuals.OverlayMode)"));
        }
    }
}
