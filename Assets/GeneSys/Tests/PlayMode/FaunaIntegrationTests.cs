using System;
using System.Collections;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Persistence;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class FaunaIntegrationTests
    {
        private SimulationConfigSnapshot _configSnapshot;
        private SimulationConfig _config;

        private static IEnumerator WaitForHost()
        {
            if (UnityEngine.Object.FindFirstObjectByType<SimulationHost>() == null)
            {
                SceneManager.LoadScene("Terrarium");
                yield return null;
            }
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

        private static IEnumerator ResetWorld(SimulationHost host)
        {
            host.Regenerate();
            for (int i = 0; i < 8; i++)
                yield return null;
            host.Clock.SetRunning(false);
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

        private static IEnumerator ReadWorldAndFauna(SimulationHost host,
            Action<uint[], Vector4[], Vector4[], FaunaGenome.Packed[], Vector2[]> consume)
        {
            bool done = false;
            bool failed = false;
            Exception consumeError = null;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.LifeGenomeRead, 0, 0, host.Resources.LifeGenomeRead.width, 0, host.Resources.LifeGenomeRead.height, 0, 1, lifeRequest =>
                {
                    if (lifeRequest.hasError) { failed = true; done = true; return; }
                    Vector4[] life = lifeRequest.GetData<Vector4>().ToArray();
                    AsyncGPUReadback.Request(host.Resources.FaunaRead, 0, 0, host.Resources.FaunaRead.width, 0, host.Resources.FaunaRead.height, 0, 1, vitalsRequest =>
                    {
                        if (vitalsRequest.hasError) { failed = true; done = true; return; }
                        Vector4[] vitals = vitalsRequest.GetData<Vector4>().ToArray();
                        AsyncGPUReadback.Request(host.Resources.FaunaRead, 0, 0, host.Resources.FaunaRead.width, 0, host.Resources.FaunaRead.height, 2, 1, genomeRequest =>
                        {
                            if (genomeRequest.hasError) { failed = true; done = true; return; }
                            Vector4[] bits = genomeRequest.GetData<Vector4>().ToArray();
                            var genomes = new FaunaGenome.Packed[bits.Length];
                            for (int i = 0; i < bits.Length; i++)
                                genomes[i] = FaunaGenome.FromFloatBits(bits[i]);
                            AsyncGPUReadback.Request(host.Resources.AcousticRead, 0, acousticRequest =>
                            {
                                if (acousticRequest.hasError) { failed = true; done = true; return; }
                                try
                                {
                                    consume(materials, life, vitals, genomes, acousticRequest.GetData<Vector2>().ToArray());
                                }
                                catch (Exception exception)
                                {
                                    consumeError = exception;
                                }
                                done = true;
                            });
                        });
                    });
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
            if (consumeError != null)
                throw consumeError;
        }

        private static int Index(SimulationHost host, int x, int y) =>
            y * host.Grid.angularResolution + host.Grid.WrapTheta(x);

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
            host.Config.gravityStrength = 0.35f;
            host.Config.thermalRate = 0f;
            host.Config.electricalRate = 0f;
            host.Config.pressureRate = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.terrainSolarHeating = 0f;
            host.Config.atmosphereSolarHeating = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.materialSubsteps = 1;
            host.Config.slowPassInterval = 1;
            host.Config.transportPassInterval = 1;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.faunaSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
            host.Config.floraGrowthRate = 0f;
            host.Config.floraPhotosynthesisRate = 0f;
            host.Config.floraSporulationRate = 0f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.combustionBurnRate = 0f;
            host.Config.combustionIgnitionAccumulationRate = 0f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.faunaGeneExpressionRange = 0f;
            host.Config.faunaWanderRate = 0f;
            host.Config.faunaHopImpulse = 0f;
            host.Config.faunaDecisionInterval = 1;
            host.Config.faunaMateCooldownTicks = 400;
            host.Config.faunaReproduceCooldownTicks = 800;
        }

        private static void StampSurface(SimulationHost host, int x, int y)
        {
            for (int dx = -6; dx <= 6; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Soil);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
                Paint(host, x + dx, y + 2, MaterialIds.Air);
            }
        }

        private static int CountMaterial(uint[] materials, uint id)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == id) count++;
            return count;
        }

        private static int FindMaterialX(SimulationHost host, uint[] materials, uint id, int y)
        {
            int width = host.Grid.angularResolution;
            for (int x = 0; x < width; x++)
                if (materials[Index(host, x, y)] == id) return x;
            return -1;
        }

        [UnityTest]
        public IEnumerator PaintedCricketSeedsAdultVitals()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            int x = 12;
            int y = SurfaceY(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            yield return Step(host, 2);
            yield return ReadWorldAndFauna(host, (materials, _, vitals, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                Assert.That(materials[i], Is.EqualTo(MaterialIds.Cricket));
                Assert.That(FaunaGenome.Stage(genomes[i]), Is.EqualTo(FaunaGenome.StageAdult));
                Assert.That(vitals[i].x, Is.GreaterThan(0.1f));
                Assert.That(vitals[i].y, Is.GreaterThan(0.1f));
            });
        }

        [UnityTest]
        public IEnumerator HydrationAndCaloriesDrainWithoutFood()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            host.Config.faunaMaintenanceRate = 0.8f;
            host.Config.faunaHydrationDrain = 0.8f;
            int x = 16;
            int y = SurfaceY(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            yield return Step(host, 2);
            Vector4 first = Vector4.zero;
            yield return ReadWorldAndFauna(host, (_, _, vitals, _, _) => first = vitals[Index(host, x, y + 1)]);
            yield return Step(host, 12);
            yield return ReadWorldAndFauna(host, (_, _, vitals, _, _) =>
            {
                Vector4 later = vitals[Index(host, x, y + 1)];
                Assert.That(later.x, Is.LessThan(first.x - 0.01f));
                Assert.That(later.y, Is.LessThan(first.y - 0.01f));
            });
        }

        [UnityTest]
        public IEnumerator CricketEatsAdjacentAlgaeAndGainsCalories()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            host.Config.faunaMaintenanceRate = 0f;
            host.Config.faunaHydrationDrain = 0f;
            host.Config.faunaHungerThreshold = 0.99f;
            host.Config.faunaFullThreshold = 1f;
            host.Config.faunaFeedCost = 0f;
            host.Config.gravityStrength = 0f;
            int x = 20;
            int y = SurfaceY(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            yield return Step(host, 2);
            float caloriesBefore = 0f;
            yield return ReadWorldAndFauna(host, (_, _, vitals, _, _) =>
            {
                caloriesBefore = vitals[Index(host, x, y + 1)].x;
                Assert.That(caloriesBefore, Is.GreaterThan(0.05f));
            });
            Paint(host, x + 1, y + 1, MaterialIds.Algae);
            host.QueueFloraSeed(new Vector2Int(host.Grid.WrapTheta(x + 1), y + 1), 0, 0.85f);
            yield return Step(host, 8);
            yield return ReadWorldAndFauna(host, (materials, _, vitals, _, _) =>
            {
                Assert.That(materials[Index(host, x + 1, y + 1)], Is.Not.EqualTo(MaterialIds.Algae));
                Assert.That(vitals[Index(host, x, y + 1)].x, Is.GreaterThanOrEqualTo(caloriesBefore - 0.05f));
            });
        }

        [UnityTest]
        public IEnumerator StrongerHopImpulseTravelsFartherThanWeakImpulse()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            int y = SurfaceY(host);
            int origin = 28;

            FreezeWorld(host);
            yield return ResetWorld(host);
            host.Config.faunaHopImpulse = 5f;
            host.Config.faunaHopCost = 0f;
            host.Config.faunaWanderRate = 8f;
            host.Config.faunaDecisionInterval = 1;
            host.Config.faunaDryMass = 0.2f;
            StampSurface(host, origin, y);
            Paint(host, origin, y + 1, MaterialIds.Cricket);
            yield return Step(host, 24);
            int strongX = origin;
            yield return ReadWorldAndFauna(host, (materials, _, _, _, _) =>
            {
                int found = FindMaterialX(host, materials, MaterialIds.Cricket, y + 1);
                if (found < 0) found = FindMaterialX(host, materials, MaterialIds.Cricket, y + 2);
                strongX = found;
            });

            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
            host.Config.faunaHopImpulse = 0.15f;
            host.Config.faunaHopCost = 0f;
            host.Config.faunaWanderRate = 8f;
            host.Config.faunaDecisionInterval = 1;
            host.Config.faunaDryMass = 0.2f;
            StampSurface(host, origin, y);
            Paint(host, origin, y + 1, MaterialIds.Cricket);
            yield return Step(host, 24);
            int weakX = origin;
            yield return ReadWorldAndFauna(host, (materials, _, _, _, _) =>
            {
                int found = FindMaterialX(host, materials, MaterialIds.Cricket, y + 1);
                if (found < 0) found = FindMaterialX(host, materials, MaterialIds.Cricket, y + 2);
                weakX = found;
            });

            int width = host.Grid.angularResolution;
            int strongDelta = Mathf.Min(Mathf.Abs(strongX - origin), width - Mathf.Abs(strongX - origin));
            int weakDelta = Mathf.Min(Mathf.Abs(weakX - origin), width - Mathf.Abs(weakX - origin));
            Assert.That(strongDelta, Is.GreaterThanOrEqualTo(weakDelta));
            Assert.That(strongX, Is.GreaterThanOrEqualTo(0));
            Assert.That(weakX, Is.GreaterThanOrEqualTo(0));
        }

        [UnityTest]
        public IEnumerator AdultsLayTwoToFourEggsThatHatch()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            host.Config.faunaMaturityTicks = 1;
            host.Config.faunaReproductionCalorieThreshold = 0.05f;
            host.Config.faunaInitialCalories = 1f;
            host.Config.faunaClutchMin = 2;
            host.Config.faunaClutchMax = 4;
            host.Config.faunaHatchTicksMin = 10;
            host.Config.faunaHatchTicksMax = 10;
            host.Config.faunaReproduceCooldownTicks = 400;
            host.Config.faunaMaintenanceRate = 0f;
            host.Config.faunaHydrationDrain = 0f;
            host.Config.gravityStrength = 0f;
            int x = 36;
            int y = SurfaceY(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            yield return Step(host, 4);
            int eggs = 0;
            yield return ReadWorldAndFauna(host, (materials, _, _, genomes, _) =>
            {
                int width = host.Grid.angularResolution;
                for (int dx = -4; dx <= 4; dx++)
                {
                    int i = Index(host, x + dx, y + 1);
                    if (materials[i] == MaterialIds.CricketEgg)
                    {
                        eggs++;
                        Assert.That(FaunaGenome.Stage(genomes[i]), Is.EqualTo(FaunaGenome.StageEgg));
                    }
                }
                Assert.That(eggs, Is.InRange(2, 4), $"local eggs={eggs} width={width}");
            });
            host.Config.faunaMaturityTicks = 20000;
            host.Config.faunaReproductionCalorieThreshold = 1f;
            host.Config.faunaReproduceCooldownTicks = 8000;
            yield return Step(host, 16);
            yield return ReadWorldAndFauna(host, (materials, _, vitals, genomes, _) =>
            {
                int localCrickets = 0;
                int remainingEggs = 0;
                float age = 0f;
                float hatch = 0f;
                uint stage = 0;
                for (int dx = -4; dx <= 4; dx++)
                {
                    int i = Index(host, x + dx, y + 1);
                    if (materials[i] == MaterialIds.Cricket) localCrickets++;
                    if (materials[i] == MaterialIds.CricketEgg)
                    {
                        remainingEggs++;
                        age = Mathf.Max(age, vitals[i].z);
                        hatch = Mathf.Max(hatch, vitals[i].w);
                        stage = FaunaGenome.Stage(genomes[i]);
                    }
                }
                Assert.That(remainingEggs, Is.EqualTo(0), $"eggs={remainingEggs} age={age} hatch={hatch} stage={stage} crickets={localCrickets}");
                Assert.That(localCrickets, Is.GreaterThanOrEqualTo(3));
            });
        }

        [UnityTest]
        public IEnumerator EggsDieWhenTooDry()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            host.Config.faunaEggDesiccationMoisture = 0.95f;
            host.Config.faunaInitialHydration = 0.2f;
            host.Config.faunaHatchTicksMin = 5000;
            host.Config.faunaHatchTicksMax = 5000;
            int x = 44;
            int y = SurfaceY(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.CricketEgg);
            yield return Step(host, 8);
            yield return ReadWorldAndFauna(host, (materials, _, _, _, _) =>
            {
                Assert.That(materials[Index(host, x, y + 1)], Is.Not.EqualTo(MaterialIds.CricketEgg));
            });
        }

        [UnityTest]
        public IEnumerator ForagersEmitFeedingCallsAndFullCricketsStayQuiet()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            host.Config.faunaInitialCalories = 0.1f;
            host.Config.faunaHungerThreshold = 0.9f;
            host.Config.faunaFullThreshold = 0.95f;
            host.Config.faunaFeedCallAmplitude = 1f;
            host.Config.faunaAcousticDamping = 0.02f;
            int x = 52;
            int y = SurfaceY(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            yield return Step(host, 6);
            float hungryCall = 0f;
            yield return ReadWorldAndFauna(host, (_, _, _, _, acoustic) =>
            {
                hungryCall = Mathf.Abs(acoustic[Index(host, x, y + 1)].x);
            });

            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
            host.Config.faunaInitialCalories = 1f;
            host.Config.faunaHungerThreshold = 0.05f;
            host.Config.faunaFullThreshold = 0.2f;
            host.Config.faunaFeedCallAmplitude = 1f;
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            yield return Step(host, 6);
            float fullCall = 0f;
            yield return ReadWorldAndFauna(host, (_, _, _, _, acoustic) =>
            {
                fullCall = Mathf.Abs(acoustic[Index(host, x, y + 1)].x);
            });
            Assert.That(hungryCall, Is.GreaterThan(fullCall));
        }

        [UnityTest]
        public IEnumerator SnapshotRoundTripPreservesCricket()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            int x = 60;
            int y = SurfaceY(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            yield return Step(host, 3);
            uint stage = 0;
            float calories = 0f;
            yield return ReadWorldAndFauna(host, (materials, _, vitals, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                Assert.That(materials[i], Is.EqualTo(MaterialIds.Cricket));
                stage = FaunaGenome.Stage(genomes[i]);
                calories = vitals[i].x;
            });

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-fauna-test.snapshot");
            bool saved = false;
            new WorldSnapshotService().Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++)
                yield return null;
            Assert.That(saved, Is.True);

            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(new WorldSnapshotService().Load(host, path), Is.True);
            yield return null;
            yield return ReadWorldAndFauna(host, (materials, _, vitals, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                Assert.That(materials[i], Is.EqualTo(MaterialIds.Cricket));
                Assert.That(FaunaGenome.Stage(genomes[i]), Is.EqualTo(stage));
                Assert.That(vitals[i].x, Is.EqualTo(calories).Within(0.02f));
            });
        }

        [UnityTest]
        public IEnumerator FixedSeedFaunaIsDeterministicAndValidatorPasses()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            int x = 18;
            int y = SurfaceY(host);
            host.Config.seed = 7777;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            Paint(host, x + 2, y, MaterialIds.Algae);
            yield return Step(host, 10);
            Vector4 firstVitals = Vector4.zero;
            uint firstStage = 0;
            yield return ReadWorldAndFauna(host, (_, _, vitals, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                firstVitals = vitals[i];
                firstStage = FaunaGenome.Stage(genomes[i]);
            });

            host.Config.seed = 7777;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            Paint(host, x + 2, y, MaterialIds.Algae);
            yield return Step(host, 10);
            yield return ReadWorldAndFauna(host, (_, _, vitals, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                Assert.That(vitals[i].x, Is.EqualTo(firstVitals.x).Within(0.001f));
                Assert.That(vitals[i].y, Is.EqualTo(firstVitals.y).Within(0.001f));
                Assert.That(FaunaGenome.Stage(genomes[i]), Is.EqualTo(firstStage));
            });

            SimulationValidator validator = UnityEngine.Object.FindFirstObjectByType<SimulationValidator>();
            Assert.That(validator, Is.Not.Null);
            validator.ResetBaseline();
            bool validationDone = false;
            validator.ValidationCompleted += (_, __) => validationDone = true;
            validator.ValidateNow();
            for (int i = 0; i < 240 && !validationDone; i++)
                yield return null;
            Assert.That(validator.LastValidationPassed, Is.True, validator.LastMessage);
        }

        [UnityTest]
        public IEnumerator AdjacentAdultsRetainPartnerGenomes()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            host.Config.faunaMaintenanceRate = 0f;
            host.Config.faunaHydrationDrain = 0f;
            host.Config.faunaMateCooldownTicks = 0;
            int x = 72;
            int y = SurfaceY(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            Paint(host, x + 1, y + 1, MaterialIds.Cricket);
            yield return Step(host, 6);

            bool done = false;
            bool failed = false;
            bool leftHasPartner = false;
            bool rightHasPartner = false;
            AsyncGPUReadback.Request(host.Resources.FaunaRead, 0, 0, host.Resources.FaunaRead.width, 0, host.Resources.FaunaRead.height, 3, 1, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                var partners = request.GetData<Vector4>().ToArray();
                leftHasPartner = partners[Index(host, x, y + 1)].w != 0f;
                rightHasPartner = partners[Index(host, x + 1, y + 1)].w != 0f;
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(leftHasPartner || rightHasPartner, Is.True);
        }

        [UnityTest]
        public IEnumerator FireConvertsCricketToAsh()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            host.Config.combustionBurnRate = 1f;
            host.Config.combustionIgnitionAccumulationRate = 8f;
            host.Config.combustionSeedIntensity = 1f;
            int x = 80;
            int y = SurfaceY(host);
            StampSurface(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            yield return Step(host, 2);
            host.QueueFieldDeposit(new Vector2Int(host.Grid.WrapTheta(x), y + 1), 0, 1, 250f);
            host.QueueOxygen(new Vector2Int(host.Grid.WrapTheta(x), y + 1), 0, 1f);
            host.QueueIgnition(new Vector2Int(host.Grid.WrapTheta(x), y + 1), 0, 1f);
            yield return Step(host, 10);
            yield return ReadWorldAndFauna(host, (materials, _, _, _, _) =>
            {
                Assert.That(materials[Index(host, x, y + 1)], Is.Not.EqualTo(MaterialIds.Cricket));
            });
        }

        [UnityTest]
        public IEnumerator DenseAlgaeCricketEcosystemStaysFiniteAndClaimSafe()
        {
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            FreezeWorld(host);
            yield return ResetWorld(host);
            host.Config.faunaMaintenanceRate = 0.02f;
            host.Config.faunaHydrationDrain = 0.02f;
            host.Config.faunaHopImpulse = 0.6f;
            host.Config.faunaWanderRate = 0.15f;
            host.Config.faunaHopCost = 0.01f;
            host.Config.faunaReproduceCooldownTicks = 800;
            int y = SurfaceY(host);
            int planted = 0;
            int startX = 8;
            int endX = Mathf.Min(host.Grid.angularResolution - 8, 400);
            int queued = 0;
            for (int x = startX; x <= endX; x++)
            {
                Paint(host, x, y - 1, MaterialIds.Rock);
                Paint(host, x, y, MaterialIds.Soil);
                Paint(host, x, y + 1, MaterialIds.Air);
                Paint(host, x, y + 2, MaterialIds.Air);
                queued += 4;
                if (queued >= 100)
                {
                    yield return Step(host, 1);
                    queued = 0;
                }
            }
            for (int x = startX; x <= endX; x += 2)
            {
                Paint(host, x, y, MaterialIds.Algae);
                host.QueueFloraSeed(new Vector2Int(host.Grid.WrapTheta(x), y), 0, 0.7f);
                queued += 2;
                if (queued >= 100)
                {
                    yield return Step(host, 1);
                    queued = 0;
                }
            }
            for (int x = startX + 2; x <= endX - 2; x += 3)
            {
                Paint(host, x, y + 1, MaterialIds.Cricket);
                planted++;
                queued++;
                if (queued >= 100)
                {
                    yield return Step(host, 1);
                    queued = 0;
                }
            }
            yield return Step(host, 24);

            int crickets = 0;
            int eggs = 0;
            float maxAcoustic = 0f;
            yield return ReadWorldAndFauna(host, (materials, _, vitals, genomes, acoustic) =>
            {
                int living = 0;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == MaterialIds.Cricket)
                    {
                        crickets++;
                        living++;
                        Assert.That(float.IsFinite(vitals[i].x));
                        Assert.That(float.IsFinite(vitals[i].y));
                        Assert.That(FaunaGenome.IsValidStage(FaunaGenome.Stage(genomes[i])));
                    }
                    else if (materials[i] == MaterialIds.CricketEgg)
                    {
                        eggs++;
                        living++;
                    }
                }
                for (int i = 0; i < acoustic.Length; i++)
                {
                    Assert.That(float.IsFinite(acoustic[i].x));
                    Assert.That(float.IsFinite(acoustic[i].y));
                    maxAcoustic = Mathf.Max(maxAcoustic, Mathf.Abs(acoustic[i].x), Mathf.Abs(acoustic[i].y));
                }
                Assert.That(living, Is.LessThanOrEqualTo(planted * 5));
                Assert.That(crickets, Is.GreaterThan(0));
            });
            Assert.That(maxAcoustic, Is.LessThanOrEqualTo(1.5f));
            Assert.That(crickets + eggs, Is.GreaterThanOrEqualTo(Mathf.Max(8, planted / 5)));
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
