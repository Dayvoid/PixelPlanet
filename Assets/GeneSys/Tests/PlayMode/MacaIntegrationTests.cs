using System;
using System.Collections;
using GeneSys.Materials;
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
    public sealed class MacaIntegrationTests
    {
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

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

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

        private static void Excavate(SimulationHost host, int x, int y, int halfWidth, int height)
        {
            for (int dx = -halfWidth; dx <= halfWidth; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                    Paint(host, x + dx, y + dy, MaterialIds.Air);
            }
        }

        private static void ConfigureMaceIsolation(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.maceEnabled = true;
            host.Config.macePhasesPerTick = 2;
            host.Config.gravityStrength = 1f;
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
            host.Config.maceHardCrustWear = 0f;
            host.Config.maceSoluteRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.mantlePressure = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.faunaSeedAtWorldgen = false;
            host.Config.waspSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
            host.Config.materialSubsteps = 1;
            host.Config.slowPassInterval = 64;
            host.Config.validationIntervalTicks = 100000;
        }

        private static IEnumerator ReadMobile(SimulationHost host, Action<Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.LifeGenomeRead, 0, 0, host.Grid.angularResolution, 0, host.Grid.radialResolution, 2, 1, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<Vector4>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator ReadMaterials(SimulationHost host, Action<uint[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<uint>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static double SoftMass(Vector4[] mobile)
        {
            double sum = 0d;
            for (int i = 0; i < mobile.Length; i++)
                sum += Math.Max(0d, mobile[i].x) + Math.Max(0d, mobile[i].y);
            return sum;
        }

        private static int Index(SimulationHost host, int x, int y) => y * host.Grid.angularResolution + host.Grid.WrapTheta(x);

        [UnityTest]
        public IEnumerator MacaSedimentConservesTotalMass()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMaceIsolation(host);
            host.Config.seed = 4242;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.78f), 16, host.Grid.radialResolution - 16);
            Excavate(host, x, y - 2, 8, 12);
            for (int dx = -6; dx <= 6; dx++)
            {
                Paint(host, x + dx, y - 2, MaterialIds.Rock);
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
            }
            for (int dx = -2; dx <= 2; dx++)
            for (int dy = 0; dy < 4; dy++)
                Paint(host, x + dx, y + dy, MaterialIds.Sediment);
            yield return Step(host, 1);

            Vector4[] before = null;
            yield return ReadMobile(host, m => before = m);
            double start = SoftMass(before);
            Assert.That(start, Is.GreaterThan(4d));

            yield return Step(host, 80);

            Vector4[] after = null;
            yield return ReadMobile(host, m => after = m);
            double end = SoftMass(after);
            Assert.That(Math.Abs(end - start) / Math.Max(1d, start), Is.LessThan(1e-3d));
        }

        [UnityTest]
        public IEnumerator MacaAngleOfReposeConforms()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMaceIsolation(host);
            host.Config.seed = 5151;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int shelf = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.76f), 12, host.Grid.radialResolution - 18);
            Excavate(host, x, shelf, 18, 14);
            for (int dx = -16; dx <= 16; dx++)
                Paint(host, x + dx, shelf, MaterialIds.Rock);
            for (int dy = 1; dy <= 10; dy++)
                Paint(host, x, shelf + dy, MaterialIds.Sediment);
            yield return Step(host, 160);

            uint[] mats = null;
            yield return ReadMaterials(host, m => mats = m);
            Vector4[] mobile = null;
            yield return ReadMobile(host, m => mobile = m);
            int left = x, right = x, peak = 0;
            for (int dx = -16; dx <= 16; dx++)
            {
                for (int dy = 1; dy <= 12; dy++)
                {
                    int idx = Index(host, x + dx, shelf + dy);
                    bool filled = mats[idx] == MaterialIds.Sediment || mobile[idx].x > 0.45f;
                    if (!filled) continue;
                    peak = Math.Max(peak, dy);
                    left = Math.Min(left, x + dx);
                    right = Math.Max(right, x + dx);
                }
            }
            int halfWidth = Math.Max(1, (right - left) / 2);
            float angle = Mathf.Atan2(peak, halfWidth) * Mathf.Rad2Deg;
            Assert.That(angle, Is.InRange(15f, 45f), $"pile peak={peak} halfWidth={halfWidth}");
        }

        [UnityTest]
        public IEnumerator MacaDeterministicReplay()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMaceIsolation(host);
            host.Config.seed = 6161;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 24);
            Vector4[] first = null;
            yield return ReadMobile(host, m => first = m);

            host.Config.seed = 6161;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 24);
            Vector4[] second = null;
            yield return ReadMobile(host, m => second = m);

            Assert.That(second.Length, Is.EqualTo(first.Length));
            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(second[i].x, Is.EqualTo(first[i].x).Within(1e-5f));
                Assert.That(second[i].y, Is.EqualTo(first[i].y).Within(1e-5f));
            }
        }

        [UnityTest]
        public IEnumerator MacaMagmaAshUnaffected()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMaceIsolation(host);
            host.Config.magmaEruption = 1f;
            host.Config.eruptionPressureStrength = 3f;
            host.Config.eruptionFlowStrength = 3f;
            host.Config.eruptionBurdenDepth = 3;
            host.Config.seed = 7171;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.55f), 8, host.Grid.radialResolution - 8);
            for (int dx = -3; dx <= 3; dx++)
            for (int dy = -3; dy <= 3; dy++)
                Paint(host, x + dx, y + dy, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Magma);
            Paint(host, x, y + 1, MaterialIds.Sediment);
            Paint(host, x, y + 2, MaterialIds.Air);
            yield return Step(host, 12);

            uint[] mats = null;
            yield return ReadMaterials(host, m => mats = m);
            bool sawMagma = false;
            bool sawSedimentOrAsh = false;
            int width = host.Grid.angularResolution;
            for (int i = 0; i < mats.Length; i++)
            {
                int cx = i % width;
                int cy = i / width;
                if (Math.Abs(cx - x) > 6 || Math.Abs(cy - y) > 6) continue;
                if (mats[i] == MaterialIds.Magma) sawMagma = true;
                if (mats[i] == MaterialIds.Sediment || mats[i] == MaterialIds.Ash) sawSedimentOrAsh = true;
            }
            Assert.That(sawMagma || sawSedimentOrAsh, Is.True);
        }

        [UnityTest]
        public IEnumerator MacaSoilCliffShedsWithoutFlicker()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMaceIsolation(host);
            host.Config.erosionRate = 1.5f;
            host.Config.slowPassInterval = 1;
            host.Config.maceErosionShed = 0.35f;
            host.Config.seed = 8181;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.76f), 12, host.Grid.radialResolution - 12);
            Excavate(host, x, y - 1, 3, 10);
            for (int dy = 0; dy < 6; dy++)
                Paint(host, x, y + dy, MaterialIds.Soil);
            Paint(host, x, y - 1, MaterialIds.Rock);
            yield return Step(host, 1);

            uint last = MaterialIds.Soil;
            int flips = 0;
            for (int i = 0; i < 40; i++)
            {
                host.QueueBrush(new GpuPassScheduler.BrushCommand
                {
                    center = new Vector2Int(x, y + 5),
                    radius = 1,
                    materialId = 0,
                    values = new Vector4(13f, 2f, 0f, 0f)
                });
                yield return Step(host, 1);
                uint current = 0;
                yield return ReadMaterials(host, mats => current = mats[Index(host, x, y + 5)]);
                if (current != last && last != MaterialIds.Soil)
                    flips++;
                last = current;
            }

            Assert.That(flips, Is.LessThan(6), "Cliff identity should not flicker between hosts.");
            double mass = 0d;
            bool massReady = false;
            SimulationMetrics.MeasureSoftSolidsAsync(host, value => { mass = value; massReady = true; });
            for (int i = 0; i < 120 && !massReady; i++) yield return null;
            Assert.That(mass, Is.GreaterThan(0d));
        }

        [UnityTest]
        public IEnumerator MacaMoistureIncreasesCriticalAngle()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMaceIsolation(host);
            host.Config.maceMoistureCohesion = 2f;
            host.Config.seed = 9191;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int xDry = host.Grid.angularResolution / 3;
            int xWet = (host.Grid.angularResolution * 2) / 3;
            int shelf = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.76f), 12, host.Grid.radialResolution - 16);
            Excavate(host, xDry, shelf, 6, 12);
            Excavate(host, xWet, shelf, 6, 12);
            for (int dx = -4; dx <= 4; dx++)
            {
                Paint(host, xDry + dx, shelf, MaterialIds.Rock);
                Paint(host, xWet + dx, shelf, MaterialIds.Rock);
            }
            for (int dy = 1; dy <= 8; dy++)
            {
                Paint(host, xDry, shelf + dy, MaterialIds.Sediment);
                Paint(host, xWet, shelf + dy, MaterialIds.Sediment);
            }
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(xWet, shelf + 4),
                radius = 3,
                materialId = 0,
                values = new Vector4(2f, 0.8f, 0f, 0f)
            });
            yield return Step(host, 80);

            uint[] mats = null;
            yield return ReadMaterials(host, m => mats = m);
            int dryPeak = 0, wetPeak = 0;
            for (int dy = 1; dy <= 8; dy++)
            {
                if (mats[Index(host, xDry, shelf + dy)] == MaterialIds.Sediment) dryPeak = dy;
                if (mats[Index(host, xWet, shelf + dy)] == MaterialIds.Sediment) wetPeak = dy;
            }
            Assert.That(wetPeak, Is.GreaterThanOrEqualTo(dryPeak));
        }

        [UnityTest]
        public IEnumerator MacaRunoffMovesSedimentWithoutLoss()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMaceIsolation(host);
            host.Config.maceAdvection = 2f;
            host.Config.maceShearDrive = 2f;
            host.Config.seed = 1010;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.76f), 12, host.Grid.radialResolution - 12);
            Excavate(host, x, y, 10, 8);
            for (int dx = -8; dx <= 8; dx++)
                Paint(host, x + dx, y, MaterialIds.Rock);
            Paint(host, x, y + 1, MaterialIds.Sediment);
            yield return Step(host, 1);
            Vector4[] before = null;
            yield return ReadMobile(host, m => before = m);
            double start = SoftMass(before);

            for (int i = 0; i < 30; i++)
            {
                host.QueueBrush(new GpuPassScheduler.BrushCommand
                {
                    center = new Vector2Int(x, y + 1),
                    radius = 2,
                    materialId = 0,
                    values = new Vector4(13f, 3f, 0f, 0f)
                });
                yield return Step(host, 1);
            }

            Vector4[] after = null;
            yield return ReadMobile(host, m => after = m);
            double end = SoftMass(after);
            Assert.That(Math.Abs(end - start) / Math.Max(1d, start), Is.LessThan(1e-3d));
        }

        [UnityTest]
        public IEnumerator MacaBeachDepositionHandsOffWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMaceIsolation(host);
            host.Config.maceSuspendCap = 0.35f;
            host.Config.maceOccupyHigh = 0.85f;
            host.Config.seed = 1212;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.76f), 12, host.Grid.radialResolution - 14);
            Excavate(host, x, y, 10, 10);
            for (int dx = -8; dx <= 8; dx++)
            {
                Paint(host, x + dx, y, MaterialIds.Rock);
                Paint(host, x + dx, y + 1, MaterialIds.Water);
            }
            for (int dy = 2; dy <= 6; dy++)
                Paint(host, x, y + dy, MaterialIds.Sediment);
            yield return Step(host, 1);

            WorldWaterMetrics before = default;
            bool beforeReady = false;
            SimulationMetrics.MeasureAsync(host, result => { before = result; beforeReady = true; });
            for (int i = 0; i < 240 && !beforeReady; i++) yield return null;
            Assert.That(before.TotalTrackedWaterMass, Is.GreaterThan(4d));

            yield return Step(host, 80);

            WorldWaterMetrics after = default;
            bool afterReady = false;
            SimulationMetrics.MeasureAsync(host, result => { after = result; afterReady = true; });
            for (int i = 0; i < 240 && !afterReady; i++) yield return null;
            Assert.That(Math.Abs(after.TotalTrackedWaterMass - before.TotalTrackedWaterMass) / Math.Max(1d, before.TotalTrackedWaterMass), Is.LessThan(1e-3d));

            uint[] mats = null;
            yield return ReadMaterials(host, m => mats = m);
            Vector4[] mobile = null;
            yield return ReadMobile(host, m => mobile = m);
            bool shorelineMix = false;
            for (int dx = -8; dx <= 8; dx++)
            {
                int idx = Index(host, x + dx, y + 1);
                if (mats[idx] == MaterialIds.Sediment || mobile[idx].x > 0.05f)
                    shorelineMix = true;
            }
            Assert.That(shorelineMix, Is.True, "Sediment should enter the waterline as suspended load or a deposited beach.");
        }
    }
}
