using System;
using System.Collections;
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
    public sealed class MargolusDensitySortTests
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

        private static int Index(SimulationHost host, int x, int y) => y * host.Grid.angularResolution + x;

        private static void Paint(SimulationHost host, int x, int y, uint materialId)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = materialId,
                values = Vector4.zero
            });
            if (materialId == MaterialIds.Water)
            {
                host.QueueBrush(new GpuPassScheduler.BrushCommand
                {
                    center = new Vector2Int(x, y),
                    radius = 0,
                    materialId = MaterialIds.Void,
                    values = new Vector4(2f, 1f, 0f, 0f)
                });
            }
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static void ConfigureMargolusDensitySort(SimulationHost host)
        {
            host.Clock.SetRunning(false);
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
            host.Config.ashSettlingStrength = 0f;
            host.Config.ashUpdraftStrength = 0f;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.phaseHysteresis = 50f;
            host.Config.materialSubsteps = 1;
            host.Config.slowPassInterval = 64;
            host.Config.enableMaterialTransport = true;
            host.Config.margolusSubsteps = 1;
            host.Config.margolusMetricEnable = true;
            host.Config.gravityStrength = 1f;
            host.Config.grassRootCohesionBonus = 0f;
            host.Config.treeRootCohesionBonus = 0f;
            host.Config.grassWaterUptakeRate = 0f;
            host.Config.treeWaterUptakeRate = 0f;
            host.RefreshMaterialDefinitions();
        }

        private static void FillColumn(SimulationHost host, int x, int y0, int y1, uint materialId)
        {
            for (int y = y0; y <= y1; y++)
                Paint(host, x, y, materialId);
        }

        private static void SealColumn(SimulationHost host, int x, int floorY, int topY)
        {
            for (int y = floorY - 1; y <= topY + 1; y++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    bool interior = dx == 0 && y >= floorY && y <= topY;
                    if (interior) continue;
                    Paint(host, x + dx, y, MaterialIds.Core);
                }
            }
        }

        private static int CrustX(SimulationHost host, int slot) =>
            Mathf.Clamp(host.Grid.angularResolution / 2 + slot * 12, 2, host.Grid.angularResolution - 3);

        private static int CrustFloorY(SimulationHost host) =>
            Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.82f), 8, host.Grid.radialResolution - 12);

        private static IEnumerator PrepareColumn(SimulationHost host, int x, int floorY, int topY, Action paintInterior)
        {
            SealColumn(host, x, floorY, topY);
            yield return Step(host, 1);
            paintInterior();
            yield return Step(host, 1);
        }

        [UnityTest]
        public IEnumerator DenseSoilSettlesThroughWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusDensitySort(host);

            int x = CrustX(host, 1);
            int floorY = CrustFloorY(host);
            yield return PrepareColumn(host, x, floorY, floorY + 5, () =>
            {
                FillColumn(host, x, floorY, floorY + 4, MaterialIds.Water);
                Paint(host, x, floorY + 5, MaterialIds.Soil);
            });

            yield return Step(host, 24);

            uint[] after = null;
            yield return ReadMaterials(host, mats => after = mats);
            Assert.That(after[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Soil));
            Assert.That(after[Index(host, x, floorY + 5)], Is.EqualTo(MaterialIds.Water));
        }

        [UnityTest]
        public IEnumerator CoreRemainsAnchoredAboveWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusDensitySort(host);

            int x = CrustX(host, 3);
            int floorY = CrustFloorY(host);
            yield return PrepareColumn(host, x, floorY, floorY + 4, () =>
            {
                FillColumn(host, x, floorY, floorY + 3, MaterialIds.Water);
                Paint(host, x, floorY + 4, MaterialIds.Core);
            });

            uint[] before = null;
            yield return ReadMaterials(host, mats => before = mats);
            Assert.That(before[Index(host, x, floorY + 4)], Is.EqualTo(MaterialIds.Core));
            Assert.That(before[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Water));

            yield return Step(host, 16);

            uint[] after = null;
            yield return ReadMaterials(host, mats => after = mats);
            Assert.That(after[Index(host, x, floorY + 4)], Is.EqualTo(MaterialIds.Core));
            Assert.That(after[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Water));
        }

        [UnityTest]
        public IEnumerator EqualDensityWaterColumnStaysStable()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusDensitySort(host);

            int x = CrustX(host, 4);
            int floorY = CrustFloorY(host);
            yield return PrepareColumn(host, x, floorY, floorY + 5, () =>
            {
                FillColumn(host, x, floorY, floorY + 5, MaterialIds.Water);
            });

            uint[] beforeMats = null;
            yield return ReadMaterials(host, mats => beforeMats = mats);
            for (int y = floorY; y <= floorY + 5; y++)
                Assert.That(beforeMats[Index(host, x, y)], Is.EqualTo(MaterialIds.Water));

            yield return Step(host, 12);

            uint[] afterMats = null;
            yield return ReadMaterials(host, mats => afterMats = mats);
            for (int y = floorY; y <= floorY + 5; y++)
                Assert.That(afterMats[Index(host, x, y)], Is.EqualTo(MaterialIds.Water));
        }
    }
}
