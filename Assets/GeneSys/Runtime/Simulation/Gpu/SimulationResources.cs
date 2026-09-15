using System;
using GeneSys.Simulation.Climate;
using GeneSys.Simulation.Geodynamics;
using GeneSys.Simulation.RockChunks;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GeneSys.Simulation.Gpu
{
    public sealed class SimulationResources : IDisposable
    {
        public const int FloraSliceCount = 5;
        public const int FaunaSliceCount = 11;
        public const int FloraClaimsCount = 1;
        public const int FaunaClaimsCount = 5;
        public const int PropaguleSliceCount = 2;

        // Backward compatibility constants:
        public const int WaspSliceCount = 11;
        public const int WaspClaimCount = 5;
        public const int GrassVisitSliceCount = 2;
        public const int TreeSliceCount = 5;

        public RenderTexture MaterialRead { get; private set; }
        public RenderTexture MaterialWrite { get; private set; }
        public RenderTexture StateRead { get; private set; }
        public RenderTexture StateWrite { get; private set; }
        public RenderTexture FlowRead { get; private set; }
        public RenderTexture FlowWrite { get; private set; }
        public RenderTexture AuxRead { get; private set; }
        public RenderTexture AuxWrite { get; private set; }
        public RenderTexture ShadeRead { get; private set; }
        public RenderTexture ShadeWrite { get; private set; }
        public RenderTexture EcologyRead { get; private set; }
        public RenderTexture EcologyWrite { get; private set; }
        public RenderTexture CombustionRead { get; private set; }
        public RenderTexture CombustionWrite { get; private set; }
        public RenderTexture StormRead { get; private set; }
        public RenderTexture StormWrite { get; private set; }
        public RenderTexture LifeGenomeRead { get; private set; }
        public RenderTexture LifeGenomeWrite { get; private set; }
        public RenderTexture LifeRead => LifeGenomeRead;
        public RenderTexture LifeWrite => LifeGenomeWrite;
        public RenderTexture GenomeRead => LifeGenomeRead;
        public RenderTexture GenomeWrite => LifeGenomeWrite;
        public RenderTexture LightField { get; private set; }

        // Unified Flora (depth 5: 0 physiology, 1 identity, 2 topology, 3 genome, 4 propagule):
        public RenderTexture FloraRead { get; private set; }
        public RenderTexture FloraWrite { get; private set; }
        public RenderTexture FloraClaims { get; private set; }
        public RenderTexture PropaguleRead { get; private set; }
        public RenderTexture PropaguleWrite { get; private set; }

        // Unified Fauna (depth 11: 0-3 cricket, 4-10 wasp):
        public RenderTexture FaunaRead { get; private set; }
        public RenderTexture FaunaWrite { get; private set; }
        public RenderTexture AcousticRead { get; private set; }
        public RenderTexture AcousticWrite { get; private set; }
        public RenderTexture AcousticPrev { get; private set; }
        public RenderTexture FaunaClaims { get; private set; }

        // Backward-compatible aliases:
        public RenderTexture GrassRead => FloraRead;
        public RenderTexture GrassWrite => FloraWrite;
        public RenderTexture TreeRead => FloraRead;
        public RenderTexture TreeWrite => FloraWrite;
        public RenderTexture WaspRead => FaunaRead;
        public RenderTexture WaspWrite => FaunaWrite;
        public RenderTexture TreeGrowthClaims => FloraClaims;
        public RenderTexture GrassDropClaims => FloraClaims;
        public RenderTexture WaspClaims => FaunaClaims;
        public RenderTexture GrassVisit => null;
        public RenderTexture GrassRootFlux => null;
        public RenderTexture PlantRootFlux => null;

        public ComputeBuffer WaterColumn { get; private set; }
        public ComputeBuffer WaterFaceFlux { get; private set; }
        public ComputeBuffer ClimateColumns { get; private set; }
        public ComputeBuffer ClimateState { get; private set; }
        public ComputeBuffer GeodynamicsColumns { get; private set; }
        public ComputeBuffer GeodynamicsStateRead { get; private set; }
        public ComputeBuffer GeodynamicsStateWrite { get; private set; }
        public ComputeBuffer GeodynamicsEvents { get; private set; }
        public ComputeBuffer GeodynamicsEventCounter { get; private set; }
        public RenderTexture RockSupportRead { get; private set; }
        public RenderTexture RockSupportWrite { get; private set; }
        public RenderTexture RockChunkClaims { get; private set; }
        public RenderTexture RockChunkDest { get; private set; }
        public ComputeBuffer RockChunkHeaders { get; private set; }
        public ComputeBuffer RockChunkMembers { get; private set; }
        public PolarGridDefinition Grid { get; private set; }
        public bool IsCreated => MaterialRead != null && MaterialRead.IsCreated();

        public SimulationResources(PolarGridDefinition grid)
        {
            Grid = grid;
            Create();
        }

        private void Create()
        {
            MaterialRead = CreateTexture("GeneSys Material A", GraphicsFormat.R32_UInt);
            MaterialWrite = CreateTexture("GeneSys Material B", GraphicsFormat.R32_UInt);
            StateRead = CreateTexture("GeneSys State A", GraphicsFormat.R32G32B32A32_SFloat);
            StateWrite = CreateTexture("GeneSys State B", GraphicsFormat.R32G32B32A32_SFloat);
            FlowRead = CreateTexture("GeneSys Flow A", GraphicsFormat.R32G32_SFloat);
            FlowWrite = CreateTexture("GeneSys Flow B", GraphicsFormat.R32G32_SFloat);
            AuxRead = CreateTexture("GeneSys Aux A", GraphicsFormat.R32G32B32A32_SFloat);
            AuxWrite = CreateTexture("GeneSys Aux B", GraphicsFormat.R32G32B32A32_SFloat);
            ShadeRead = CreateTexture("GeneSys Shade A", GraphicsFormat.R32_UInt);
            ShadeWrite = CreateTexture("GeneSys Shade B", GraphicsFormat.R32_UInt);
            EcologyRead = CreateTexture("GeneSys Ecology A", GraphicsFormat.R32G32B32A32_SFloat);
            EcologyWrite = CreateTexture("GeneSys Ecology B", GraphicsFormat.R32G32B32A32_SFloat);
            CombustionRead = CreateTexture("GeneSys Combustion A", GraphicsFormat.R32G32B32A32_SFloat);
            CombustionWrite = CreateTexture("GeneSys Combustion B", GraphicsFormat.R32G32B32A32_SFloat);
            StormRead = CreateTexture("GeneSys Storm A", GraphicsFormat.R32G32B32A32_SFloat);
            StormWrite = CreateTexture("GeneSys Storm B", GraphicsFormat.R32G32B32A32_SFloat);
            LifeGenomeRead = CreateTextureArray("GeneSys LifeGenome A", GraphicsFormat.R32G32B32A32_SFloat, 2);
            LifeGenomeWrite = CreateTextureArray("GeneSys LifeGenome B", GraphicsFormat.R32G32B32A32_SFloat, 2);
            LightField = CreateTexture("GeneSys Light", GraphicsFormat.R32_SFloat);

            // Unified Flora:
            FloraRead = CreateTextureArray("GeneSys Flora A", GraphicsFormat.R32G32B32A32_SFloat, FloraSliceCount);
            FloraWrite = CreateTextureArray("GeneSys Flora B", GraphicsFormat.R32G32B32A32_SFloat, FloraSliceCount);
            FloraClaims = CreateTexture("GeneSys Flora Claims", GraphicsFormat.R32_UInt);
            PropaguleRead = CreateTextureArray("GeneSys Propagule A", GraphicsFormat.R32G32B32A32_SFloat, PropaguleSliceCount);
            PropaguleWrite = CreateTextureArray("GeneSys Propagule B", GraphicsFormat.R32G32B32A32_SFloat, PropaguleSliceCount);

            // Unified Fauna:
            FaunaRead = CreateTextureArray("GeneSys Fauna A", GraphicsFormat.R32G32B32A32_SFloat, FaunaSliceCount);
            FaunaWrite = CreateTextureArray("GeneSys Fauna B", GraphicsFormat.R32G32B32A32_SFloat, FaunaSliceCount);
            AcousticRead = CreateTexture("GeneSys Acoustic A", GraphicsFormat.R32G32_SFloat);
            AcousticWrite = CreateTexture("GeneSys Acoustic B", GraphicsFormat.R32G32_SFloat);
            AcousticPrev = CreateTexture("GeneSys Acoustic Prev", GraphicsFormat.R32G32_SFloat);
            FaunaClaims = CreateTextureArray("GeneSys Fauna Claims", GraphicsFormat.R32_UInt, FaunaClaimsCount);

            WaterColumn = CreateColumnBuffer();
            WaterFaceFlux = CreateColumnBuffer();
            ClimateColumns = CreateStructuredBuffer(ClimateGrid.ColumnBufferCount(Grid.angularResolution));
            ClimateState = CreateStructuredBuffer(ClimateGrid.StateBufferCount());
            GeodynamicsColumns = CreateStructuredBuffer(GeodynamicsGrid.ColumnBufferCount());
            GeodynamicsStateRead = CreateStructuredBuffer(GeodynamicsGrid.StateBufferCount());
            GeodynamicsStateWrite = CreateStructuredBuffer(GeodynamicsGrid.StateBufferCount());
            GeodynamicsEvents = CreateStructuredBuffer(GeodynamicsGrid.EventBufferCount());
            GeodynamicsEventCounter = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
            RockSupportRead = CreateTexture("GeneSys RockSupport A", GraphicsFormat.R32_UInt);
            RockSupportWrite = CreateTexture("GeneSys RockSupport B", GraphicsFormat.R32_UInt);
            RockChunkClaims = CreateTexture("GeneSys RockChunk Claims", GraphicsFormat.R32_UInt);
            RockChunkDest = CreateTexture("GeneSys RockChunk Dest", GraphicsFormat.R32_UInt);
            RockChunkHeaders = new ComputeBuffer(RockChunksGrid.MaxSlots, RockChunksGrid.HeaderStride, ComputeBufferType.Structured);
            RockChunkMembers = new ComputeBuffer(RockChunksGrid.MemberBufferCount(), RockChunksGrid.MemberStride, ComputeBufferType.Structured);

            ClearFlora();
            ClearFaunaAndAcoustic();
            ClearWaterColumns();
            ClearClimate();
            ClearGeodynamics();
            ClearRockChunks();
        }

        public void ClearWaterColumns()
        {
            int count = Mathf.Max(1, Grid.angularResolution);
            var zeros = new Vector4[count];
            WaterColumn?.SetData(zeros);
            WaterFaceFlux?.SetData(zeros);
        }

        public void ClearClimate()
        {
            if (ClimateColumns != null)
                ClimateColumns.SetData(new Vector4[ClimateGrid.ColumnBufferCount(Grid.angularResolution)]);
            if (ClimateState != null)
                ClimateState.SetData(new Vector4[ClimateGrid.StateBufferCount()]);
        }

        public void ClearGeodynamics()
        {
            if (GeodynamicsColumns != null)
                GeodynamicsColumns.SetData(new Vector4[GeodynamicsGrid.ColumnBufferCount()]);
            if (GeodynamicsStateRead != null)
                GeodynamicsStateRead.SetData(new Vector4[GeodynamicsGrid.StateBufferCount()]);
            if (GeodynamicsStateWrite != null)
                GeodynamicsStateWrite.SetData(new Vector4[GeodynamicsGrid.StateBufferCount()]);
            if (GeodynamicsEvents != null)
                GeodynamicsEvents.SetData(new Vector4[GeodynamicsGrid.EventBufferCount()]);
            GeodynamicsEventCounter?.SetData(new uint[1]);
        }

        public void ClearRockChunks()
        {
            ClearRenderTarget(RockSupportRead);
            ClearRenderTarget(RockSupportWrite);
            ClearRenderTarget(RockChunkClaims);
            ClearRenderTarget(RockChunkDest);
            if (RockChunkHeaders != null)
                RockChunkHeaders.SetData(new RockChunksGrid.Header[RockChunksGrid.MaxSlots]);
            if (RockChunkMembers != null)
            {
                var members = new RockChunksGrid.Member[RockChunksGrid.MemberBufferCount()];
                for (int i = 0; i < members.Length; i++)
                    members[i].packedXY = RockChunksGrid.EmptyPacked;
                RockChunkMembers.SetData(members);
            }
        }

        public void SwapRockSupport()
        {
            (RockSupportRead, RockSupportWrite) = (RockSupportWrite, RockSupportRead);
        }

        public void SwapGeodynamics()
        {
            (GeodynamicsStateRead, GeodynamicsStateWrite) = (GeodynamicsStateWrite, GeodynamicsStateRead);
        }

        public void CopyGeodynamicsReadToWrite()
        {
            if (GeodynamicsStateRead == null || GeodynamicsStateWrite == null) return;
            var values = new Vector4[GeodynamicsGrid.StateBufferCount()];
            GeodynamicsStateRead.GetData(values);
            GeodynamicsStateWrite.SetData(values);
        }

        public void ClearFlora()
        {
            ClearRenderTarget(FloraRead);
            ClearRenderTarget(FloraWrite);
            ClearRenderTarget(FloraClaims);
            ClearRenderTarget(PropaguleRead);
            ClearRenderTarget(PropaguleWrite);
        }

        public void ClearGrass() => ClearFlora();
        public void ClearTree() => ClearFlora();
        public void ClearWasp() => ClearFaunaAndAcoustic();

        public void ClearFaunaAndAcoustic()
        {
            ClearRenderTarget(FaunaRead);
            ClearRenderTarget(FaunaWrite);
            ClearRenderTarget(AcousticRead);
            ClearRenderTarget(AcousticWrite);
            ClearRenderTarget(AcousticPrev);
            ClearRenderTarget(FaunaClaims);
        }

        private static void ClearRenderTarget(RenderTexture texture)
        {
            if (texture == null) return;
            int slices = texture.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray
                ? Mathf.Max(1, texture.volumeDepth)
                : 1;
            RenderTexture previous = RenderTexture.active;
            for (int slice = 0; slice < slices; slice++)
            {
                Graphics.SetRenderTarget(texture, 0, CubemapFace.Unknown, slice);
                GL.Clear(false, true, Color.clear);
            }
            RenderTexture.active = previous;
        }

        private RenderTexture CreateTexture(string name, GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(Grid.angularResolution, Grid.radialResolution)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false
            };
            var texture = new RenderTexture(descriptor)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.Create();
            return texture;
        }

        private ComputeBuffer CreateColumnBuffer()
        {
            return CreateStructuredBuffer(Mathf.Max(1, Grid.angularResolution));
        }

        private static ComputeBuffer CreateStructuredBuffer(int count)
        {
            return new ComputeBuffer(Mathf.Max(1, count), sizeof(float) * 4, ComputeBufferType.Structured);
        }

        private RenderTexture CreateTextureArray(string name, GraphicsFormat format, int slices)
        {
            var descriptor = new RenderTextureDescriptor(Grid.angularResolution, Grid.radialResolution)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                volumeDepth = Mathf.Max(1, slices)
            };
            var texture = new RenderTexture(descriptor)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.Create();
            return texture;
        }

        public void Swap()
        {
            // Fine-grid ping-pong only. Geodynamics lattice buffers are also excluded; use SwapGeodynamics.
            (MaterialRead, MaterialWrite) = (MaterialWrite, MaterialRead);
            (StateRead, StateWrite) = (StateWrite, StateRead);
            (FlowRead, FlowWrite) = (FlowWrite, FlowRead);
            (AuxRead, AuxWrite) = (AuxWrite, AuxRead);
            (ShadeRead, ShadeWrite) = (ShadeWrite, ShadeRead);
            (EcologyRead, EcologyWrite) = (EcologyWrite, EcologyRead);
            (CombustionRead, CombustionWrite) = (CombustionWrite, CombustionRead);
            (LifeGenomeRead, LifeGenomeWrite) = (LifeGenomeWrite, LifeGenomeRead);
        }

        public void SwapStorm()
        {
            (StormRead, StormWrite) = (StormWrite, StormRead);
        }

        public void SwapFlora()
        {
            (FloraRead, FloraWrite) = (FloraWrite, FloraRead);
        }

        public void SwapFauna()
        {
            (FaunaRead, FaunaWrite) = (FaunaWrite, FaunaRead);
        }

        public void SwapGrass() => SwapFlora();
        public void SwapTree() => SwapFlora();
        public void SwapWasp() => SwapFauna();

        public void SwapPropagule()
        {
            (PropaguleRead, PropaguleWrite) = (PropaguleWrite, PropaguleRead);
        }

        public void SwapAcoustic()
        {
            RenderTexture oldPrev = AcousticPrev;
            AcousticPrev = AcousticRead;
            AcousticRead = AcousticWrite;
            AcousticWrite = oldPrev;
        }

        public void CopyReadToWrite()
        {
            Graphics.CopyTexture(MaterialRead, MaterialWrite);
            Graphics.CopyTexture(StateRead, StateWrite);
            Graphics.CopyTexture(FlowRead, FlowWrite);
            Graphics.CopyTexture(AuxRead, AuxWrite);
            Graphics.CopyTexture(ShadeRead, ShadeWrite);
            Graphics.CopyTexture(EcologyRead, EcologyWrite);
            Graphics.CopyTexture(CombustionRead, CombustionWrite);
            Graphics.CopyTexture(StormRead, StormWrite);
            Graphics.CopyTexture(LifeGenomeRead, LifeGenomeWrite);
            Graphics.CopyTexture(FloraRead, FloraWrite);
            Graphics.CopyTexture(FaunaRead, FaunaWrite);
            Graphics.CopyTexture(AcousticRead, AcousticWrite);
            Graphics.CopyTexture(PropaguleRead, PropaguleWrite);
            if (RockSupportRead != null && RockSupportWrite != null)
                Graphics.CopyTexture(RockSupportRead, RockSupportWrite);
        }

        public void Dispose()
        {
            Release(MaterialRead); Release(MaterialWrite);
            Release(StateRead); Release(StateWrite);
            Release(FlowRead); Release(FlowWrite);
            Release(AuxRead); Release(AuxWrite);
            Release(ShadeRead); Release(ShadeWrite);
            Release(EcologyRead); Release(EcologyWrite);
            Release(CombustionRead); Release(CombustionWrite);
            Release(StormRead); Release(StormWrite);
            Release(LifeGenomeRead); Release(LifeGenomeWrite);
            Release(LightField);
            Release(FloraRead); Release(FloraWrite);
            Release(FloraClaims);
            Release(PropaguleRead); Release(PropaguleWrite);
            Release(FaunaRead); Release(FaunaWrite);
            Release(AcousticRead); Release(AcousticWrite); Release(AcousticPrev);
            Release(FaunaClaims);
            WaterColumn?.Release();
            WaterFaceFlux?.Release();
            ClimateColumns?.Release();
            ClimateState?.Release();
            GeodynamicsColumns?.Release();
            GeodynamicsStateRead?.Release();
            GeodynamicsStateWrite?.Release();
            GeodynamicsEvents?.Release();
            GeodynamicsEventCounter?.Release();
            RockChunkHeaders?.Release();
            RockChunkMembers?.Release();
            Release(RockSupportRead); Release(RockSupportWrite);
            Release(RockChunkClaims); Release(RockChunkDest);
            MaterialRead = MaterialWrite = StateRead = StateWrite = null;
            FlowRead = FlowWrite = AuxRead = AuxWrite = null;
            ShadeRead = ShadeWrite = null;
            EcologyRead = EcologyWrite = null;
            CombustionRead = CombustionWrite = null;
            StormRead = StormWrite = null;
            LifeGenomeRead = LifeGenomeWrite = null;
            LightField = null;
            FloraRead = FloraWrite = null;
            FloraClaims = null;
            PropaguleRead = PropaguleWrite = null;
            FaunaRead = FaunaWrite = null;
            AcousticRead = AcousticWrite = AcousticPrev = null;
            FaunaClaims = null;
            WaterColumn = null;
            WaterFaceFlux = null;
            ClimateColumns = null;
            ClimateState = null;
            GeodynamicsColumns = null;
            GeodynamicsStateRead = null;
            GeodynamicsStateWrite = null;
            GeodynamicsEvents = null;
            GeodynamicsEventCounter = null;
            RockChunkHeaders = null;
            RockChunkMembers = null;
            RockSupportRead = RockSupportWrite = null;
            RockChunkClaims = RockChunkDest = null;
        }

        private static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
