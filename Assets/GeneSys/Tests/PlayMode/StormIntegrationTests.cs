using System;
using System.Collections;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class StormIntegrationTests
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

        private static IEnumerator ReadFields(SimulationHost host,
            Action<uint[], Vector4[], Vector4[], Vector4[], Vector2[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.StateRead, 0, stateRequest =>
                {
                    if (stateRequest.hasError) { failed = true; done = true; return; }
                    Vector4[] states = stateRequest.GetData<Vector4>().ToArray();
                    AsyncGPUReadback.Request(host.Resources.AuxRead, 0, auxRequest =>
                    {
                        if (auxRequest.hasError) { failed = true; done = true; return; }
                        Vector4[] aux = auxRequest.GetData<Vector4>().ToArray();
                        AsyncGPUReadback.Request(host.Resources.CombustionRead, 0, combustionRequest =>
                        {
                            if (combustionRequest.hasError) { failed = true; done = true; return; }
                            Vector4[] combustion = combustionRequest.GetData<Vector4>().ToArray();
                            AsyncGPUReadback.Request(host.Resources.FlowRead, 0, flowRequest =>
                            {
                                if (flowRequest.hasError) { failed = true; done = true; return; }
                                Vector2[] flow = flowRequest.GetData<Vector2>().ToArray();
                                AsyncGPUReadback.Request(host.Resources.StormRead, 0, stormRequest =>
                                {
                                    if (stormRequest.hasError) { failed = true; done = true; return; }
                                    consume(materials, states, aux, combustion, flow, stormRequest.GetData<Vector4>().ToArray());
                                    done = true;
                                });
                            });
                        });
                    });
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static int Index(SimulationHost host, int x, int y) => y * host.Grid.angularResolution + x;

        private static int SurfaceY(SimulationHost host) =>
            Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.86f), 4, host.Grid.radialResolution - 8);

        private static void Paint(SimulationHost host, int x, int y, uint materialId)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = materialId,
                values = Vector4.zero
            });
        }

        private static void PaintField(SimulationHost host, int x, int y, float mode, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(mode, amount, 0f, 0f)
            });
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
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
            host.Config.precipitationRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.windDamping = 0f;
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
            host.Config.slowPassInterval = 64;
            host.Config.mycologyAirTransportRate = 0f;
            host.Config.mycologyWaterTransportRate = 0f;
            host.Config.mycologyDiffusionRate = 0f;
            host.Config.mycologySettlingRate = 0f;
            host.Config.mycologySporulationRate = 0f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.mycologyDecayRate = 0f;
            host.Config.phaseHysteresis = 50f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.combustionIgnitionAccumulationRate = 0f;
            host.Config.combustionOxygenDiffusionRate = 0f;
            host.Config.stormChargeSeparationRate = 0f;
            host.Config.stormChargeLeakRate = 0f;
            host.Config.stormChargeDiffusionRate = 0f;
            host.Config.stormChargeAdvectionRate = 0f;
            host.Config.stormBreakdownAccumulationRate = 0f;
            host.Config.stormMaxStrikesPerTick = 0;
            host.Config.stormChannelDecay = 0f;
            host.Config.stormFlashDecay = 0f;
            host.Config.stormFlashDiffusion = 0f;
            host.Config.stormFlashVaporization = 0f;
            host.Config.stormMinimumHeight = 0;
            host.Config.coreHeatRate = 0f;
        }

        private IEnumerator PrepareIsolatedWorld(SimulationHost host)
        {
            host.Config.seed = 2026;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
        }

        private static void StampSurfaceColumn(SimulationHost host, int x, int y, uint surfaceMaterial)
        {
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x, y, surfaceMaterial);
            for (int dy = 1; dy <= 6; dy++)
                Paint(host, x, y + dy, MaterialIds.Air);
        }

        private static void StampSupportedSurface(SimulationHost host, int x, int y, uint surfaceMaterial)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int px = x + dx;
                Paint(host, px, y - 2, MaterialIds.Rock);
                Paint(host, px, y - 1, MaterialIds.Rock);
                Paint(host, px, y, surfaceMaterial);
                for (int dy = 1; dy <= 6; dy++)
                    Paint(host, px, y + dy, MaterialIds.Air);
            }
        }

        private static void SealNearbySurface(SimulationHost host, int x, int y, int range)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                if (Mathf.Abs(dx) <= 2) continue;
                int px = x + dx;
                Paint(host, px, y - 1, MaterialIds.Rock);
                Paint(host, px, y, MaterialIds.Rock);
                Paint(host, px, y + 1, MaterialIds.Air);
            }
        }

        private static void MaxPlatform(SimulationHost host, int x, int y, Vector4[] states, Vector4[] combustion, Vector4[] storm,
            out float maxLuminance, out float maxTemp, out float maxIgnition)
        {
            maxLuminance = 0f;
            maxTemp = float.MinValue;
            maxIgnition = 0f;
            for (int dx = -2; dx <= 2; dx++)
            {
                int i = Index(host, x + dx, y);
                maxLuminance = Mathf.Max(maxLuminance, storm[i].y);
                maxTemp = Mathf.Max(maxTemp, states[i].x);
                if (combustion != null)
                    maxIgnition = Mathf.Max(maxIgnition, combustion[i].w);
            }
        }

        [UnityTest]
        public IEnumerator ChargeBuildsInColdMoistUpdraftNotWarmDryAir()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.stormChargeSeparationRate = 3.5f;
            host.Config.stormRimingTempMin = -25f;
            host.Config.stormRimingTempMax = 5f;
            host.Config.stormMaxStrikesPerTick = 0;

            int y = SurfaceY(host);
            int cold = 24;
            int warm = 48;
            StampSurfaceColumn(host, cold, y, MaterialIds.Soil);
            StampSurfaceColumn(host, warm, y, MaterialIds.Soil);
            yield return Step(host, 1);

            int airY = y + 2;
            for (int i = 0; i < 8; i++)
            {
                PaintField(host, cold, airY, 1f, -10f);
                PaintField(host, cold, airY, 2f, 0.6f);
                PaintField(host, cold, airY, 12f, 4f);
                PaintField(host, warm, airY, 1f, 28f);
                PaintField(host, warm, airY, 2f, -10f);
                PaintField(host, warm, airY, 12f, 4f);
                yield return Step(host, 1);
            }

            yield return ReadFields(host, (_, __, ___, ____, _____, storm) =>
            {
                Assert.That(Mathf.Abs(storm[Index(host, cold, airY)].x), Is.GreaterThan(0.02f));
                Assert.That(Mathf.Abs(storm[Index(host, warm, airY)].x), Is.LessThan(0.02f));
            });
        }

        [UnityTest]
        public IEnumerator BreakdownFiresAndDrainsChargePocket()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.stormBreakdownThreshold = 0.05f;
            host.Config.stormBreakdownAccumulationRate = 8f;
            host.Config.stormMaxStrikesPerTick = 4;
            host.Config.stormChannelChargeDrain = 0.85f;
            host.Config.stormCooldownRate = 0.01f;
            host.Config.stormTargetRange = 24;
            host.Config.stormMaxChannelLength = 64;

            int x = 40;
            int y = SurfaceY(host);
            StampSurfaceColumn(host, x, y, MaterialIds.Soil);
            yield return Step(host, 1);

            int originY = y + 5;
            PaintField(host, x, originY, 11f, 2.5f);
            PaintField(host, x - 1, originY, 11f, -2.5f);
            PaintField(host, x + 1, originY, 11f, -2.5f);
            PaintField(host, x, originY, 13f, 1f);
            yield return Step(host, 1);

            float chargeAfter = 0f;
            bool sawChannel = false;
            yield return ReadFields(host, (_, __, ___, ____, _____, storm) =>
            {
                chargeAfter = storm[Index(host, x, originY)].x;
                sawChannel = storm[Index(host, x, originY)].y > 0.1f || storm[Index(host, x, originY)].w < 0f;
                for (int dy = 0; dy <= 5 && !sawChannel; dy++)
                    sawChannel |= storm[Index(host, x, y + dy)].y > 0.08f;
            });

            Assert.That(sawChannel, Is.True);
            Assert.That(Mathf.Abs(chargeAfter), Is.LessThan(2.4f));
        }

        [UnityTest]
        public IEnumerator StrikeChannelConnectsCloudToSurface()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.stormBreakdownThreshold = 0.05f;
            host.Config.stormBreakdownAccumulationRate = 8f;
            host.Config.stormMaxStrikesPerTick = 4;
            host.Config.stormTargetRange = 8;
            host.Config.stormMaxChannelLength = 64;
            host.Config.stormStrikeBranchChance = 0f;
            host.Config.stormTortuosity = 0f;

            int x = 36;
            int y = SurfaceY(host);
            StampSurfaceColumn(host, x, y, MaterialIds.Soil);
            yield return Step(host, 1);

            int originY = y + 5;
            PaintField(host, x, originY, 11f, 2.2f);
            PaintField(host, x, originY - 1, 11f, -0.2f);
            PaintField(host, x, originY, 13f, 1f);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, ___, ____, _____, storm) =>
            {
                int lit = 0;
                for (int yy = y; yy <= originY; yy++)
                {
                    if (storm[Index(host, x, yy)].y > 0.08f)
                        lit++;
                }
                Assert.That(lit, Is.GreaterThan(1));
                Assert.That(storm[Index(host, x, y)].y + storm[Index(host, x, originY)].y, Is.GreaterThan(0.2f));
            });
        }

        [UnityTest]
        public IEnumerator StrikePrefersMetalOverEquidistantSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.stormBreakdownThreshold = 0.05f;
            host.Config.stormBreakdownAccumulationRate = 8f;
            host.Config.stormMaxStrikesPerTick = 4;
            host.Config.stormTargetRange = 24;
            host.Config.stormMaxChannelLength = 80;
            host.Config.stormStrikeHeat = 120f;
            host.Config.stormChargeDeposit = 2f;
            host.Config.stormIgnitionImpulse = 1f;
            host.Config.stormTortuosity = 0f;
            host.Config.stormStrikeBranchChance = 0f;

            int originX = 64;
            int y = SurfaceY(host);
            int soilX = originX - 8;
            int metalX = originX + 8;
            StampSurfaceColumn(host, originX, y, MaterialIds.Soil);
            StampSurfaceColumn(host, soilX, y, MaterialIds.Soil);
            StampSurfaceColumn(host, metalX, y, MaterialIds.Metal);
            yield return Step(host, 1);

            int originY = y + 5;
            PaintField(host, originX, originY, 11f, 2.4f);
            PaintField(host, originX, originY - 1, 11f, -0.3f);
            PaintField(host, originX, originY, 13f, 1f);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, states, __, combustion, ___, storm) =>
            {
                int metal = Index(host, metalX, y);
                int soil = Index(host, soilX, y);
                float metalHit = storm[metal].y + Mathf.Abs(states[metal].w) + combustion[metal].w;
                float soilHit = storm[soil].y + Mathf.Abs(states[soil].w) + combustion[soil].w;
                Assert.That(metalHit, Is.GreaterThan(soilHit + 0.05f));
            });
        }

        [UnityTest]
        public IEnumerator TerminusHeatsAndIgnitesFueledSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.stormBreakdownThreshold = 0.05f;
            host.Config.stormBreakdownAccumulationRate = 8f;
            host.Config.stormMaxStrikesPerTick = 4;
            host.Config.stormStrikeHeat = 140f;
            host.Config.stormIgnitionImpulse = 1f;
            host.Config.stormTargetRange = 12;
            host.Config.stormMaxChannelLength = 64;
            host.Config.stormTortuosity = 0f;
            host.Config.stormStrikeAirHeatFraction = 0.3f;
            host.Config.stormMinimumHeight = 0;
            host.Config.combustionIgnitionAccumulationRate = 8f;
            host.Config.combustionMinFuel = 0.02f;
            host.Config.combustionMoistureIgnitionPenalty = 0f;

            int x = 30;
            int y = SurfaceY(host);
            StampSupportedSurface(host, x, y, MaterialIds.Soil);
            SealNearbySurface(host, x, y, 12);
            Paint(host, x, y, MaterialIds.Metal);
            yield return Step(host, 1);

            for (int dx = -2; dx <= 2; dx++)
            {
                host.QueueSporeSeed(new Vector2Int(x + dx, y), 0, 0.2f, 0.9f, MycologyTraits.Basic);
                host.QueueOxygen(new Vector2Int(x + dx, y), 0, 1f);
                PaintField(host, x + dx, y, 2f, -10f);
            }
            yield return Step(host, 1);

            float tempBefore = 0f;
            yield return ReadFields(host, (materials, states, __, ___, ____, _____) =>
            {
                Assert.That(materials[Index(host, x, y)], Is.EqualTo(MaterialIds.Metal));
                tempBefore = states[Index(host, x, y)].x;
            });

            int originY = y + 5;
            PaintField(host, x, originY, 11f, 2.3f);
            PaintField(host, x, originY - 1, 11f, -0.3f);
            PaintField(host, x, originY, 13f, 1f);
            yield return Step(host, 1);

            yield return ReadFields(host, (materials, states, __, combustion, ___, storm) =>
            {
                MaxPlatform(host, x, y, states, combustion, storm, out float lit, out float maxTemp, out float ignition);
                Assert.That(materials[Index(host, x, y)], Is.EqualTo(MaterialIds.Metal));
                Assert.That(lit, Is.GreaterThan(0.15f), "strike should terminate on the metal pad");
                Assert.That(maxTemp, Is.GreaterThan(tempBefore + 5f));
                Assert.That(ignition, Is.GreaterThan(0.4f));
            });
        }

        [UnityTest]
        public IEnumerator StrikeHeatsAirLessThanSolidTerminus()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.stormBreakdownThreshold = 0.05f;
            host.Config.stormBreakdownAccumulationRate = 8f;
            host.Config.stormMaxStrikesPerTick = 4;
            host.Config.stormStrikeHeat = 160f;
            host.Config.stormStrikeAirHeatFraction = 0f;
            host.Config.stormIgnitionImpulse = 0f;
            host.Config.stormFlashVaporization = 0f;
            host.Config.stormTargetRange = 12;
            host.Config.stormMaxChannelLength = 64;
            host.Config.stormTortuosity = 0f;
            host.Config.thermalRate = 0f;
            host.Config.terrainRadiativeCooling = 0f;
            host.Config.atmosphereRadiativeCooling = 0f;
            host.Config.surfaceAirHeatExchange = 0f;

            int x = 34;
            int y = SurfaceY(host);
            StampSupportedSurface(host, x, y, MaterialIds.Rock);
            SealNearbySurface(host, x, y, 12);
            Paint(host, x, y, MaterialIds.Metal);
            yield return Step(host, 1);

            float solidBefore = 0f;
            float airBefore = 0f;
            yield return ReadFields(host, (materials, states, __, ___, ____, _____) =>
            {
                Assert.That(materials[Index(host, x, y)], Is.EqualTo(MaterialIds.Metal));
                Assert.That(materials[Index(host, x, y + 3)], Is.EqualTo(MaterialIds.Air));
                solidBefore = states[Index(host, x, y)].x;
                airBefore = states[Index(host, x, y + 3)].x;
            });

            int originY = y + 5;
            PaintField(host, x, originY, 11f, 2.3f);
            PaintField(host, x, originY - 1, 11f, -0.3f);
            PaintField(host, x, originY, 13f, 1f);
            yield return Step(host, 1);

            yield return ReadFields(host, (materials, states, __, combustion, ____, storm) =>
            {
                MaxPlatform(host, x, y, states, combustion, storm, out float lit, out float maxTemp, out _);
                float solidDelta = maxTemp - solidBefore;
                float airDelta = states[Index(host, x, y + 3)].x - airBefore;
                Assert.That(materials[Index(host, x, y)], Is.EqualTo(MaterialIds.Metal));
                Assert.That(storm[Index(host, x, originY)].y, Is.GreaterThan(0.15f), "strike should light the air channel");
                Assert.That(lit, Is.GreaterThan(0.15f), "strike should terminate on the metal pad");
                Assert.That(solidDelta, Is.GreaterThan(8f));
                Assert.That(airDelta, Is.LessThan(solidDelta * 0.35f));
            });
        }

        [UnityTest]
        public IEnumerator StrikeWritesThunderPressureAlongChannel()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.stormBreakdownThreshold = 0.05f;
            host.Config.stormBreakdownAccumulationRate = 8f;
            host.Config.stormMaxStrikesPerTick = 4;
            host.Config.stormThunderPressure = 1.2f;
            host.Config.stormTargetRange = 8;
            host.Config.stormMaxChannelLength = 64;
            host.Config.stormTortuosity = 0f;

            int x = 22;
            int y = SurfaceY(host);
            StampSurfaceColumn(host, x, y, MaterialIds.Soil);
            yield return Step(host, 1);

            float pressureBefore = 0f;
            yield return ReadFields(host, (_, states, __, ___, ____, _____) =>
            {
                pressureBefore = states[Index(host, x, y + 3)].y;
            });

            PaintField(host, x, y + 5, 11f, 2.2f);
            PaintField(host, x, y + 4, 11f, -0.2f);
            PaintField(host, x, y + 5, 13f, 1f);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, states, __, ___, ____, storm) =>
            {
                float maxPressure = pressureBefore;
                for (int yy = y; yy <= y + 5; yy++)
                {
                    if (storm[Index(host, x, yy)].y > 0.05f)
                        maxPressure = Mathf.Max(maxPressure, states[Index(host, x, yy)].y);
                }
                Assert.That(maxPressure, Is.GreaterThan(pressureBefore + 0.05f));
            });
        }

        [UnityTest]
        public IEnumerator FlashVaporizationConservesTrackedWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.stormBreakdownThreshold = 0.05f;
            host.Config.stormBreakdownAccumulationRate = 8f;
            host.Config.stormMaxStrikesPerTick = 4;
            host.Config.stormFlashVaporization = 1f;
            host.Config.stormStrikeHeat = 160f;
            host.Config.stormTargetRange = 12;
            host.Config.stormMaxChannelLength = 64;
            host.Config.stormTortuosity = 0f;
            host.Config.stormMinimumHeight = 0;

            int x = 34;
            int y = SurfaceY(host);
            StampSupportedSurface(host, x, y, MaterialIds.Rock);
            SealNearbySurface(host, x, y, 12);
            Paint(host, x, y, MaterialIds.Metal);
            yield return Step(host, 1);

            yield return ReadFields(host, (materials, _, __, ___, ____, _____) =>
            {
                Assert.That(materials[Index(host, x, y)], Is.EqualTo(MaterialIds.Metal));
            });

            PaintField(host, x, y, 2f, 0.8f);
            PaintField(host, x, y + 5, 11f, 2.3f);
            PaintField(host, x, y + 4, 11f, -0.3f);
            PaintField(host, x, y + 5, 13f, 1f);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, states, aux, __, ___, storm) =>
            {
                MaxPlatform(host, x, y, states, null, storm, out float lit, out float unusedTemp, out float unusedIgnition);
                int i = Index(host, x, y);
                float film = Mathf.Max(0f, states[i].z);
                float vapor = Mathf.Max(0f, aux[i].x);
                Assert.That(lit, Is.GreaterThan(0.15f), "strike should terminate on the wet metal pad");
                Assert.That(vapor, Is.GreaterThan(0.05f));
                Assert.That(film, Is.LessThan(0.8f));
                Assert.That(film + vapor + Mathf.Max(0f, aux[i].y), Is.GreaterThan(0.4f));
            });
        }

        [UnityTest]
        public IEnumerator SheetDischargeStaysInAtmosphere()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.stormBreakdownThreshold = 0.05f;
            host.Config.stormBreakdownAccumulationRate = 8f;
            host.Config.stormMaxStrikesPerTick = 4;
            host.Config.stormSheetBranchChance = 0.8f;
            host.Config.stormMaxChannelLength = 40;
            host.Config.stormTargetRange = 4;

            int x = 80;
            int y = Mathf.Clamp(host.Grid.radialResolution - 5, SurfaceY(host) + 8, host.Grid.radialResolution - 2);
            for (int dx = -3; dx <= 3; dx++)
            {
                int xx = (x + dx + host.Grid.angularResolution) % host.Grid.angularResolution;
                Paint(host, xx, y, MaterialIds.Air);
                Paint(host, xx, y - 1, MaterialIds.Air);
                Paint(host, xx, y + 1, MaterialIds.Air);
            }
            yield return Step(host, 1);

            PaintField(host, x, y, 11f, 2.4f);
            PaintField(host, x - 1, y, 11f, -2.4f);
            PaintField(host, x + 1, y, 11f, -2.4f);
            PaintField(host, x, y - 1, 11f, 2.4f);
            PaintField(host, x, y, 13f, 1f);
            yield return Step(host, 1);

            yield return ReadFields(host, (materials, _, __, ___, ____, storm) =>
            {
                bool sawSheet = false;
                bool hitSolid = false;
                int width = host.Grid.angularResolution;
                int height = host.Grid.radialResolution;
                for (int i = 0; i < storm.Length; i++)
                {
                    if (storm[i].y <= 0.05f) continue;
                    int yy = i / width;
                    int xx = i - yy * width;
                    uint material = materials[i];
                    if (yy == y || Mathf.Abs(yy - y) <= 2)
                        sawSheet = true;
                    bool atmosphere = material == MaterialIds.Air || material == MaterialIds.Vapor || material == MaterialIds.Void;
                    if (!atmosphere && yy < height - 1)
                        hitSolid = true;
                }
                Assert.That(sawSheet, Is.True);
                Assert.That(hitSolid, Is.False);
            });
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
                FieldInfo[] fields = typeof(SimulationConfig).GetFields(BindingFlags.Instance | BindingFlags.Public);
                var values = new object[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                    values[i] = fields[i].GetValue(config);
                return new SimulationConfigSnapshot(fields, values);
            }

            public void Restore(SimulationConfig config)
            {
                for (int i = 0; i < fields.Length; i++)
                    fields[i].SetValue(config, values[i]);
            }
        }
    }
}
