using System.Runtime.InteropServices;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.UI;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class SensorArrayTests
    {
        [Test]
        public void GpuStructsMatchShaderStrides()
        {
            Assert.That(Marshal.SizeOf<SensorSlotGpu>(), Is.EqualTo(SensorSlotGpu.Stride));
            Assert.That(Marshal.SizeOf<SensorSpawnCommand>(), Is.EqualTo(SensorSpawnCommand.Stride));
        }

        [Test]
        public void EvenSupportHoldsASquare()
        {
            const int width = 16;
            const int height = 24;
            uint[,] grid = Air(width, height);
            FillRow(grid, 9, MaterialIds.Rock);
            var pose = new SensorPose
            {
                A = new SensorAnchor(5, 10),
                B = new SensorAnchor(6, 10)
            };

            bool moved = SensorArrayLogic.TryStep(pose, width, height, At(grid), out SensorPose next);

            Assert.That(moved, Is.False);
            AssertAnchors(next, 5, 10, 6, 10);
            Assert.That(SensorArrayLogic.TryGetBody(next, width, height, out _, out _, out SensorAnchor capA, out SensorAnchor capB), Is.True);
            Assert.That(capA.Y, Is.EqualTo(11));
            Assert.That(capB.Y, Is.EqualTo(11));
        }

        [Test]
        public void UnsupportedSideShearsIntoADiagonal()
        {
            const int width = 16;
            const int height = 24;
            uint[,] grid = Air(width, height);
            grid[5, 9] = MaterialIds.Rock;
            grid[6, 8] = MaterialIds.Rock;
            var pose = new SensorPose
            {
                A = new SensorAnchor(5, 10),
                B = new SensorAnchor(6, 10)
            };

            bool moved = SensorArrayLogic.TryStep(pose, width, height, At(grid), out SensorPose next);

            Assert.That(moved, Is.True);
            AssertAnchors(next, 5, 10, 6, 9);
            Assert.That(SensorArrayLogic.TryGetBody(next, width, height, out SensorAnchor a, out SensorAnchor b, out SensorAnchor capA, out SensorAnchor capB), Is.True);
            Assert.That(capA.Y, Is.EqualTo(a.Y + 1));
            Assert.That(capB.Y, Is.EqualTo(b.Y + 1));
            Assert.That(Mathf.Abs(a.Y - b.Y), Is.EqualTo(1));
        }

        [Test]
        public void DropPastTheSpanRollsAroundThePivot()
        {
            const int width = 16;
            const int height = 24;
            uint[,] grid = Air(width, height);
            grid[5, 9] = MaterialIds.Rock;
            grid[4, 8] = MaterialIds.Rock;
            var pose = new SensorPose
            {
                A = new SensorAnchor(5, 10),
                B = new SensorAnchor(6, 9)
            };

            bool moved = SensorArrayLogic.TryStep(pose, width, height, At(grid), out SensorPose next);

            Assert.That(moved, Is.True);
            AssertAnchors(next, 5, 10, 4, 9);
        }

        [Test]
        public void FlatGroundReformsTheSquare()
        {
            const int width = 16;
            const int height = 24;
            uint[,] grid = Air(width, height);
            grid[6, 8] = MaterialIds.Rock;
            grid[5, 8] = MaterialIds.Rock;
            var pose = new SensorPose
            {
                A = new SensorAnchor(5, 10),
                B = new SensorAnchor(6, 9)
            };

            bool moved = SensorArrayLogic.TryStep(pose, width, height, At(grid), out SensorPose next);

            Assert.That(moved, Is.True);
            AssertAnchors(next, 5, 9, 6, 9);
        }

        [Test]
        public void UnsupportedBodyFallsOneCell()
        {
            const int width = 16;
            const int height = 24;
            uint[,] grid = Air(width, height);
            var pose = new SensorPose
            {
                A = new SensorAnchor(5, 10),
                B = new SensorAnchor(6, 10)
            };

            bool moved = SensorArrayLogic.TryStep(pose, width, height, At(grid), out SensorPose next);

            Assert.That(moved, Is.True);
            AssertAnchors(next, 5, 9, 6, 9);
        }

        [Test]
        public void ForeignSensorBelowBlocksTheFall()
        {
            const int width = 16;
            const int height = 24;
            uint[,] grid = Air(width, height);
            grid[5, 9] = MaterialIds.Sensor;
            grid[6, 9] = MaterialIds.Sensor;
            var pose = new SensorPose
            {
                A = new SensorAnchor(5, 10),
                B = new SensorAnchor(6, 10)
            };

            bool moved = SensorArrayLogic.TryStep(pose, width, height, At(grid), out SensorPose next);

            Assert.That(moved, Is.False);
            AssertAnchors(next, 5, 10, 6, 10);
        }

        [Test]
        public void VerticalAnchorPairIsIllegal()
        {
            var pose = new SensorPose
            {
                A = new SensorAnchor(5, 10),
                B = new SensorAnchor(5, 11)
            };
            Assert.That(SensorArrayLogic.TryGetBody(pose, 16, 24, out _, out _, out _, out _), Is.False);
        }

        [Test]
        public void SpawnPoseSitsInsideTheRimAndWrapsTheta()
        {
            const int angular = 32;
            const int radial = 20;
            Assert.That(SensorArrayLogic.TrySpawnPose(angular - 1, angular, radial, out SensorPose pose), Is.True);
            AssertAnchors(pose, angular - 1, radial - 2, 0, radial - 2);
            Assert.That(SensorArrayLogic.TryGetBody(pose, angular, radial, out _, out _, out SensorAnchor capA, out SensorAnchor capB), Is.True);
            Assert.That(capA.Y, Is.EqualTo(radial - 1));
            Assert.That(capB.Y, Is.EqualTo(radial - 1));
        }

        [Test]
        public void WrappedHorizontalPairStaysLegal()
        {
            const int width = 32;
            const int height = 20;
            uint[,] grid = Air(width, height);
            FillRow(grid, 9, MaterialIds.Soil);
            var pose = new SensorPose
            {
                A = new SensorAnchor(0, 10),
                B = new SensorAnchor(width - 1, 10)
            };

            bool moved = SensorArrayLogic.TryStep(pose, width, height, At(grid), out SensorPose next);

            Assert.That(moved, Is.False);
            AssertAnchors(next, 0, 10, width - 1, 10);
            Assert.That(SensorArrayLogic.Chebyshev(next.A, next.B, width), Is.EqualTo(1));
        }

        [Test]
        public void ChooseSpawnSlotPrefersAFreeSlotThenTheOldest()
        {
            var serials = new uint[] { 5, 2, 9 };
            var alive = new[] { true, false, true };
            Assert.That(SensorArrayLogic.ChooseSpawnSlot(serials, alive), Is.EqualTo(1));

            alive[1] = true;
            Assert.That(SensorArrayLogic.ChooseSpawnSlot(serials, alive), Is.EqualTo(1));

            serials[0] = 4;
            serials[1] = 4;
            Assert.That(SensorArrayLogic.ChooseSpawnSlot(serials, alive), Is.EqualTo(0));

            alive[0] = false;
            alive[1] = false;
            alive[2] = false;
            Assert.That(SensorArrayLogic.ChooseSpawnSlot(serials, alive), Is.EqualTo(0));
        }

        [Test]
        public void ReadoutListsAtmosphereAndSurface()
        {
            var slot = new SensorSlotGpu
            {
                AtmTemp = 20f,
                AtmVapor = 0.01f,
                AtmWater = 0.25f,
                SurfaceMaterial = MaterialIds.Soil,
                SurfaceTemp = 18f,
                SurfaceFilm = 0.1f,
                SurfaceGround = 0.2f
            };
            float humidity = SimulationMetrics.RelativeHumidity(slot.AtmVapor, slot.AtmTemp, 0.01f);
            string text = SimulationUIController.FormatSensorReadout(slot, "Soil", 0.01f);

            Assert.That(text, Does.Contain("Atmosphere"));
            Assert.That(text, Does.Contain("T 20.00"));
            Assert.That(text, Does.Contain($"Humidity {humidity * 100f:F0}%"));
            Assert.That(text, Does.Contain("Water 0.260"));
            Assert.That(text, Does.Contain("Surface"));
            Assert.That(text, Does.Contain("Soil"));
            Assert.That(text, Does.Contain("T 18.00"));
            Assert.That(text, Does.Contain("Moisture 0.300"));
        }

        [Test]
        public void ReadoutPanelFlipsToTheOtherSideWhenItWouldLeaveTheScreen()
        {
            Vector2 inside = SimulationUIController.ClampPanelPosition(new Vector2(100f, 100f), new Vector2(240f, 120f), new Vector2(1920f, 1080f), 8f, 16f);
            Assert.That(inside.x, Is.EqualTo(116f).Within(0.01f));
            Assert.That(inside.y, Is.EqualTo(40f).Within(0.01f));

            Vector2 flipped = SimulationUIController.ClampPanelPosition(new Vector2(1800f, 100f), new Vector2(240f, 120f), new Vector2(1920f, 1080f), 8f, 16f);
            Assert.That(flipped.x, Is.EqualTo(1544f).Within(0.01f));
            Assert.That(flipped.y, Is.EqualTo(40f).Within(0.01f));
        }

        private static uint[,] Air(int width, int height)
        {
            var grid = new uint[width, height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = MaterialIds.Air;
            return grid;
        }

        private static void FillRow(uint[,] grid, int y, uint material)
        {
            for (int x = 0; x < grid.GetLength(0); x++)
                grid[x, y] = material;
        }

        private static System.Func<int, int, uint> At(uint[,] grid)
        {
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            return (x, y) =>
            {
                x = SensorArrayLogic.WrapTheta(x, width);
                if (y < 0 || y >= height) return MaterialIds.Void;
                return grid[x, y];
            };
        }

        private static void AssertAnchors(SensorPose pose, int x0, int y0, int x1, int y1)
        {
            bool match = (pose.A.X == x0 && pose.A.Y == y0 && pose.B.X == x1 && pose.B.Y == y1)
                || (pose.A.X == x1 && pose.A.Y == y1 && pose.B.X == x0 && pose.B.Y == y0);
            Assert.That(match, Is.True, $"anchors ({pose.A.X},{pose.A.Y}) ({pose.B.X},{pose.B.Y})");
        }
    }
}
