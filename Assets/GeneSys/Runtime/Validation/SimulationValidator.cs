using System;
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

        public void ResetBaseline() => baselineWater = -1d;

        private void Update()
        {
            if (host == null || !host.IsReady || pending) return;
            long tick = host.Clock.TickCount;
            if (tick == 0 || tick == lastValidatedTick || tick % Mathf.Max(1, host.Config.validationIntervalTicks) != 0) return;
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
                                        if (host == null || !host.IsReady || host.Config == null)
                                        { Complete(false, "Simulation host unavailable."); return; }
                                        if (baselineWater < 0d) baselineWater = Math.Max(1d, water);
                                        double drift = Math.Abs(water - baselineWater) / baselineWater;
                                        bool passed = drift <= Math.Max(host.Config.conservationTolerance * 5f, 0.1f);
                                        Complete(passed, passed ? $"Fields finite; tracked water drift {drift:P2}." : $"Tracked water drift {drift:P2} exceeds tolerance.");
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
