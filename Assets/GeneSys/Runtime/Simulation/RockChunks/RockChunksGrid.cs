using System.Runtime.InteropServices;
using UnityEngine;

namespace GeneSys.Simulation.RockChunks
{
    public static class RockChunksGrid
    {
        public const int MaxSlots = 32;
        public const int MaxCellsPerChunk = 96;
        public const int HeaderStride = 128;
        public const int MemberStride = 16;
        public const uint EmptyPacked = 0xFFFFFFFFu;
        public const uint HopsUngrounded = 255u;

        public const uint StatusIdle = 0;
        public const uint StatusSearching = 1;
        public const uint StatusInFlight = 2;
        public const uint StatusStableHold = 3;
        public const uint StatusCapturePending = 4;

        public const uint FlagStable = 1u;
        public const uint FlagDetached = 2u;
        public const uint FlagHinged = 4u;
        public const uint FlagSettling = 8u;
        public const uint FlagGrounded = 16u;

        public static int MemberBufferCount() => MaxSlots * MaxCellsPerChunk;

        [StructLayout(LayoutKind.Sequential, Size = HeaderStride)]
        public struct Header
        {
            public uint status;
            public uint memberCount;
            public uint prevMemberCount;
            public uint ageTicks;
            public uint flags;
            public uint contactCount;
            public uint heelPacked;
            public uint seedPacked;
            public uint sumX;
            public uint sumY;
            public uint contactSumX;
            public uint contactSumY;
            public uint contactMinOff;
            public uint contactMaxOff;
            public uint massU;
            public uint maxRadiusU;
            public float pivotX;
            public float pivotY;
            public float angle;
            public float omega;
            public float restPivotX;
            public float restPivotY;
            public float lastDAngle;
            public float lastPivotDrop;
            public int biasSign;
            public uint collision;
            public uint halfStepUsed;
            public uint ageInFlight;
            public uint bottomMinOffU;
            public uint bottomMaxOffU;
            public uint maxY;
            public uint selfConflict;
        }

        [StructLayout(LayoutKind.Sequential, Size = MemberStride)]
        public struct Member
        {
            public uint packedXY;
            public uint chunkId;
            public float restOx;
            public float restOy;
        }
    }
}
