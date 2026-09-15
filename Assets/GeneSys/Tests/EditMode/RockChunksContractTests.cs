using System.IO;
using System.Reflection;
using GeneSys.Configuration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class RockChunksContractTests
    {
        [Test]
        public void RockChunksComputeShaderHasRequiredKernels()
        {
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/RockChunks.compute");
            Assert.That(shader, Is.Not.Null, "RockChunks.compute could not be loaded.");
            Assert.That(shader.FindKernel("RelaxHops"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ResetSearchAccum"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("SeedUndercutRock"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ExpandSearch"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("FinalizeSearch"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("CaptureMembers"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("AfterCapture"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ApplySearchOutcome"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("IntegrateChunks"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ClearClaims"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ClaimDestinations"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ResolveCollisions"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("CommitChunkMove"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("StampRockSupport"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("UpdateMemberPositions"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("FreeSettledSlots"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void RockChunksConfigDefaultsAndClamps()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.enableRockChunks, Is.True);
            Assert.That(config.rockChunkMaxSearchTicks, Is.EqualTo(40));
            Assert.That(config.rockChunkHopsPerTick, Is.EqualTo(1));
            Assert.That(config.rockChunkBasementRelaxations, Is.EqualTo(8));
            Assert.That(config.rockChunkMaxConcurrent, Is.EqualTo(32));
            Assert.That(config.rockChunkMaxCells, Is.EqualTo(96));
            Assert.That(config.rockChunkMinCells, Is.EqualTo(3));

            config.rockChunkMaxSearchTicks = 1;
            config.rockChunkHopsPerTick = 0;
            config.rockChunkBasementRelaxations = 99;
            config.rockChunkMaxConcurrent = 100;
            config.rockChunkMaxCells = 4;
            config.rockChunkMinCells = 1;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);

            Assert.That(config.rockChunkMaxSearchTicks, Is.EqualTo(2));
            Assert.That(config.rockChunkHopsPerTick, Is.EqualTo(1));
            Assert.That(config.rockChunkBasementRelaxations, Is.EqualTo(16));
            Assert.That(config.rockChunkMaxConcurrent, Is.EqualTo(32));
            Assert.That(config.rockChunkMaxCells, Is.EqualTo(8));
            Assert.That(config.rockChunkMinCells, Is.EqualTo(2));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void SimulationHostAndSchedulerWireRockChunks()
        {
            string host = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/SimulationHost.cs");
            Assert.That(host.Contains("rockChunks"), "SimulationHost.cs should declare rockChunks");
            Assert.That(host.Contains("RockChunks.compute"), "SimulationHost.cs should reference RockChunks.compute");

            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler.Contains("rockChunks"), "GpuPassScheduler.cs should declare rockChunks");
            Assert.That(scheduler.Contains("DispatchRockChunks"), "GpuPassScheduler.cs should implement DispatchRockChunks");

            string transport = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MargolusTransport.compute");
            Assert.That(transport, Does.Contain("_RockChunksEnabled"));
            Assert.That(transport, Does.Contain("RockShouldHoldForChunk"));
        }
    }
}
