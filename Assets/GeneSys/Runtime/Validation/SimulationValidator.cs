using System;
using GeneSys.Materials;
using GeneSys.Simulation;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace GeneSys.Validation
{
    public sealed class SimulationValidator : MonoBehaviour
    {
        [SerializeField] private SimulationHost host;
        private long lastValidatedTick = -1;
        private bool pending;
        private double baselineWater = -1d;

        public bool LastValidationPassed { get; private set; } = true;
        public string LastMessage { get; private set; } = "Not yet validated";
        public event Action<bool, string> ValidationCompleted;

        public void Initialize(SimulationHost simulationHost) => host = simulationHost;

        public void ResetBaseline()
        {
            baselineWater = -1d;
            CaptureBaseline();
        }

        public void CaptureBaseline()
        {
            if (host == null || !host.IsReady || host.Resources == null || pending) return;
            RenderTexture stateTex = host.Resources.StateRead;
            RenderTexture auxTex = host.Resources.AuxRead;
            if (stateTex == null || auxTex == null) return;
            pending = true;
            AsyncGPUReadback.Request(stateTex, 0, stateRequest =>
            {
                if (stateRequest.hasError) { pending = false; return; }
                NativeArray<Vector4> state = stateRequest.GetData<Vector4>();
                double water = 0d;
                for (int i = 0; i < state.Length; i++)
                    water += Math.Max(0d, state[i].z);
                AsyncGPUReadback.Request(auxTex, 0, auxRequest =>
                {
                    if (auxRequest.hasError) { pending = false; return; }
                    NativeArray<Vector4> aux = auxRequest.GetData<Vector4>();
                    for (int i = 0; i < aux.Length; i++)
                        water += Math.Max(0d, aux[i].x) + Math.Max(0d, aux[i].y);
                    baselineWater = Math.Max(1d, water);
                    pending = false;
                });
            });
        }

        private void Update()
        {
            if (host == null || !host.IsReady || pending) return;
            long tick = host.Clock.TickCount;
            int interval = Mathf.Max(1, host.Config.validationIntervalTicks);
            if (host.Clock.Speed > SimulationClock.FastForwardSpeed)
                interval *= Mathf.Max(1, Mathf.CeilToInt(host.Clock.Speed));
            if (tick == 0 || tick == lastValidatedTick || tick % interval != 0) return;
            lastValidatedTick = tick;
            ValidateNow();
        }

        public void ValidateNow()
        {
            if (host == null || !host.IsReady || host.Resources == null || pending) return;
            RenderTexture stateTex = host.Resources.StateRead;
            RenderTexture auxTex = host.Resources.AuxRead;
            RenderTexture flowTex = host.Resources.FlowRead;
            RenderTexture ecologyTex = host.Resources.EcologyRead;
            RenderTexture combustionTex = host.Resources.CombustionRead;
            RenderTexture lifeGenomeTex = host.Resources.LifeGenomeRead;
            if (stateTex == null || auxTex == null || flowTex == null || ecologyTex == null || combustionTex == null || lifeGenomeTex == null) return;
            pending = true;
            AsyncGPUReadback.Request(stateTex, 0, stateRequest =>
            {
                if (stateRequest.hasError) { Complete(false, "State GPU readback failed."); return; }
                NativeArray<Vector4> state = stateRequest.GetData<Vector4>();
                double water = 0d;
                for (int i = 0; i < state.Length; i++)
                {
                    Vector4 value = state[i];
                    if (!Finite(value)) { Complete(false, $"Non-finite primary field at cell {i}."); return; }
                    if (value.y < -0.01f || value.z < -0.001f) { Complete(false, $"Negative pressure/moisture at cell {i}."); return; }
                    water += Math.Max(0d, value.z);
                }
                AsyncGPUReadback.Request(auxTex, 0, auxRequest =>
                {
                    if (auxRequest.hasError) { Complete(false, "Auxiliary GPU readback failed."); return; }
                    NativeArray<Vector4> aux = auxRequest.GetData<Vector4>();
                    for (int i = 0; i < aux.Length; i++)
                    {
                        Vector4 value = aux[i];
                        if (!Finite(value)) { Complete(false, $"Non-finite auxiliary field at cell {i}."); return; }
                        if (value.x < -0.001f || value.y < -0.001f) { Complete(false, $"Negative vapor/groundwater at cell {i}."); return; }
                        water += Math.Max(0d, value.x) + Math.Max(0d, value.y);
                    }
                    AsyncGPUReadback.Request(flowTex, 0, flowRequest =>
                    {
                        if (flowRequest.hasError) { Complete(false, "Flow GPU readback failed."); return; }
                        NativeArray<Vector2> flow = flowRequest.GetData<Vector2>();
                        for (int i = 0; i < flow.Length; i++)
                        {
                            Vector2 value = flow[i];
                            if (!float.IsFinite(value.x) || !float.IsFinite(value.y))
                            { Complete(false, $"Non-finite flow at cell {i}."); return; }
                            if (Mathf.Abs(value.x) > 20.5f || Mathf.Abs(value.y) > 20.5f)
                            { Complete(false, $"Runaway flow at cell {i}."); return; }
                        }
                        AsyncGPUReadback.Request(ecologyTex, 0, ecologyRequest =>
                        {
                            if (ecologyRequest.hasError) { Complete(false, "Ecology GPU readback failed."); return; }
                            NativeArray<Vector4> ecology = ecologyRequest.GetData<Vector4>();
                            for (int i = 0; i < ecology.Length; i++)
                            {
                                Vector4 value = ecology[i];
                                if (!Finite(value)) { Complete(false, $"Non-finite ecology field at cell {i}."); return; }
                                if (value.x < -0.001f) { Complete(false, $"Negative spore load at cell {i}."); return; }
                                if (value.y < -0.01f || value.y > 1.01f) { Complete(false, $"Myco value out of range at cell {i}."); return; }
                                uint traits = (uint)Mathf.Round(Mathf.Max(0f, value.z));
                                if (!MycologyTraits.IsValid(traits))
                                { Complete(false, $"Invalid mycology traits {value.z} at cell {i}."); return; }
                            }
                            AsyncGPUReadback.Request(combustionTex, 0, combustionRequest =>
                            {
                                if (combustionRequest.hasError) { Complete(false, "Combustion GPU readback failed."); return; }
                                NativeArray<Vector4> combustion = combustionRequest.GetData<Vector4>();
                                for (int i = 0; i < combustion.Length; i++)
                                {
                                    Vector4 value = combustion[i];
                                    if (!Finite(value)) { Complete(false, $"Non-finite combustion field at cell {i}."); return; }
                                    if (value.x < -0.001f || value.x > 1.01f) { Complete(false, $"Oxygen out of range at cell {i}."); return; }
                                    if (value.y < -0.01f || value.y > 1.01f) { Complete(false, $"Flame intensity out of range at cell {i}."); return; }
                                    if (value.z < -0.001f) { Complete(false, $"Negative soot at cell {i}."); return; }
                                    if (value.w < -0.01f || value.w > 1.01f) { Complete(false, $"Ignition accumulator out of range at cell {i}."); return; }
                                }
                                AsyncGPUReadback.Request(lifeGenomeTex, 0, 0, lifeGenomeTex.width, 0, lifeGenomeTex.height, 0, 1, lifeRequest =>
                                {
                                    if (lifeRequest.hasError) { Complete(false, "Life GPU readback failed."); return; }
                                    NativeArray<Vector4> life = lifeRequest.GetData<Vector4>();
                                    for (int i = 0; i < life.Length; i++)
                                    {
                                        Vector4 value = life[i];
                                        if (!Finite(value)) { Complete(false, $"Non-finite life field at cell {i}."); return; }
                                        if (value.x < -0.001f) { Complete(false, $"Negative flora spore load at cell {i}."); return; }
                                        if (value.y < -0.01f || value.y > 1.01f) { Complete(false, $"Flora biomass out of range at cell {i}."); return; }
                                        if (value.z < -0.01f || value.z > 1.01f) { Complete(false, $"Flora energy out of range at cell {i}."); return; }
                                    }
                                    AsyncGPUReadback.Request(lifeGenomeTex, 0, 0, lifeGenomeTex.width, 0, lifeGenomeTex.height, 1, 1, genomeRequest =>
                                    {
                                        if (genomeRequest.hasError) { Complete(false, "Genome GPU readback failed."); return; }
                                        NativeArray<Vector4> genomeBits = genomeRequest.GetData<Vector4>();
                                        for (int i = 0; i < genomeBits.Length; i++)
                                        {
                                            uint stage = FloraGenome.Stage(FloraGenome.FromFloatBits(genomeBits[i]));
                                            if (!FloraGenome.IsValidStage(stage))
                                            { Complete(false, $"Invalid flora stage {stage} at cell {i}."); return; }
                                        }
                                        RenderTexture faunaTex = host.Resources.FaunaRead;
                                        AsyncGPUReadback.Request(faunaTex, 0, 0, faunaTex.width, 0, faunaTex.height, 0, 1, faunaRequest =>
                                        {
                                            if (faunaRequest.hasError) { Complete(false, "Fauna GPU readback failed."); return; }
                                            NativeArray<Vector4> fauna = faunaRequest.GetData<Vector4>();
                                            for (int i = 0; i < fauna.Length; i++)
                                            {
                                                Vector4 value = fauna[i];
                                                if (!Finite(value)) { Complete(false, $"Non-finite fauna vitals at cell {i}."); return; }
                                                if (value.x < -0.01f || value.x > 1.01f) { Complete(false, $"Fauna calories out of range at cell {i}."); return; }
                                                if (value.y < -0.01f || value.y > 1.01f) { Complete(false, $"Fauna hydration out of range at cell {i}."); return; }
                                            }
                                            AsyncGPUReadback.Request(faunaTex, 0, 0, faunaTex.width, 0, faunaTex.height, 2, 1, faunaGenomeRequest =>
                                            {
                                                if (faunaGenomeRequest.hasError) { Complete(false, "Fauna genome GPU readback failed."); return; }
                                                NativeArray<Vector4> faunaBits = faunaGenomeRequest.GetData<Vector4>();
                                                for (int i = 0; i < faunaBits.Length; i++)
                                                {
                                                    uint faunaStage = FaunaGenome.Stage(FaunaGenome.FromFloatBits(faunaBits[i]));
                                                    if (!FaunaGenome.IsValidStage(faunaStage))
                                                    { Complete(false, $"Invalid fauna stage {faunaStage} at cell {i}."); return; }
                                                }
                                                AsyncGPUReadback.Request(host.Resources.AcousticRead, 0, acousticRequest =>
                                                {
                                                    if (acousticRequest.hasError) { Complete(false, "Acoustic GPU readback failed."); return; }
                                                    NativeArray<Vector2> acoustic = acousticRequest.GetData<Vector2>();
                                                    for (int i = 0; i < acoustic.Length; i++)
                                                    {
                                                        Vector2 value = acoustic[i];
                                                        if (!float.IsFinite(value.x) || !float.IsFinite(value.y))
                                                        { Complete(false, $"Non-finite acoustic field at cell {i}."); return; }
                                                        if (Mathf.Abs(value.x) > 1.5f || Mathf.Abs(value.y) > 1.5f)
                                                        { Complete(false, $"Runaway acoustic field at cell {i}."); return; }
                                                    }
                                                    if (host == null || !host.IsReady || host.Config == null)
                                                    { Complete(false, "Simulation host unavailable."); return; }
                                                    RenderTexture waspTex = host.Resources.WaspRead;
                                                    AsyncGPUReadback.Request(waspTex, 0, 0, waspTex.width, 0, waspTex.height, 0, 1, waspRequest =>
                                                    {
                                                        if (waspRequest.hasError) { Complete(false, "Wasp GPU readback failed."); return; }
                                                        NativeArray<Vector4> waspVitals = waspRequest.GetData<Vector4>();
                                                        for (int i = 0; i < waspVitals.Length; i++)
                                                        {
                                                            Vector4 value = waspVitals[i];
                                                            if (!Finite(value)) { Complete(false, $"Non-finite wasp vitals at cell {i}."); return; }
                                                            if (value.x < -0.01f || value.x > 1.01f) { Complete(false, $"Wasp calories out of range at cell {i}."); return; }
                                                            if (value.y < -0.01f || value.y > 1.01f) { Complete(false, $"Wasp hydration out of range at cell {i}."); return; }
                                                        }
                                                        AsyncGPUReadback.Request(waspTex, 0, 0, waspTex.width, 0, waspTex.height, 2, 1, waspGenomeRequest =>
                                                        {
                                                            if (waspGenomeRequest.hasError) { Complete(false, "Wasp genome GPU readback failed."); return; }
                                                            NativeArray<Vector4> waspBits = waspGenomeRequest.GetData<Vector4>();
                                                            for (int i = 0; i < waspBits.Length; i++)
                                                            {
                                                                uint waspStage = WaspGenome.Stage(WaspGenome.FromFloatBits(waspBits[i]));
                                                                if (!WaspGenome.IsValidStage(waspStage))
                                                                { Complete(false, $"Invalid wasp stage {waspStage} at cell {i}."); return; }
                                                            }
                                                            if (host == null || !host.IsReady || host.Config == null)
                                                            { Complete(false, "Simulation host unavailable."); return; }
                                                            RenderTexture treeTex = host.Resources.TreeRead;
                                                            RenderTexture materialTex = host.Resources.MaterialRead;
                                                            if (treeTex == null || materialTex == null)
                                                            { Complete(false, "Tree GPU resources unavailable."); return; }
                                                            AsyncGPUReadback.Request(treeTex, 0, 0, treeTex.width, 0, treeTex.height, TreeGenome.PhysiologySlice, 1, treePhysRequest =>
                                                            {
                                                                if (treePhysRequest.hasError) { Complete(false, "Tree physiology GPU readback failed."); return; }
                                                                NativeArray<Vector4> treePhys = treePhysRequest.GetData<Vector4>();
                                                                for (int i = 0; i < treePhys.Length; i++)
                                                                {
                                                                    if (!Finite(treePhys[i])) { Complete(false, $"Non-finite tree physiology at cell {i}."); return; }
                                                                    if (treePhys[i].x < -0.01f || treePhys[i].x > 1.01f) { Complete(false, $"Tree energy out of range at cell {i}."); return; }
                                                                    if (treePhys[i].y < -0.01f || treePhys[i].y > 1.01f) { Complete(false, $"Tree hydration out of range at cell {i}."); return; }
                                                                    if (treePhys[i].w < -0.01f || treePhys[i].w > 1.01f) { Complete(false, $"Tree health out of range at cell {i}."); return; }
                                                                }
                                                                AsyncGPUReadback.Request(treeTex, 0, 0, treeTex.width, 0, treeTex.height, TreeGenome.TopologySlice, 1, treeTopoRequest =>
                                                                {
                                                                    if (treeTopoRequest.hasError) { Complete(false, "Tree topology GPU readback failed."); return; }
                                                                    Vector4[] treeTopoBits = treeTopoRequest.GetData<Vector4>().ToArray();
                                                                    AsyncGPUReadback.Request(treeTex, 0, 0, treeTex.width, 0, treeTex.height, TreeGenome.GenomeSlice, 1, treeGenomeRequest =>
                                                                    {
                                                                        if (treeGenomeRequest.hasError) { Complete(false, "Tree genome GPU readback failed."); return; }
                                                                        Vector4[] treeGenomeBits = treeGenomeRequest.GetData<Vector4>().ToArray();
                                                                        if (host == null || !host.IsReady || host.Resources == null)
                                                                        { Complete(false, "Simulation host unavailable."); return; }
                                                                        AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
                                                                        {
                                                                            if (materialRequest.hasError) { Complete(false, "Material GPU readback failed."); return; }
                                                                            uint[] materials = materialRequest.GetData<uint>().ToArray();
                                                                            int width = host.Resources.TreeRead != null ? host.Resources.TreeRead.width : 0;
                                                                            int height = host.Resources.TreeRead != null ? host.Resources.TreeRead.height : 0;
                                                                            for (int i = 0; i < treeGenomeBits.Length; i++)
                                                                            {
                                                                                TreeGenome.Packed genome = TreeGenome.Sanitize(TreeGenome.FromFloatBits(treeGenomeBits[i]));
                                                                                TreeGenome.Packed topology = TreeGenome.FromFloatBits(treeTopoBits[i]);
                                                                                uint stage = TreeGenome.Stage(genome);
                                                                                uint role = TreeGenome.Role(topology.Z);
                                                                                if (!TreeGenome.IsValidStage(stage))
                                                                                { Complete(false, $"Invalid tree stage {stage} at cell {i}."); return; }
                                                                                if (!TreeGenome.IsValidRole(role))
                                                                                { Complete(false, $"Invalid tree role {role} at cell {i}."); return; }
                                                                                uint material = i < materials.Length ? materials[i] : 0u;
                                                                                bool occupied = topology.X != 0;
                                                                                if (!occupied && (material == MaterialIds.Leaf || material == MaterialIds.Wood))
                                                                                { Complete(false, $"Orphan tree material {material} at cell {i}."); return; }
                                                                                if (occupied && topology.Y != 0)
                                                                                {
                                                                                    uint packed = topology.Y;
                                                                                    int parent = (int)packed - 1;
                                                                                    if (parent < 0 || parent >= width * height)
                                                                                    { Complete(false, $"Invalid tree parent link at cell {i}."); return; }
                                                                                }
                                                                            }
                                                                            if (host == null || !host.IsReady || host.Config == null)
                                                                            { Complete(false, "Simulation host unavailable."); return; }
                                                                            if (baselineWater < 0d) baselineWater = Math.Max(1d, water);
                                                                            double signedDrift = (water - baselineWater) / baselineWater;
                                                                            float duplicationLimit = Mathf.Max(host.Config.conservationTolerance, 0.02f);
                                                                            float sinkLimit = Mathf.Max(host.Config.conservationTolerance * 5f, 0.1f);
                                                                            if (signedDrift > duplicationLimit)
                                                                            {
                                                                                Complete(false, $"Positive tracked water drift {signedDrift:P2} (duplication).");
                                                                                return;
                                                                            }
                                                                            if (signedDrift < -sinkLimit)
                                                                            {
                                                                                Complete(false, $"Tracked water sink {-signedDrift:P2} exceeds tolerance.");
                                                                                return;
                                                                            }
                                                                            Complete(true, $"Fields finite; tracked water drift {signedDrift:P2}.");
                                                                        });
                                                                    });
                                                                });
                                                            });
                                                        });
                                                    });
                                                });
                                            });
                                        });
                                    });
                                });
                            });
                        });
                    });
                });
            });
        }

        private static bool Finite(Vector4 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

        private void Complete(bool passed, string message)
        {
            pending = false;
            LastValidationPassed = passed;
            LastMessage = message;
            if (!passed) Debug.LogWarning($"GeneSys validation: {message}", this);
            ValidationCompleted?.Invoke(passed, message);
        }
    }
}
