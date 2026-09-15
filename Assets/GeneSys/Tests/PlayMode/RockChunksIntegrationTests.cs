using System;
using System.Collections;
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
    public sealed class RockChunksIntegrationTests
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

        private static void ConfigureIsolation(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.magmaEruption = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.coreHeatRate = 0f;
            host.Config.thermalRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;
            host.Config.enableMaterialTransport = true;
            host.Config.gravityStrength = 1f;
            host.Config.enableRockChunks = true;
            host.Config.rockChunkMaxSearchTicks = 40;
            host.Config.rockChunkHopsPerTick = 1;
            host.Config.rockChunkMinCells = 3;
            host.Config.rockChunkMaxCells = 96;
        }

        private static void RestoreConfig(SimulationHost host)
        {
            host.Config.enableMaterialTransport = true;
            host.Config.gravityStrength = 1f;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.slowPassInterval = 4;
            host.Config.thermalRate = 1f;
            host.Config.runoffRate = 2f;
            host.Config.pondingRate = 0.5f;
            host.Config.enableRockChunks = true;
        }

        private static int CountInColumn(uint[] mats, int width, int x, int y0, int y1, uint id)
        {
            int count = 0;
            for (int y = y0; y <= y1; y++)
            {
                if (mats[y * width + x] == id)
                    count++;
            }
            return count;
        }

        private static int CountRect(uint[] mats, int width, int x0, int x1, int y0, int y1, uint id)
        {
            int count = 0;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int wx = x;
                    while (wx < 0) wx += width;
                    wx %= width;
                    if (mats[y * width + wx] == id)
                        count++;
                }
            }
            return count;
        }

        private static void PaintAirShaft(SimulationHost host, int x, int floorY, int height)
        {
            for (int y = floorY + 1; y <= floorY + height; y++)
            {
                Paint(host, x - 2, y, MaterialIds.Air);
                Paint(host, x - 1, y, MaterialIds.Air);
                Paint(host, x, y, MaterialIds.Air);
                Paint(host, x + 1, y, MaterialIds.Air);
                Paint(host, x + 2, y, MaterialIds.Air);
            }
        }

        [UnityTest]
        public IEnumerator GranitePillarTipsSidewaysAfterBaseRemoved()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureIsolation(host);
            host.Config.seed = 7711;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 12, height - 24);

            for (int dx = -8; dx <= 8; dx++)
                Paint(host, x + dx, floorY, MaterialIds.Rock);
            PaintAirShaft(host, x, floorY, 8);
            for (int y = floorY + 2; y <= floorY + 7; y++)
                Paint(host, x, y, MaterialIds.Granite);

            yield return Step(host, 1);
            yield return Step(host, 90);

            yield return ReadMaterials(host, mats =>
            {
                int stillInColumn = CountInColumn(mats, width, x, floorY + 2, floorY + 7, MaterialIds.Granite);
                int nearby = CountRect(mats, width, x - 8, x + 8, floorY + 1, floorY + 8, MaterialIds.Granite);
                Assert.That(stillInColumn, Is.LessThan(4),
                    "A granite pillar that lost its base must tip out of its original angular column instead of stacking in place.");
                Assert.That(nearby, Is.GreaterThanOrEqualTo(4),
                    "Tipped pillar mass must remain nearby as granite after settling.");
            });

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator PillarSearchHoldsThenMovesWithinFortyTicks()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureIsolation(host);
            host.Config.seed = 7712;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 12, height - 24);

            for (int dx = -8; dx <= 8; dx++)
                Paint(host, x + dx, floorY, MaterialIds.Rock);
            PaintAirShaft(host, x, floorY, 8);
            for (int y = floorY + 2; y <= floorY + 7; y++)
                Paint(host, x, y, MaterialIds.Granite);

            yield return Step(host, 1);
            yield return Step(host, 5);

            yield return ReadMaterials(host, mats =>
            {
                int intact = CountInColumn(mats, width, x, floorY + 2, floorY + 7, MaterialIds.Granite);
                Assert.That(intact, Is.EqualTo(6),
                    "Chunk search must hold a multi-cell pillar for the first few ticks instead of raining it immediately.");
            });

            yield return Step(host, 40);

            yield return ReadMaterials(host, mats =>
            {
                int remaining = CountInColumn(mats, width, x, floorY + 2, floorY + 7, MaterialIds.Granite);
                Assert.That(remaining, Is.LessThan(6),
                    "By tick 40 the held pillar must have begun moving under chunk rotation or fall.");
            });

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator HingedGraniteSlabRotatesAsAUnit()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureIsolation(host);
            host.Config.seed = 7713;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 12, height - 24);

            for (int dx = -2; dx <= 8; dx++)
                Paint(host, x + dx, floorY, MaterialIds.Rock);
            for (int y = floorY + 1; y <= floorY + 4; y++)
            {
                Paint(host, x, y, MaterialIds.Rock);
                for (int dx = 1; dx <= 6; dx++)
                    Paint(host, x + dx, y, MaterialIds.Air);
            }
            for (int dx = 1; dx <= 6; dx++)
                Paint(host, x + dx, floorY + 4, MaterialIds.Granite);
            Paint(host, x, floorY + 4, MaterialIds.Rock);

            yield return Step(host, 1);
            yield return Step(host, 90);

            yield return ReadMaterials(host, mats =>
            {
                int stillOnShelf = 0;
                for (int dx = 1; dx <= 6; dx++)
                {
                    if (mats[(floorY + 4) * width + ((x + dx) % width)] == MaterialIds.Granite)
                        stillOnShelf++;
                }
                int below = CountRect(mats, width, x + 1, x + 8, floorY + 1, floorY + 3, MaterialIds.Granite);
                Assert.That(stillOnShelf, Is.LessThan(5),
                    "An undercut granite shelf must leave its original row instead of hanging as independent pixels.");
                Assert.That(below, Is.GreaterThan(0),
                    "The hinged slab must rotate downward as a body and occupy cells under the original shelf.");
            });

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator TippingPillarTriggersNeighboringPillar()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureIsolation(host);
            host.Config.seed = 7714;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x0 = width / 2;
            int x1 = x0 + 4;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 12, height - 24);

            for (int dx = -8; dx <= 12; dx++)
                Paint(host, x0 + dx, floorY, MaterialIds.Rock);
            for (int y = floorY + 1; y <= floorY + 8; y++)
            {
                for (int x = x0 - 2; x <= x1 + 2; x++)
                    Paint(host, x, y, MaterialIds.Air);
            }
            for (int y = floorY + 2; y <= floorY + 7; y++)
            {
                Paint(host, x0, y, MaterialIds.Granite);
                Paint(host, x1, y, MaterialIds.Granite);
            }
            Paint(host, x0 + 1, floorY + 7, MaterialIds.Granite);

            yield return Step(host, 1);
            yield return Step(host, 110);

            yield return ReadMaterials(host, mats =>
            {
                int first = CountInColumn(mats, width, x0, floorY + 2, floorY + 7, MaterialIds.Granite);
                int second = CountInColumn(mats, width, x1, floorY + 2, floorY + 7, MaterialIds.Granite);
                Assert.That(first, Is.LessThan(5),
                    "The first pillar must tip out of its column.");
                Assert.That(second, Is.LessThan(6),
                    "Impact or undercut from the first tipping pillar must move the neighboring pillar.");
            });

            RestoreConfig(host);
        }
    }
}
