using System;
using GeneSys.Simulation.Climate;
using GeneSys.Simulation.Geodynamics;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GeneSys.Simulation.Gpu
{
    public sealed class SimulationResources : IDisposable
    {
        public const int WaspSliceCount = 7;
        public const int WaspClaimCount = 5;
        public const int GrassVisitSliceCount = 2;
        public const int TreeSliceCount = 3;

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
        public RenderTexture FaunaRead { get; private set; }
        public RenderTexture FaunaWrite { get; private set; }
        public RenderTexture AcousticRead { get; private set; }
        public RenderTexture AcousticWrite { get; private set; }
        public RenderTexture AcousticPrev { get; private set; }
        public RenderTexture FaunaClaims { get; private set; }
        public RenderTexture GrassRead { get; private set; }
        public RenderTexture GrassWrite { get; private set; }
        public RenderTexture PropaguleRead { get; private set; }
        public RenderTexture PropaguleWrite { get; private set; }
        public RenderTexture GrassRootFlux { get; private set; }
        public RenderTexture GrassDropClaims { get; private set; }
        public RenderTexture WaspRead { get; private set; }
        public RenderTexture WaspWrite { get; private set; }
        public RenderTexture WaspClaims { get; private set; }
        public RenderTexture GrassVisit { get; private set; }
        public RenderTexture TreeRead { get; private set; }
        public RenderTexture TreeWrite { get; private set; }
        public RenderTexture TreeGrowthClaims { get; private set; }
        public RenderTexture PlantRootFlux => GrassRootFlux;
        public ComputeBuffer WaterColumn { get; private set; }
        public ComputeBuffer WaterFaceFlux { get; private set; }
        public ComputeBuffer ClimateColumns { get; private set; }
        public ComputeBuffer ClimateState { get; private set; }
        public ComputeBuffer GeodynamicsColumns { get; private set; }
        public ComputeBuffer GeodynamicsStateRead { get; private set; }
        public ComputeBuffer GeodynamicsStateWrite { get; private set; }
        public ComputeBuffer GeodynamicsEvents { get; private set; }
        public ComputeBuffer GeodynamicsEventCounter { get; private set; }
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
            FaunaRead = CreateTextureArray("GeneSys Fauna A", GraphicsFormat.R32G32B32A32_SFloat, 4);
            FaunaWrite = CreateTextureArray("GeneSys Fauna B", GraphicsFormat.R32G32B32A32_SFloat, 4);
            AcousticRead = CreateTexture("GeneSys Acoustic A", GraphicsFormat.R32G32_SFloat);
            AcousticWrite = CreateTexture("GeneSys Acoustic B", GraphicsFormat.R32G32_SFloat);
            AcousticPrev = CreateTexture("GeneSys Acoustic Prev", GraphicsFormat.R32G32_SFloat);
            FaunaClaims = CreateTextureArray("GeneSys Fauna Claims", GraphicsFormat.R32_UInt, 4);
            GrassRead = CreateTextureArray("GeneSys Grass A", GraphicsFormat.R32G32B32A32_SFloat, 12);
            GrassWrite = CreateTextureArray("GeneSys Grass B", GraphicsFormat.R32G32B32A32_SFloat, 12);
            PropaguleRead = CreateTextureArray("GeneSys Propagule A", GraphicsFormat.R32G32B32A32_SFloat, 3);
            PropaguleWrite = CreateTextureArray("GeneSys Propagule B", GraphicsFormat.R32G32B32A32_SFloat, 3);
            GrassRootFlux = CreateTexture("GeneSys Grass Root Flux", GraphicsFormat.R32G32B32A32_SFloat);
            GrassDropClaims = CreateTexture("GeneSys Grass Drop Claims", GraphicsFormat.R32_UInt);
            WaspRead = CreateTextureArray("GeneSys Wasp A", GraphicsFormat.R32G32B32A32_SFloat, WaspSliceCount);
            WaspWrite = CreateTextureArray("GeneSys Wasp B", GraphicsFormat.R32G32B32A32_SFloat, WaspSliceCount);
            WaspClaims = CreateTextureArray("GeneSys Wasp Claims", GraphicsFormat.R32_UInt, WaspClaimCount);
            GrassVisit = CreateTextureArray("GeneSys Grass Visit", GraphicsFormat.R32G32B32A32_SFloat, GrassVisitSliceCount);
            TreeRead = CreateTextureArray("GeneSys Tree A", GraphicsFormat.R32G32B32A32_SFloat, TreeSliceCount);
            TreeWrite = CreateTextureArray("GeneSys Tree B", GraphicsFormat.R32G32B32A32_SFloat, TreeSliceCount);
            TreeGrowthClaims = CreateTexture("GeneSys Tree Growth Claims", GraphicsFormat.R32_UInt);
            WaterColumn = CreateColumnBuffer();
            WaterFaceFlux = CreateColumnBuffer();
            ClimateColumns = CreateStructuredBuffer(ClimateGrid.ColumnBufferCount(Grid.angularResolution));
            ClimateState = CreateStructuredBuffer(ClimateGrid.StateBufferCount());
            GeodynamicsColumns = CreateStructuredBuffer(GeodynamicsGrid.ColumnBufferCount());
            GeodynamicsStateRead = CreateStructuredBuffer(GeodynamicsGrid.StateBufferCount());
            GeodynamicsStateWrite = CreateStructuredBuffer(GeodynamicsGrid.StateBufferCount());
            GeodynamicsEvents = CreateStructuredBuffer(GeodynamicsGrid.EventBufferCount());
            GeodynamicsEventCounter = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
            ClearFaunaAndAcoustic();
            ClearGrass();
            ClearWasp();
            ClearTree();
            ClearWaterColumns();
            ClearClimate();
            ClearGeodynamics();
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

        public void ClearGrass()
        {
            ClearRenderTarget(GrassRead);
            ClearRenderTarget(GrassWrite);
            ClearRenderTarget(PropaguleRead);
            ClearRenderTarget(PropaguleWrite);
            ClearRenderTarget(GrassRootFlux);
            ClearRenderTarget(GrassDropClaims);
        }

        public void ClearWasp()
        {
            ClearRenderTarget(WaspRead);
            ClearRenderTarget(WaspWrite);
            ClearRenderTarget(WaspClaims);
            ClearRenderTarget(GrassVisit);
        }

        public void ClearTree()
        {
            ClearRenderTarget(TreeRead);
            ClearRenderTarget(TreeWrite);
            ClearRenderTarget(TreeGrowthClaims);
        }

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
            (MaterialRead, MaterialWrite) = (MaterialWrite, MaterialRead);
            (StateRead, StateWrite) = (StateWrite, StateRead);
            (FlowRead, FlowWrite) = (FlowWrite, FlowRead);
            (AuxRead, AuxWrite) = (AuxWrite, AuxRead);
            (ShadeRead, ShadeWrite) = (ShadeWrite, ShadeRead);
            (EcologyRead, EcologyWrite) = (EcologyWrite, EcologyRead);
            (CombustionRead, CombustionWrite) = (CombustionWrite, CombustionRead);
            (LifeGenomeRead, LifeGenomeWrite) = (LifeGenomeWrite, LifeGenomeRead);
            // Storm, Light, Fauna, Acoustic, Claims, Grass, Propagule, Wasp, grass visit, root flux,
            // and hydrostatic column scratch are excluded: other kernels do not
            // copy them through WriteCell. Geodynamics lattice buffers are also excluded.
        }

        public void SwapStorm()
        {
            (StormRead, StormWrite) = (StormWrite, StormRead);
        }

        public void SwapFauna()
        {
            (FaunaRead, FaunaWrite) = (FaunaWrite, FaunaRead);
        }

        public void SwapWasp()
        {
            (WaspRead, WaspWrite) = (WaspWrite, WaspRead);
        }

        public void SwapGrass()
        {
            (GrassRead, GrassWrite) = (GrassWrite, GrassRead);
        }

        public void SwapPropagule()
        {
            (PropaguleRead, PropaguleWrite) = (PropaguleWrite, PropaguleRead);
        }

        public void SwapTree()
        {
            (TreeRead, TreeWrite) = (TreeWrite, TreeRead);
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
            Graphics.CopyTexture(FaunaRead, FaunaWrite);
            Graphics.CopyTexture(AcousticRead, AcousticWrite);
            Graphics.CopyTexture(GrassRead, GrassWrite);
            Graphics.CopyTexture(PropaguleRead, PropaguleWrite);
            Graphics.CopyTexture(WaspRead, WaspWrite);
            Graphics.CopyTexture(TreeRead, TreeWrite);
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
            Release(FaunaRead); Release(FaunaWrite);
            Release(AcousticRead); Release(AcousticWrite); Release(AcousticPrev);
            Release(FaunaClaims);
            Release(GrassRead); Release(GrassWrite);
            Release(PropaguleRead); Release(PropaguleWrite);
            Release(GrassRootFlux);
            Release(GrassDropClaims);
            Release(WaspRead); Release(WaspWrite);
            Release(WaspClaims);
            Release(GrassVisit);
            Release(TreeRead); Release(TreeWrite);
            Release(TreeGrowthClaims);
            WaterColumn?.Release();
            WaterFaceFlux?.Release();
            ClimateColumns?.Release();
            ClimateState?.Release();
            GeodynamicsColumns?.Release();
            GeodynamicsStateRead?.Release();
            GeodynamicsStateWrite?.Release();
            GeodynamicsEvents?.Release();
            GeodynamicsEventCounter?.Release();
            MaterialRead = MaterialWrite = StateRead = StateWrite = null;
            FlowRead = FlowWrite = AuxRead = AuxWrite = null;
            ShadeRead = ShadeWrite = null;
            EcologyRead = EcologyWrite = null;
            CombustionRead = CombustionWrite = null;
            StormRead = StormWrite = null;
            LifeGenomeRead = LifeGenomeWrite = null;
            LightField = null;
            FaunaRead = FaunaWrite = null;
            AcousticRead = AcousticWrite = AcousticPrev = null;
            FaunaClaims = null;
            GrassRead = GrassWrite = null;
            PropaguleRead = PropaguleWrite = null;
            GrassRootFlux = null;
            GrassDropClaims = null;
            WaspRead = WaspWrite = null;
            WaspClaims = null;
            GrassVisit = null;
            TreeRead = TreeWrite = null;
            TreeGrowthClaims = null;
            WaterColumn = null;
            WaterFaceFlux = null;
            ClimateColumns = null;
            ClimateState = null;
            GeodynamicsColumns = null;
            GeodynamicsStateRead = null;
            GeodynamicsStateWrite = null;
            GeodynamicsEvents = null;
            GeodynamicsEventCounter = null;
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
