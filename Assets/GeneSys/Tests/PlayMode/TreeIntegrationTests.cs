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
    public sealed class TreeIntegrationTests
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

        private IEnumerator PrepareIsolatedWorld(SimulationHost host)
        {
            host.ApplyPreset(SimulationPreset.Validation);
            for (int i = 0; i < 60 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Config.seed = 2026;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.faunaSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
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

        private static int Index(SimulationHost host, int x, int y) =>
            y * host.Grid.angularResolution + host.Grid.WrapTheta(x);

        private static int SurfaceY(SimulationHost host) =>
            Mathf.Clamp(host.Grid.radialResolution - 12, 10, host.Grid.radialResolution - 6);

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

        private static void StampSurfacePlot(SimulationHost host, int x, int y)
        {
            Paint(host, x - 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x + 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x - 1, y, MaterialIds.Soil);
            Paint(host, x + 1, y, MaterialIds.Soil);
            for (int dy = 1; dy <= 8; dy++)
            {
                Paint(host, x, y + dy, MaterialIds.Air);
                Paint(host, x - 1, y + dy, MaterialIds.Air);
                Paint(host, x + 1, y + dy, MaterialIds.Air);
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
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.solarIntensity = 0.8f;
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
            host.Config.floraSeedAtWorldgen = false;
            host.Config.faunaSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
            host.Config.combustionIgnitionAccumulationRate = 0f;
            host.Config.stormChargeSeparationRate = 0f;
            host.Config.densityExchangeRate = 0f;
            host.Config.treeGeneExpressionRange = 0f;
            host.Config.treeGrowthCost = 0f;
            host.Config.treeDecayRate = 0f;
            host.Config.treeMaintenanceRate = 0f;
            host.Config.treeNightDrain = 0f;
            host.Config.treeInitialEnergy = 1f;
            host.Config.treeInitialHydration = 1f;
            host.Config.treeInitialNutrient = 1f;
            host.Config.treeInitialHealth = 1f;
            host.Config.treeGrowthTempMin = -50f;
            host.Config.treeGrowthTempMax = 80f;
            host.Config.treeGrowthMoistureMin = 0f;
            host.Config.treeGrowthMoistureMax = 2f;
            host.Config.treeSurvivalTempMin = -80f;
            host.Config.treeSurvivalTempMax = 120f;
            host.Config.treeSurvivalMoistureMin = 0f;
            host.Config.treeSurvivalMoistureMax = 2f;
            host.Config.treeMinLight = 0f;
            host.Config.treeSproutHeight = 3;
            host.Config.treeSaplingHeight = 6;
            host.Config.treeMaxHeight = 12;
            host.Config.treeLeafLifeTicks = 400;
            host.Config.treeRotTicks = 500;
            host.Config.treeExposureDamage = 0.08f;
            host.Config.detritusDecompositionRate = 0f;
            host.Config.detritusInitialNutrient = 0.8f;
            host.Config.validationIntervalTicks = 100000;
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static IEnumerator RequestTexture<T>(Texture tex, Action<T[]> consume) where T : struct
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(tex, 0);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            consume(request.GetData<T>().ToArray());
            yield return null;
        }

        private static IEnumerator RequestSlice(Texture tex, int slice, Action<Vector4[]> consume)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(tex, 0, 0, tex.width, 0, tex.height, slice, 1);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            consume(request.GetData<Vector4>().ToArray());
            yield return null;
        }

        private static IEnumerator ReadTreeCell(SimulationHost host, int x, int y,
            Action<uint, Vector4, TreeGenome.Packed, TreeGenome.Packed> consume)
        {
            int idx = Index(host, x, y);
            uint material = 0;
            Vector4 phys = Vector4.zero;
            var topology = default(TreeGenome.Packed);
            var genome = default(TreeGenome.Packed);
            yield return RequestTexture<uint>(host.Resources.MaterialRead, data => material = data[idx]);
            yield return RequestSlice(host.Resources.TreeRead, TreeGenome.PhysiologySlice, data => phys = data[idx]);
            yield return RequestSlice(host.Resources.TreeRead, TreeGenome.TopologySlice,
                data => topology = TreeGenome.FromFloatBits(data[idx]));
            yield return RequestSlice(host.Resources.TreeRead, TreeGenome.GenomeSlice,
                data => genome = TreeGenome.Sanitize(TreeGenome.FromFloatBits(data[idx])));
            consume(material, phys, topology, genome);
        }

        private static IEnumerator PlantSprout(SimulationHost host, int x, int y)
        {
            StampSurfacePlot(host, x, y);
            yield return Step(host, 1);
            host.QueueTreeSprout(new Vector2Int(host.Grid.WrapTheta(x), y), 0);
            yield return Step(host, 1);
        }

        [UnityTest]
        public IEnumerator SproutPlacementConvertsExposedSoilToWoodRoot()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 18;
            int y = SurfaceY(host);
            yield return PlantSprout(host, x, y);

            uint material = 0;
            uint stage = 0;
            uint role = 0;
            uint flags = 0;
            yield return ReadTreeCell(host, x, y, (mat, _, topology, genome) =>
            {
                material = mat;
                stage = TreeGenome.Stage(genome);
                role = TreeGenome.Role(topology.Z);
                flags = TreeGenome.Flags(topology.Z);
            });
            Assert.That(material, Is.EqualTo(MaterialIds.Wood));
            Assert.That(stage, Is.EqualTo(TreeGenome.StageSprout));
            Assert.That(role, Is.EqualTo(TreeGenome.RoleRoot));
            Assert.That((flags & TreeGenome.FlagAnchor) != 0, Is.True);
        }

        [UnityTest]
        public IEnumerator SproutGrowsUprightShootAndLateralLeaf()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.treeSproutHeight = 3;
            int x = 20;
            int y = SurfaceY(host);
            yield return PlantSprout(host, x, y);
            yield return Step(host, 10);

            int shoot = 0;
            int leaves = 0;
            for (int dy = 1; dy <= 5; dy++)
            {
                uint material = 0;
                uint role = 0;
                yield return ReadTreeCell(host, x, y + dy, (mat, _, topology, _) =>
                {
                    material = mat;
                    role = TreeGenome.Role(topology.Z);
                });
                if ((material == MaterialIds.Leaf || material == MaterialIds.Wood)
                    && (role == TreeGenome.RoleJuvenileShoot || role == TreeGenome.RoleTrunk || role == TreeGenome.RoleLeaf))
                    shoot++;
            }
            for (int dx = -1; dx <= 1; dx += 2)
            {
                for (int dy = 1; dy <= 5; dy++)
                {
                    uint material = 0;
                    uint role = 0;
                    yield return ReadTreeCell(host, x + dx, y + dy, (mat, _, topology, _) =>
                    {
                        material = mat;
                        role = TreeGenome.Role(topology.Z);
                    });
                    if (material == MaterialIds.Leaf && role == TreeGenome.RoleLeaf)
                        leaves++;
                }
            }
            Assert.That(shoot, Is.GreaterThanOrEqualTo(2), "Sprout should grow an upright shoot column.");
            Assert.That(leaves, Is.GreaterThanOrEqualTo(1), "Sprout should place a lateral L leaf.");
        }

        [UnityTest]
        public IEnumerator SaplingConvertsJuvenileShootToWood()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.treeSproutHeight = 2;
            host.Config.treeSaplingHeight = 4;
            int x = 22;
            int y = SurfaceY(host);
            yield return PlantSprout(host, x, y);
            yield return Step(host, 16);

            uint stage = 0;
            uint role = 0;
            uint material = 0;
            yield return ReadTreeCell(host, x, y + 1, (mat, _, topology, genome) =>
            {
                material = mat;
                role = TreeGenome.Role(topology.Z);
                stage = TreeGenome.Stage(genome);
            });
            Assert.That(stage, Is.GreaterThanOrEqualTo(TreeGenome.StageSapling));
            Assert.That(material, Is.EqualTo(MaterialIds.Wood));
            Assert.That(role, Is.EqualTo(TreeGenome.RoleTrunk).Or.EqualTo(TreeGenome.RoleRoot));
        }

        [UnityTest]
        public IEnumerator SharedGrassAndTreeUptakeDebitsSoilOnce()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.treeWaterUptakeRate = 0.8f;
            host.Config.grassWaterUptakeRate = 0.8f;
            int x = 24;
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(host.Grid.WrapTheta(x), y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(2f, 0.8f, 0f, 0f)
            });
            yield return Step(host, 1);
            float moistureBefore = 0f;
            yield return RequestTexture<Vector4>(host.Resources.StateRead, data => moistureBefore = data[Index(host, x, y)].z);
            host.QueueTreeSprout(new Vector2Int(host.Grid.WrapTheta(x), y), 0);
            host.QueueGrassSeed(new Vector2Int(host.Grid.WrapTheta(x + 1), y), 0);
            yield return Step(host, 4);
            float moistureAfter = 0f;
            yield return RequestTexture<Vector4>(host.Resources.StateRead, data => moistureAfter = data[Index(host, x, y)].z);
            Assert.That(moistureAfter, Is.LessThanOrEqualTo(moistureBefore + 0.001f));
            Assert.That(moistureAfter, Is.GreaterThanOrEqualTo(-0.001f));
        }

        [UnityTest]
        public IEnumerator CricketConsumesLeafAndLeavesWood()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.treeSproutHeight = 3;
            host.Config.faunaMaintenanceRate = 0f;
            host.Config.faunaHydrationDrain = 0f;
            host.Config.faunaHungerThreshold = 0.99f;
            host.Config.faunaFullThreshold = 1f;
            host.Config.faunaFeedCost = 0f;
            host.Config.faunaDecisionInterval = 1;
            host.Config.faunaHopImpulse = 0f;
            host.Config.faunaWanderRate = 0f;
            int x = 26;
            int y = SurfaceY(host);
            yield return PlantSprout(host, x, y);
            yield return Step(host, 12);

            int leafX = x;
            int leafY = y + 1;
            bool foundLeaf = false;
            for (int dx = -1; dx <= 1 && !foundLeaf; dx++)
            {
                for (int dy = 1; dy <= 5 && !foundLeaf; dy++)
                {
                    uint material = 0;
                    yield return ReadTreeCell(host, x + dx, y + dy, (mat, _, __, ___) => material = mat);
                    if (material == MaterialIds.Leaf)
                    {
                        leafX = x + dx;
                        leafY = y + dy;
                        foundLeaf = true;
                    }
                }
            }
            Assert.That(foundLeaf, Is.True, "Sprout should grow at least one leaf or juvenile shoot.");
            int side = leafX >= x ? 1 : -1;
            int cricketX = leafX + (leafX == x ? side : 0);
            if (cricketX == leafX)
                cricketX = leafX + side;
            Paint(host, cricketX, leafY - 1, MaterialIds.Rock);
            Paint(host, cricketX, leafY, MaterialIds.Cricket);
            yield return Step(host, 8);
            uint after = MaterialIds.Leaf;
            yield return RequestTexture<uint>(host.Resources.MaterialRead, data => after = data[Index(host, leafX, leafY)]);
            Assert.That(after, Is.Not.EqualTo(MaterialIds.Leaf));
            uint wood = 0;
            yield return ReadTreeCell(host, x, y, (mat, _, __, ___) => wood = mat);
            Assert.That(wood, Is.EqualTo(MaterialIds.Wood));
        }

        [UnityTest]
        public IEnumerator ShortLivedLeafBecomesDetritus()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.treeSproutHeight = 3;
            host.Config.treeLeafLifeTicks = 8;
            host.Config.detritusDecompositionRate = 0f;
            int x = 28;
            int y = SurfaceY(host);
            yield return PlantSprout(host, x, y);
            yield return Step(host, 12);
            bool hadLeaf = false;
            int leafX = x;
            int leafY = y + 1;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = 1; dy <= 5; dy++)
                {
                    uint material = 0;
                    uint role = 0;
                    yield return ReadTreeCell(host, x + dx, y + dy, (mat, _, topology, __) =>
                    {
                        material = mat;
                        role = TreeGenome.Role(topology.Z);
                    });
                    if (material == MaterialIds.Leaf && role == TreeGenome.RoleLeaf)
                    {
                        hadLeaf = true;
                        leafX = x + dx;
                        leafY = y + dy;
                    }
                }
            }
            Assert.That(hadLeaf, Is.True, "Sprout should place a lateral leaf before it expires.");
            host.Config.treeGrowthRate = 0f;
            uint originFirstNonLeaf = MaterialIds.Leaf;
            uint belowAtTransition = 0;
            int transitionTick = -1;
            for (int tick = 0; tick < 16 && originFirstNonLeaf == MaterialIds.Leaf; tick++)
            {
                yield return Step(host, 1);
                uint origin = MaterialIds.Leaf;
                uint below = 0;
                yield return RequestTexture<uint>(host.Resources.MaterialRead, data =>
                {
                    origin = data[Index(host, leafX, leafY)];
                    below = leafY > 0 ? data[Index(host, leafX, leafY - 1)] : 0u;
                });
                if (origin != MaterialIds.Leaf)
                {
                    originFirstNonLeaf = origin;
                    belowAtTransition = below;
                    transitionTick = tick;
                }
            }
            yield return Step(host, 24);
            int detritusCount = 0;
            int remainingLeaf = 0;
            uint originAfterFall = 0;
            yield return RequestTexture<uint>(host.Resources.MaterialRead, data =>
            {
                originAfterFall = data[Index(host, leafX, leafY)];
                int minY = 0;
                int maxY = Mathf.Min(host.Grid.radialResolution - 1, leafY + 2);
                for (int y = minY; y <= maxY; y++)
                {
                    for (int dx = -32; dx <= 32; dx++)
                    {
                        uint material = data[Index(host, leafX + dx, y)];
                        if (material == MaterialIds.Detritus) detritusCount++;
                        if (material == MaterialIds.Leaf) remainingLeaf++;
                    }
                }
            });
            Assert.That(originFirstNonLeaf, Is.Not.EqualTo(MaterialIds.Leaf),
                "Expired leaves should leave the Leaf material.");
            Assert.That(detritusCount, Is.GreaterThan(0),
                $"Expired leaves should become Detritus, not vanish. originFirstNonLeaf={originFirstNonLeaf} belowAtTransition={belowAtTransition} transitionTick={transitionTick} originAfterFall={originAfterFall} remainingLeaf={remainingLeaf} detritusCount={detritusCount}");
        }

        [UnityTest]
        public IEnumerator ExposedRootLosesHealth()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.treeExposureDamage = 0.8f;
            int x = 30;
            int y = SurfaceY(host);
            yield return PlantSprout(host, x, y);
            float healthBefore = 1f;
            yield return ReadTreeCell(host, x, y, (_, phys, __, ___) => healthBefore = phys.w);
            yield return Step(host, 8);
            float healthAfter = 1f;
            yield return ReadTreeCell(host, x, y, (_, phys, __, ___) => healthAfter = phys.w);
            Assert.That(healthAfter, Is.LessThan(healthBefore - 0.01f));
        }

        [UnityTest]
        public IEnumerator AttachedWoodDoesNotFallWhenGravityIsOn()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.treeSproutHeight = 2;
            int x = 32;
            int y = SurfaceY(host);
            yield return PlantSprout(host, x, y);
            yield return Step(host, 6);
            host.Config.gravityStrength = 2f;
            yield return Step(host, 4);
            uint material = 0;
            yield return ReadTreeCell(host, x, y, (mat, _, __, ___) => material = mat);
            Assert.That(material, Is.EqualTo(MaterialIds.Wood));
        }

        [UnityTest]
        public IEnumerator SnapshotV12RoundTripsAndV11ClearsTree()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 34;
            int y = SurfaceY(host);
            yield return PlantSprout(host, x, y);
            uint lineage = 0;
            yield return ReadTreeCell(host, x, y, (_, __, ___, genome) => lineage = TreeGenome.Lineage(genome));
            Assert.That(lineage, Is.GreaterThan(0u));

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-tree-test.snapshot");
            bool saved = false;
            new WorldSnapshotService().Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++)
                yield return null;
            Assert.That(saved, Is.True);

            host.Resources.ClearTree();
            yield return Step(host, 1);
            Assert.That(new WorldSnapshotService().Load(host, path), Is.True);
            yield return ReadTreeCell(host, x, y, (mat, _, topology, genome) =>
            {
                Assert.That(mat, Is.EqualTo(MaterialIds.Wood));
                Assert.That(topology.X, Is.Not.EqualTo(0u));
                Assert.That(TreeGenome.Lineage(genome), Is.EqualTo(lineage));
            });

            string legacy = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-tree-v11.snapshot");
            saved = false;
            new WorldSnapshotService().Save(host, legacy, 11, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++)
                yield return null;
            Assert.That(saved, Is.True);
            Assert.That(new WorldSnapshotService().Load(host, legacy), Is.True);
            yield return ReadTreeCell(host, x, y, (_, __, topology, genome) =>
            {
                Assert.That(topology.X, Is.EqualTo(0u), "Loading a pre-tree snapshot should clear tree state.");
                Assert.That(TreeGenome.Stage(genome), Is.EqualTo(TreeGenome.StageEmpty));
            });
        }

        [UnityTest]
        public IEnumerator SameSeedProducesTheSameSproutGenome()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 36;
            int y = SurfaceY(host);
            host.Config.seed = 77;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
            yield return PlantSprout(host, x, y);
            var first = default(TreeGenome.Packed);
            yield return ReadTreeCell(host, x, y, (_, __, ___, genome) => first = genome);

            host.Config.seed = 77;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
            yield return PlantSprout(host, x, y);
            var second = default(TreeGenome.Packed);
            yield return ReadTreeCell(host, x, y, (_, __, ___, genome) => second = genome);
            Assert.That(first.X, Is.EqualTo(second.X));
            Assert.That(first.Y, Is.EqualTo(second.Y));
            Assert.That(first.Z, Is.EqualTo(second.Z));
            Assert.That(TreeGenome.Lineage(first), Is.EqualTo(TreeGenome.Lineage(second)));
        }

        [UnityTest]
        public IEnumerator ValidatorAcceptsAHealthySprout()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 38;
            int y = SurfaceY(host);
            yield return PlantSprout(host, x, y);
            var validator = host.GetComponent<GeneSys.Validation.SimulationValidator>();
            Assert.That(validator, Is.Not.Null);
            bool done = false;
            validator.ValidationCompleted += (_, __) => done = true;
            validator.ValidateNow();
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(done, Is.True, validator.LastMessage);
            Assert.That(validator.LastValidationPassed, Is.True, validator.LastMessage);
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
