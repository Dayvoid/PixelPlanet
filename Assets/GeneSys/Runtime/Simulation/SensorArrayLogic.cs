using System;
using System.Runtime.InteropServices;
using GeneSys.Materials;

namespace GeneSys.Simulation
{
    public struct SensorAnchor
    {
        public int X;
        public int Y;

        public SensorAnchor(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool SameAs(SensorAnchor other) => X == other.X && Y == other.Y;
    }

    public struct SensorPose
    {
        public SensorAnchor A;
        public SensorAnchor B;
    }

    /// <summary>
    /// GPU slot. Field order and 4-byte packing match SensorSlot in SensorArrays.compute.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SensorSlotGpu
    {
        public uint Alive;
        public uint Serial;
        public int AnchorAx;
        public int AnchorAy;
        public int AnchorBx;
        public int AnchorBy;
        public uint SurfaceMaterial;
        public uint Pad0;
        public float AtmTemp;
        public float AtmVapor;
        public float AtmWater;
        public float SurfaceTemp;
        public float SurfaceFilm;
        public float SurfaceGround;
        public float Pad1;
        public float Pad2;

        public const int Stride = 64;
    }

    /// <summary>
    /// One deploy request. Field order matches SensorSpawn in SensorArrays.compute.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SensorSpawnCommand
    {
        public uint Slot;
        public uint Serial;
        public int AnchorAx;
        public int AnchorAy;
        public int AnchorBx;
        public int AnchorBy;
        public uint Pad0;
        public uint Pad1;

        public const int Stride = 32;
    }

    /// <summary>
    /// Discrete pose for a 4-pixel sensor. Two anchors stay one Chebyshev cell apart
    /// and each has a cap one step radially outward. The compute kernel transcribes these rules.
    /// </summary>
    public static class SensorArrayLogic
    {
        public const int MaxSensors = 3;
        public const int AnchorSpan = 1;

        public static int WrapTheta(int x, int width)
        {
            if (width <= 0) return 0;
            int m = x % width;
            return m < 0 ? m + width : m;
        }

        public static int SignedTheta(int from, int to, int width)
        {
            int d = to - from;
            int half = width / 2;
            if (d > half) d -= width;
            if (d < -half) d += width;
            return d;
        }

        public static int Chebyshev(SensorAnchor a, SensorAnchor b, int width)
        {
            int dx = Math.Abs(SignedTheta(a.X, b.X, width));
            int dy = Math.Abs(a.Y - b.Y);
            return Math.Max(dx, dy);
        }

        public static bool IsOpen(uint material) =>
            material == MaterialIds.Void || material == MaterialIds.Air || material == MaterialIds.Vapor;

        public static bool TryGetBody(
            SensorPose pose,
            int width,
            int height,
            out SensorAnchor a,
            out SensorAnchor b,
            out SensorAnchor capA,
            out SensorAnchor capB)
        {
            a = new SensorAnchor(WrapTheta(pose.A.X, width), pose.A.Y);
            b = new SensorAnchor(WrapTheta(pose.B.X, width), pose.B.Y);
            capA = new SensorAnchor(a.X, a.Y + 1);
            capB = new SensorAnchor(b.X, b.Y + 1);
            if (a.Y < 0 || b.Y < 0 || capA.Y >= height || capB.Y >= height)
                return false;
            if (Chebyshev(a, b, width) != AnchorSpan)
                return false;
            if (SignedTheta(a.X, b.X, width) == 0)
                return false;
            if (a.SameAs(b) || a.SameAs(capA) || a.SameAs(capB) || b.SameAs(capA) || b.SameAs(capB) || capA.SameAs(capB))
                return false;
            return true;
        }

        public static bool IsSupported(SensorAnchor anchor, int width, Func<int, int, uint> materialAt)
        {
            if (anchor.Y <= 0) return true;
            uint below = materialAt(WrapTheta(anchor.X, width), anchor.Y - 1);
            if (below == MaterialIds.Sensor) return false;
            return !IsOpen(below);
        }

        public static bool TrySpawnPose(int theta, int angularResolution, int radialResolution, out SensorPose pose)
        {
            pose = default;
            int y = radialResolution - 2;
            if (y < 0 || radialResolution < 2 || angularResolution < 2)
                return false;
            pose = new SensorPose
            {
                A = new SensorAnchor(WrapTheta(theta, angularResolution), y),
                B = new SensorAnchor(WrapTheta(theta + 1, angularResolution), y)
            };
            return TryGetBody(pose, angularResolution, radialResolution, out _, out _, out _, out _);
        }

        public static int ChooseSpawnSlot(uint[] serials, bool[] alive)
        {
            int count = Math.Min(MaxSensors, Math.Min(serials?.Length ?? 0, alive?.Length ?? 0));
            if (count <= 0) return 0;
            for (int i = 0; i < count; i++)
                if (!alive[i]) return i;
            int oldest = 0;
            for (int i = 1; i < count; i++)
                if (serials[i] < serials[oldest]) oldest = i;
            return oldest;
        }

        public static bool TryStep(SensorPose current, int width, int height, Func<int, int, uint> materialAt, out SensorPose next)
        {
            next = current;
            if (!TryGetBody(current, width, height, out SensorAnchor a, out SensorAnchor b, out _, out _))
                return false;
            next = new SensorPose { A = a, B = b };
            bool supA = IsSupported(a, width, materialAt);
            bool supB = IsSupported(b, width, materialAt);
            if (supA && supB)
                return false;
            if (!supA && !supB)
                return TryFall(next, width, height, materialAt, out next);

            SensorAnchor pivot = supA ? a : b;
            SensorAnchor free = supA ? b : a;
            var dropped = new SensorAnchor(free.X, free.Y - 1);
            if (dropped.Y >= 0)
            {
                var sheared = new SensorPose { A = pivot, B = dropped };
                if (TryGetBody(sheared, width, height, out _, out _, out _, out _)
                    && BodyFree(sheared, next, width, height, materialAt))
                {
                    next = sheared;
                    return true;
                }
            }

            if (TryRoll(pivot, free, next, width, height, materialAt, out SensorPose rolled))
            {
                next = rolled;
                return true;
            }

            return TryFall(next, width, height, materialAt, out next);
        }

        public static int MatchSlot(SensorSlotGpu[] slots, int width, int height, int x, int y, int maxDistance)
        {
            if (slots == null || width <= 0 || height <= 0) return -1;
            int best = -1;
            int bestDistance = int.MaxValue;
            int count = Math.Min(MaxSensors, slots.Length);
            for (int i = 0; i < count; i++)
            {
                int distance = BodyDistance(slots[i], width, height, x, y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return bestDistance <= maxDistance ? best : -1;
        }

        public static SensorPose PoseOf(SensorSlotGpu slot) => new SensorPose
        {
            A = new SensorAnchor(slot.AnchorAx, slot.AnchorAy),
            B = new SensorAnchor(slot.AnchorBx, slot.AnchorBy)
        };

        public static int BodyDistance(SensorSlotGpu slot, int width, int height, int x, int y)
        {
            if (slot.Alive == 0) return int.MaxValue;
            if (!TryGetBody(PoseOf(slot), width, height, out SensorAnchor a, out SensorAnchor b, out SensorAnchor capA, out SensorAnchor capB))
                return int.MaxValue;
            int best = DistanceTo(x, y, a, width);
            best = Math.Min(best, DistanceTo(x, y, b, width));
            best = Math.Min(best, DistanceTo(x, y, capA, width));
            best = Math.Min(best, DistanceTo(x, y, capB, width));
            return best;
        }

        private static int DistanceTo(int x, int y, SensorAnchor cell, int width)
        {
            int dx = Math.Abs(SignedTheta(x, cell.X, width));
            int dy = Math.Abs(y - cell.Y);
            return Math.Max(dx, dy);
        }

        private static bool TryFall(SensorPose current, int width, int height, Func<int, int, uint> materialAt, out SensorPose next)
        {
            next = current;
            var fallen = new SensorPose
            {
                A = new SensorAnchor(current.A.X, current.A.Y - 1),
                B = new SensorAnchor(current.B.X, current.B.Y - 1)
            };
            if (!TryGetBody(fallen, width, height, out _, out _, out _, out _))
                return false;
            if (!BodyFree(fallen, current, width, height, materialAt))
                return false;
            next = fallen;
            return true;
        }

        private static bool TryRoll(
            SensorAnchor pivot,
            SensorAnchor free,
            SensorPose oldPose,
            int width,
            int height,
            Func<int, int, uint> materialAt,
            out SensorPose rolled)
        {
            rolled = default;
            int side = SignedTheta(pivot.X, free.X, width);
            int sideSign = side < 0 ? -1 : 1;
            int[] dxOrder = { sideSign, -sideSign, 0 };
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int i = 0; i < dxOrder.Length; i++)
                {
                    int dx = dxOrder[i];
                    if (dx == 0 && dy == 0) continue;
                    var candidate = new SensorAnchor(WrapTheta(pivot.X + dx, width), pivot.Y + dy);
                    if (candidate.Y < 0) continue;
                    var pose = new SensorPose { A = pivot, B = candidate };
                    if (!TryGetBody(pose, width, height, out _, out _, out _, out _))
                        continue;
                    if (!BodyFree(pose, oldPose, width, height, materialAt))
                        continue;
                    if (!IsSupported(candidate, width, materialAt))
                        continue;
                    rolled = pose;
                    return true;
                }
            }
            return false;
        }

        private static bool BodyFree(SensorPose pose, SensorPose oldPose, int width, int height, Func<int, int, uint> materialAt)
        {
            if (!TryGetBody(pose, width, height, out SensorAnchor a, out SensorAnchor b, out SensorAnchor capA, out SensorAnchor capB))
                return false;
            if (!TryGetBody(oldPose, width, height, out SensorAnchor oldA, out SensorAnchor oldB, out SensorAnchor oldCapA, out SensorAnchor oldCapB))
                return false;
            return CellFree(a, oldA, oldB, oldCapA, oldCapB, width, materialAt)
                && CellFree(b, oldA, oldB, oldCapA, oldCapB, width, materialAt)
                && CellFree(capA, oldA, oldB, oldCapA, oldCapB, width, materialAt)
                && CellFree(capB, oldA, oldB, oldCapA, oldCapB, width, materialAt);
        }

        private static bool CellFree(
            SensorAnchor cell,
            SensorAnchor oldA,
            SensorAnchor oldB,
            SensorAnchor oldCapA,
            SensorAnchor oldCapB,
            int width,
            Func<int, int, uint> materialAt)
        {
            if (cell.SameAs(oldA) || cell.SameAs(oldB) || cell.SameAs(oldCapA) || cell.SameAs(oldCapB))
                return true;
            return IsOpen(materialAt(WrapTheta(cell.X, width), cell.Y));
        }
    }
}
