using System;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GeneSys.Simulation.Gpu
{
    public sealed class SimulationResources : IDisposable
    {
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
        public GraphicsBuffer FloraMoveClaims { get; private set; }
        public GraphicsBuffer FloraMoveClaimsRead { get; private set; }
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
            FloraMoveClaims = CreateClaimBuffer();
            FloraMoveClaimsRead = CreateClaimBuffer();
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

        private GraphicsBuffer CreateClaimBuffer()
        {
            int count = Mathf.Max(1, Grid.angularResolution * Grid.radialResolution);
            const GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured
                | GraphicsBuffer.Target.CopySource
                | GraphicsBuffer.Target.CopyDestination;
            return new GraphicsBuffer(target, count, sizeof(uint));
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
            // Storm is excluded: other kernels do not copy it through WriteCell.
            // Light is derived each tick and is not ping-ponged.
        }

        public void SwapStorm()
        {
            (StormRead, StormWrite) = (StormWrite, StormRead);
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
            FloraMoveClaims?.Dispose();
            FloraMoveClaimsRead?.Dispose();
            MaterialRead = MaterialWrite = StateRead = StateWrite = null;
            FlowRead = FlowWrite = AuxRead = AuxWrite = null;
            ShadeRead = ShadeWrite = null;
            EcologyRead = EcologyWrite = null;
            CombustionRead = CombustionWrite = null;
            StormRead = StormWrite = null;
            LifeGenomeRead = LifeGenomeWrite = null;
            LightField = null;
            FloraMoveClaims = null;
            FloraMoveClaimsRead = null;
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
