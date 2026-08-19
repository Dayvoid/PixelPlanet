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
    public sealed class DensityDisplacementIntegrationTests
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

        private static IEnumerator ReadMaterialsAndState(SimulationHost host, Action<uint[], Vector4[]> consume)
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
                    consume(materials, stateRequest.GetData<Vector4>().ToArray());
                    done = true;
                });
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
        }

        private static void PaintHeat(SimulationHost host, int x, int y, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(1f, amount, 0f, 0f)
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

        private static void ConfigureDensityIsolation(SimulationHost host)
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
            host.Config.humidityBuoyancy = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.radiativeCooling = 0f;
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.ashSettlingStrength = 0f;
            host.Config.ashUpdraftStrength = 0f;
            host.Config.mantlePressure = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.volcanicCooling = 0f;
            host.Config.densityExchangeRate = 64f;
            host.Config.densityExchangeEpsilon = 0.02f;
            host.Config.phaseHysteresis = 50f;
            host.Config.materialSubsteps = 1;
            host.Config.slowPassInterval = 64;
            host.RefreshMaterialDefinitions();
        }

        private static void FillColumn(SimulationHost host, int x, int y0, int y1, uint materialId)
        {
            for (int y = y0; y <= y1; y++)
                Paint(host, x, y, materialId);
        }

        private static void SealColumn(SimulationHost host, int x, int floorY, int topY)
        {
            // Paint a solid Core box so liquids/granulars cannot diagonally drain into open air.
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

        // Place fixtures in the cool near-surface band so painted cells do not inherit mantle heat.
        private static int CrustFloorY(SimulationHost host) =>
            Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.82f), 8, host.Grid.radialResolution - 12);

        private static IEnumerator PrepareColumn(
            SimulationHost host, int x, int floorY, int topY, float targetTemperature,
            Action paintInterior)
        {
            float previousRate = host.Config.densityExchangeRate;
            host.Config.densityExchangeRate = 0f;

            SealColumn(host, x, floorY, topY);
            yield return Step(host, 1);

            Vector4[] states = null;
            yield return ReadMaterialsAndState(host, (_, s) => states = s);
            paintInterior();
            for (int y = floorY; y <= topY; y++)
                PaintHeat(host, x, y, targetTemperature - states[Index(host, x, y)].x);
            yield return Step(host, 1);

            host.Config.densityExchangeRate = previousRate;
        }

        [UnityTest]
        public IEnumerator DenseRockSettlesThroughWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureDensityIsolation(host);

            int x = CrustX(host, 0);
            int floorY = CrustFloorY(host);
            yield return PrepareColumn(host, x, floorY, floorY + 6, 15f, () =>
            {
                FillColumn(host, x, floorY, floorY + 5, MaterialIds.Water);
                Paint(host, x, floorY + 6, MaterialIds.Rock);
            });

            uint[] before = null;
            yield return ReadMaterials(host, mats => before = mats);
            Assert.That(before[Index(host, x, floorY + 6)], Is.EqualTo(MaterialIds.Rock));
            Assert.That(before[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Water));

            yield return Step(host, 24);

            uint[] after = null;
            yield return ReadMaterials(host, mats => after = mats);
            Assert.That(after[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Rock), "Rock should settle to the bottom of the water column.");
            Assert.That(after[Index(host, x, floorY + 6)], Is.EqualTo(MaterialIds.Water), "Displaced water should rise.");
            int beforeLocalRock = 0, afterLocalRock = 0, beforeLocalWater = 0, afterLocalWater = 0;
            for (int y = floorY; y <= floorY + 6; y++)
            {
                if (before[Index(host, x, y)] == MaterialIds.Rock) beforeLocalRock++;
                if (after[Index(host, x, y)] == MaterialIds.Rock) afterLocalRock++;
                if (before[Index(host, x, y)] == MaterialIds.Water) beforeLocalWater++;
                if (after[Index(host, x, y)] == MaterialIds.Water) afterLocalWater++;
            }
            Assert.That(afterLocalRock, Is.EqualTo(beforeLocalRock));
            Assert.That(afterLocalWater, Is.EqualTo(beforeLocalWater));
        }

        [UnityTest]
        public IEnumerator DenseSoilSettlesThroughWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureDensityIsolation(host);

            int x = CrustX(host, 1);
            int floorY = CrustFloorY(host);
            yield return PrepareColumn(host, x, floorY, floorY + 5, 15f, () =>
            {
                FillColumn(host, x, floorY, floorY + 4, MaterialIds.Water);
                Paint(host, x, floorY + 5, MaterialIds.Soil);
            });

            uint[] before = null;
            yield return ReadMaterials(host, mats => before = mats);
            Assert.That(before[Index(host, x, floorY + 5)], Is.EqualTo(MaterialIds.Soil));
            Assert.That(before[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Water));

            yield return Step(host, 24);

            uint[] after = null;
            yield return ReadMaterials(host, mats => after = mats);
            Assert.That(after[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Soil));
            Assert.That(after[Index(host, x, floorY + 5)], Is.EqualTo(MaterialIds.Water));
        }

        [UnityTest]
        public IEnumerator IceRisesThroughWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureDensityIsolation(host);

            int x = CrustX(host, 2);
            int floorY = CrustFloorY(host);
            // WaterCycle keeps Ice for T<=1 and Water for T>=0, so 0.5 keeps both phases stable.
            yield return PrepareColumn(host, x, floorY, floorY + 5, 0.5f, () =>
            {
                Paint(host, x, floorY, MaterialIds.Ice);
                FillColumn(host, x, floorY + 1, floorY + 5, MaterialIds.Water);
            });

            uint[] before = null;
            yield return ReadMaterials(host, mats => before = mats);
            Assert.That(before[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Ice));
            Assert.That(before[Index(host, x, floorY + 5)], Is.EqualTo(MaterialIds.Water));

            yield return Step(host, 24);

            uint[] after = null;
            yield return ReadMaterials(host, mats => after = mats);
            Assert.That(after[Index(host, x, floorY + 5)], Is.EqualTo(MaterialIds.Ice), "Ice should float to the top of the water column.");
            Assert.That(after[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Water));
        }

        [UnityTest]
        public IEnumerator CoreRemainsAnchoredAboveWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureDensityIsolation(host);

            int x = CrustX(host, 3);
            int floorY = CrustFloorY(host);
            yield return PrepareColumn(host, x, floorY, floorY + 4, 15f, () =>
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
            ConfigureDensityIsolation(host);

            int x = CrustX(host, 4);
            int floorY = CrustFloorY(host);
            yield return PrepareColumn(host, x, floorY, floorY + 5, 10f, () =>
            {
                FillColumn(host, x, floorY, floorY + 5, MaterialIds.Water);
            });

            host.Config.densityExchangeRate = 0f;
            for (int y = floorY; y <= floorY + 5; y++)
                PaintHeat(host, x, y, y - floorY);
            yield return Step(host, 1);
            host.Config.densityExchangeRate = 64f;

            Vector4[] beforeStates = null;
            uint[] beforeMats = null;
            yield return ReadMaterialsAndState(host, (mats, states) =>
            {
                beforeMats = mats;
                beforeStates = states;
            });
            for (int y = floorY; y <= floorY + 5; y++)
                Assert.That(beforeMats[Index(host, x, y)], Is.EqualTo(MaterialIds.Water));

            yield return Step(host, 12);

            Vector4[] afterStates = null;
            uint[] afterMats = null;
            yield return ReadMaterialsAndState(host, (mats, states) =>
            {
                afterMats = mats;
                afterStates = states;
            });

            for (int y = floorY; y <= floorY + 5; y++)
            {
                int idx = Index(host, x, y);
                Assert.That(afterMats[idx], Is.EqualTo(MaterialIds.Water));
                Assert.That(afterStates[idx].x, Is.EqualTo(beforeStates[idx].x).Within(0.25f),
                    "Equal-density water cells must not exchange payloads.");
            }
        }

        [UnityTest]
        public IEnumerator DensityExchangePreservesCellPayload()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureDensityIsolation(host);

            int x = CrustX(host, 5);
            int floorY = CrustFloorY(host);
            yield return PrepareColumn(host, x, floorY, floorY + 1, 15f, () =>
            {
                Paint(host, x, floorY, MaterialIds.Water);
                Paint(host, x, floorY + 1, MaterialIds.Rock);
            });

            host.Config.densityExchangeRate = 0f;
            PaintHeat(host, x, floorY + 1, 42f);
            yield return Step(host, 1);

            uint[] beforeMats = null;
            Vector4[] beforeStates = null;
            yield return ReadMaterialsAndState(host, (mats, states) =>
            {
                beforeMats = mats;
                beforeStates = states;
            });
            Assert.That(beforeMats[Index(host, x, floorY + 1)], Is.EqualTo(MaterialIds.Rock));
            float rockHeat = beforeStates[Index(host, x, floorY + 1)].x;
            Assert.That(rockHeat, Is.EqualTo(57f).Within(0.5f));

            host.Config.densityExchangeRate = 64f;
            yield return Step(host, 8);

            uint[] afterMats = null;
            Vector4[] afterStates = null;
            yield return ReadMaterialsAndState(host, (mats, states) =>
            {
                afterMats = mats;
                afterStates = states;
            });

            Assert.That(afterMats[Index(host, x, floorY)], Is.EqualTo(MaterialIds.Rock));
            Assert.That(afterStates[Index(host, x, floorY)].x, Is.EqualTo(rockHeat).Within(0.05f),
                "Rock should carry its temperature payload through the exchange.");
        }

        private static int Count(uint[] materials, uint id)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == id) count++;
            return count;
        }
    }
}
