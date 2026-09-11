using System.IO;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GeneSys.Tests
{
    public sealed class MaceTests
    {
        [Test]
        public void PhaseFlagsDefaultOffAndOnValidateClampsThresholds()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.maceSedimentPilot, Is.False);
            Assert.That(config.maceEntrainment, Is.False);
            Assert.That(config.maceShorelineSorting, Is.False);
            Assert.That(config.maceSolute, Is.False);
            Assert.That(config.maceAsh, Is.False);
            Assert.That(config.maceMagma, Is.False);
            Assert.That(config.maceHardWear, Is.False);

            config.maceSedimentFillThreshold = 1.4f;
            config.maceSedimentClearThreshold = 2f;
            config.maceHardCrustAlpha = 0.5f;
            config.maceMinMass = 0f;
            config.maceBeta = -3f;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.maceSedimentFillThreshold, Is.InRange(0f, 1f));
            Assert.That(config.maceSedimentClearThreshold, Is.LessThanOrEqualTo(config.maceSedimentFillThreshold));
            Assert.That(config.maceHardCrustAlpha, Is.InRange(0f, 0.01f));
            Assert.That(config.maceMinMass, Is.GreaterThan(0f));
            Assert.That(config.maceBeta, Is.GreaterThanOrEqualTo(0f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void LedgerLayoutAndKernelsMatchPilotContract()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.MobileMassRead.volumeDepth, Is.EqualTo(SimulationResources.MobileMassSliceCount));
            Assert.That(resources.MobileMassRead.volumeDepth, Is.EqualTo(6));
            Assert.That(resources.MobileMassRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(resources.MobileMassWrite.graphicsFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(resources.MaceAffinity.graphicsFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(resources.MaceNormalizer.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32_SFloat));
            Assert.That(resources.SedimentColumn, Is.Not.Null);

            string swap = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs");
            Assert.That(swap.Contains("SwapMobileMass"));
            Assert.That(swap.Contains("MaCE mobile/scratch are excluded"));

            ComputeShader mace = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/MaceTransport.compute");
            Assert.That(mace, Is.Not.Null);
            foreach (string kernel in new[]
            {
                "SeedFromMaterials", "ApplyMobileEdits", "SyncLegacyIds", "BuildSedimentColumns",
                "BuildAffinity", "BuildNormalizer", "RedistributeChannel", "OverflowProject",
                "ExtractStructural", "CrossChannelMobile", "AcknowledgeTransfers", "ReconcileIds"
            })
                Assert.That(mace.FindKernel(kernel), Is.GreaterThanOrEqualTo(0), kernel);
        }

        [Test]
        public void SnapshotV14ReservesSixPersistentSlices()
        {
            string snapshot = File.ReadAllText("Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs");
            Assert.That(snapshot.Contains("private const int Version14 = 14;"));
            Assert.That(snapshot.Contains("private const int PayloadCountV14 = 47;"));
            Assert.That(snapshot.Contains("RequestMobileMassBatch"));
            Assert.That(snapshot.Contains("SeedMobileMassFromMaterialsCpu"));
        }

        [Test]
        public void SedimentReposeIsPackedForPhysicalSlope()
        {
            MaterialDefinition sediment = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/008_Sediment.asset");
            Assert.That(sediment, Is.Not.Null);
            Assert.That(sediment.angleOfRepose, Is.EqualTo(25f).Within(0.01f));
            MaterialGpuData gpu = sediment.ToGpuData();
            Assert.That(gpu.physical.z, Is.EqualTo(sediment.angleOfRepose * Mathf.Deg2Rad).Within(0.001f));

            string common = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/MaceCommon.hlsl");
            Assert.That(common.Contains("MacePhysicalArc"));
            Assert.That(common.Contains("MaceTanRepose"));
            string structs = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl");
            Assert.That(structs.Contains("One full pixel is mass 1"));
            Assert.That(structs.Contains("state.z remains the water ledger"));
            Assert.That(File.Exists("Assets/MaCE_SedimentPilot.MD"));
        }

        [Test]
        public void DisplayAndBuilderWireTheMobileLedger()
        {
            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display.Contains("_MobileMassTex"));
            Assert.That(MaceVisuals.OverlayMode, Is.GreaterThan(0));
            string builder = File.ReadAllText("Assets/GeneSys/Editor/GeneSysProjectBuilder.cs");
            Assert.That(builder.Contains("MaceTransport.compute"));
            string host = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/SimulationHost.cs");
            Assert.That(host.Contains("maceTransport"));
        }

        [Test]
        public void StandardGridPingPongCostStaysNearTwentyFourMegabytes()
        {
            PolarGridDefinition standard = PolarGridDefinition.Standard;
            PolarGridDefinition stress = PolarGridDefinition.Stress;
            long standardBytes = (long)standard.CellCount * sizeof(float) * SimulationResources.MobileMassSliceCount * 2;
            long stressBytes = (long)stress.CellCount * sizeof(float) * SimulationResources.MobileMassSliceCount * 2;
            Assert.That(standardBytes, Is.EqualTo(24L * 1024 * 1024).Within(2L * 1024 * 1024));
            Assert.That(stressBytes, Is.GreaterThan(standardBytes));
            Assert.That(SimulationResources.MobileMassSliceCount, Is.EqualTo(6));
        }
    }
}
