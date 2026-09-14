using System;
using System.Collections;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Persistence;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Tools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class BrushIntegrationTests
    {
        private SimulationConfigSnapshot _configSnapshot;
        private SimulationConfig _config;

        private static IEnumerator WaitForHost()
        {
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            for (int i = 0; i < 180 && host == null; i++)
            {
                yield return null;
                host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            }
            Assert.That(host, Is.Not.Null);
            for (int i = 0; i < 60 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Clock.SetRunning(false);
        }

        private IEnumerator WaitForHostAndSnapshot()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            if (host != null && host.Config != null)
            {
                _config = host.Config;
                if (_configSnapshot == null)
                    _configSnapshot = SimulationConfigSnapshot.Capture(host.Config);
            }
        }

        [UnityTearDown]
        public IEnumerator RestoreConfig()
        {
            if (_config != null && _configSnapshot != null)
                _configSnapshot.Restore(_config);
            _configSnapshot = null;
            _config = null;
            yield return null;
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static int Index(SimulationHost host, int x, int y) =>
            y * host.Grid.angularResolution + host.Grid.WrapTheta(x);

        private static int DayX(SimulationHost host) => 8;
        private static int SurfaceY(SimulationHost host) =>
            Mathf.Clamp(host.Grid.radialResolution - 10, 8, host.Grid.radialResolution - 4);

        private static void Paint(SimulationHost host, int x, int y, uint materialId)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(host.Grid.WrapTheta(x), y),
                radius = 0,
                materialId = materialId,
                values = Vector4.zero
            });
        }

        private static void FreezeWorld(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.gravityStrength = 0f;
            host.Config.thermalRate = 0f;
            host.Config.electricalRate = 0f;
            host.Config.pressureRate = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.terrainRadiativeCooling = 0f;
            host.Config.atmosphereRadiativeCooling = 0f;
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.mantlePressure = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.materialSubsteps = 1;
            host.Config.slowPassInterval = 1;
            host.Config.transportPassInterval = 1;
            host.Config.mycologyAirTransportRate = 0f;
            host.Config.mycologyWaterTransportRate = 0f;
            host.Config.mycologyDiffusionRate = 0f;
            host.Config.mycologySettlingRate = 0f;
            host.Config.mycologySporulationRate = 0f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.mycologyDecayRate = 0f;
            host.Config.phaseHysteresis = 50f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
            host.Config.grassDecayRate = 0f;
            host.Config.grassMaintenanceRate = 0f;
            host.Config.grassNightDrain = 0f;
            host.Config.grassGrowthRate = 0f;
            host.Config.detritusDecompositionRate = 0f;
        }

        private IEnumerator PrepareIsolatedWorld(SimulationHost host)
        {
            host.Config.seed = 2026;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
        }

        private static IEnumerator ReadMaterialsAndEcology(SimulationHost host, Action<uint[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.EcologyRead, 0, ecologyRequest =>
                {
                    if (ecologyRequest.hasError) { failed = true; done = true; return; }
                    consume(materials, ecologyRequest.GetData<Vector4>().ToArray());
                    done = true;
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator ReadGrassGenome(SimulationHost host, int x, int y, int slot, Action<GrassGenome.Packed> consume)
        {
            bool done = false;
            bool failed = false;
            int slice = GrassGenome.Slice(slot, GrassGenome.GenomeOffset);
            AsyncGPUReadback.Request(host.Resources.GrassRead, 0, host.Grid.WrapTheta(x), 1, y, 1, slice, 1, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                var data = request.GetData<Vector4>();
                consume(data.Length > 0 ? GrassGenome.FromFloatBits(data[0]) : default);
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        [UnityTest]
        public IEnumerator MaterialBrushPaintsDetritus()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.slowPassInterval = 100000;
            host.Config.detritusDecompositionRate = 0f;
            int x = DayX(host);
            int y = SurfaceY(host);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                    Paint(host, x + dx, y + dy, MaterialIds.Rock);
            }
            yield return Step(host, 1);
            Assert.That(SimulationTools.TryBuildBrushCommand(
                BrushMode.Material, MaterialIds.Detritus, new Vector2Int(host.Grid.WrapTheta(x), y), 0, 1f,
                out var command, out bool grassSeed), Is.True);
            Assert.That(grassSeed, Is.False);
            host.QueueBrush(command);
            yield return Step(host, 1);

            uint painted = 0;
            yield return ReadMaterialsAndEcology(host, (materials, _) =>
            {
                painted = materials[Index(host, x, y)];
            });
            Assert.That(painted, Is.EqualTo(MaterialIds.Detritus));
        }

        [UnityTest]
        public IEnumerator LifeBrushMycoSporesDepositWithWorldgenRareRate()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.mycologyRareStrainChance = 0.04f;
            int originX = DayX(host);
            int originY = SurfaceY(host) + 8;
            var center = new Vector2Int(originX, originY);
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = center,
                radius = 22,
                materialId = MaterialIds.Air,
                values = Vector4.zero
            });
            yield return Step(host, 1);

            Assert.That(SimulationTools.TryBuildBrushCommand(
                BrushMode.Life, BrushSelectionIds.MycoSpores, center, 18, 1f, out var command, out bool grassSeed), Is.True);
            Assert.That(grassSeed, Is.False);
            host.QueueBrush(command);
            yield return Step(host, 1);

            int seeded = 0;
            int rare = 0;
            yield return ReadMaterialsAndEcology(host, (materials, ecology) =>
            {
                for (int dx = -16; dx <= 16; dx++)
                {
                    for (int dy = -8; dy <= 8; dy++)
                    {
                        int y = originY + dy;
                        if (y < 0 || y >= host.Grid.radialResolution) continue;
                        int i = Index(host, originX + dx, y);
                        if (ecology[i].x <= 0.02f) continue;
                        seeded++;
                        if (MycologyTraits.FromFloat(ecology[i].z) != MycologyTraits.Basic)
                            rare++;
                    }
                }
            });

            Assert.That(seeded, Is.GreaterThan(100));
            Assert.That(rare, Is.GreaterThan(0));
            Assert.That(rare, Is.LessThan(seeded / 2));
        }

        [UnityTest]
        public IEnumerator LifeBrushGrassSeedsPlantAdultOnSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            Paint(host, x - 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x + 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Air);
            yield return Step(host, 1);

            Assert.That(SimulationTools.TryBuildBrushCommand(
                BrushMode.Life, BrushSelectionIds.GrassSeeds, new Vector2Int(x, y), 0, 1f, out var command, out bool grassSeed), Is.True);
            Assert.That(grassSeed, Is.True);
            host.QueueGrassSeed(command.center, command.radius);
            yield return Step(host, 1);

            uint stage = GrassGenome.StageEmpty;
            yield return ReadGrassGenome(host, x, y, 0, genome =>
            {
                stage = GrassGenome.Stage(genome);
            });
            Assert.That(stage, Is.EqualTo(GrassGenome.StageAdult));
        }

        [UnityTest]
        public IEnumerator LifeBrushTreeSproutPlantsWoodRootOnSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            Paint(host, x - 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x + 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Air);
            yield return Step(host, 1);

            Assert.That(SimulationTools.TryBuildBrushCommand(
                BrushMode.Life, BrushSelectionIds.TreeSprouts, new Vector2Int(x, y), 0, 1f, out var command, out bool grassSeed), Is.True);
            Assert.That(grassSeed, Is.False);
            host.QueueTreeSprout(command.center, command.radius);
            yield return Step(host, 1);

            uint material = 0;
            uint stage = TreeGenome.StageEmpty;
            yield return ReadMaterialsAndEcology(host, (materials, _) => material = materials[Index(host, x, y)]);
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.TreeRead, 0, host.Grid.WrapTheta(x), 1, y, 1, TreeGenome.GenomeSlice, 1, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                var data = request.GetData<Vector4>();
                if (data.Length > 0)
                    stage = TreeGenome.Stage(TreeGenome.FromFloatBits(data[0]));
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(material, Is.EqualTo(MaterialIds.Wood));
            Assert.That(stage, Is.EqualTo(TreeGenome.StageSprout));
        }

        private sealed class SimulationConfigSnapshot
        {
            private readonly FieldInfo[] fields;
            private readonly object[] values;

            private SimulationConfigSnapshot(FieldInfo[] fields, object[] values)
            {
                this.fields = fields;
                this.values = values;
            }

            public static SimulationConfigSnapshot Capture(SimulationConfig config)
            {
                FieldInfo[] captured = typeof(SimulationConfig).GetFields(BindingFlags.Instance | BindingFlags.Public);
                var values = new object[captured.Length];
                for (int i = 0; i < captured.Length; i++)
                    values[i] = captured[i].GetValue(config);
                return new SimulationConfigSnapshot(captured, values);
            }

            public void Restore(SimulationConfig config)
            {
                for (int i = 0; i < fields.Length; i++)
                    fields[i].SetValue(config, values[i]);
            }
        }
    }
}
