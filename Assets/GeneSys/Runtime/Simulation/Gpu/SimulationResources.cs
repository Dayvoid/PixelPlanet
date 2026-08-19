using System;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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

        public void Swap()
        {
            (MaterialRead, MaterialWrite) = (MaterialWrite, MaterialRead);
            (StateRead, StateWrite) = (StateWrite, StateRead);
            (FlowRead, FlowWrite) = (FlowWrite, FlowRead);
            (AuxRead, AuxWrite) = (AuxWrite, AuxRead);
            (ShadeRead, ShadeWrite) = (ShadeWrite, ShadeRead);
        }

        public void CopyReadToWrite()
        {
            Graphics.CopyTexture(MaterialRead, MaterialWrite);
            Graphics.CopyTexture(StateRead, StateWrite);
            Graphics.CopyTexture(FlowRead, FlowWrite);
            Graphics.CopyTexture(AuxRead, AuxWrite);
            Graphics.CopyTexture(ShadeRead, ShadeWrite);
        }

        public void Dispose()
        {
            Release(MaterialRead); Release(MaterialWrite);
            Release(StateRead); Release(StateWrite);
            Release(FlowRead); Release(FlowWrite);
            Release(AuxRead); Release(AuxWrite);
            Release(ShadeRead); Release(ShadeWrite);
            MaterialRead = MaterialWrite = StateRead = StateWrite = null;
            FlowRead = FlowWrite = AuxRead = AuxWrite = null;
            ShadeRead = ShadeWrite = null;
        }

        private static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
        }
    }
}
