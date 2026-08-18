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
            if (host == null || !host.IsReady || pending) return;
            pending = true;
            AsyncGPUReadback.Request(host.Resources.StateRead, 0, stateRequest =>
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
                AsyncGPUReadback.Request(host.Resources.AuxRead, 0, auxRequest =>
                {
                    if (auxRequest.hasError) { Complete(false, "Auxiliary GPU readback failed."); return; }
                    if (host == null || !host.IsReady) { Complete(false, "Simulation host unavailable."); return; }
                    NativeArray<Vector4> aux = auxRequest.GetData<Vector4>();
                    for (int i = 0; i < aux.Length; i++)
                    {
                        Vector4 value = aux[i];
                        if (!Finite(value)) { Complete(false, $"Non-finite auxiliary field at cell {i}."); return; }
                        if (value.x < -0.001f || value.y < -0.001f) { Complete(false, $"Negative vapor/groundwater at cell {i}."); return; }
                        water += Math.Max(0d, value.x) + Math.Max(0d, value.y);
                    }
                    AsyncGPUReadback.Request(host.Resources.FlowRead, 0, flowRequest =>
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
                        if (baselineWater < 0d) baselineWater = Math.Max(1d, water);
                        double drift = Math.Abs(water - baselineWater) / baselineWater;
                        bool passed = drift <= Math.Max(host.Config.conservationTolerance * 5f, 0.1f);
                        Complete(passed, passed ? $"Fields finite; tracked water drift {drift:P2}." : $"Tracked water drift {drift:P2} exceeds tolerance.");
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
