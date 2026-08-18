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
        private int baselineMagma = -1;

        public bool LastValidationPassed { get; private set; } = true;
        public string LastMessage { get; private set; } = "Not yet validated";
        public event Action<bool, string> ValidationCompleted;

        public void Initialize(SimulationHost simulationHost) => host = simulationHost;

        public void ResetBaseline()
        {
            baselineWater = -1d;
            baselineMagma = -1;
        }

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
                for (int i = 0; i < state.Length; i++)
                {
                    Vector4 value = state[i];
                    if (!Finite(value)) { Complete(false, $"Non-finite primary field at cell {i}."); return; }
                    if (value.y < -0.01f) { Complete(false, $"Negative overpressure at cell {i}."); return; }
                }
                AsyncGPUReadback.Request(host.Resources.WaterRead, 0, waterRequest =>
                {
                    if (waterRequest.hasError) { Complete(false, "Water GPU readback failed."); return; }
                    NativeArray<Vector4> water = waterRequest.GetData<Vector4>();
                    double trackedWater = 0d;
                    for (int i = 0; i < water.Length; i++)
                    {
                        Vector4 value = water[i];
                        if (!Finite(value)) { Complete(false, $"Non-finite water field at cell {i}."); return; }
                        if (value.x < -0.001f || value.y < -0.001f || value.z < -0.001f || value.w < -0.001f)
                        { Complete(false, $"Negative water mass at cell {i}."); return; }
                        trackedWater += Math.Max(0d, value.x) + Math.Max(0d, value.y) + Math.Max(0d, value.z) + Math.Max(0d, value.w);
                    }
                    AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
                    {
                        if (materialRequest.hasError) { Complete(false, "Material GPU readback failed."); return; }
                        NativeArray<uint> materials = materialRequest.GetData<uint>();
                        int magma = 0;
                        for (int i = 0; i < materials.Length; i++)
                            if (materials[i] == 6u) magma++;
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
                            if (baselineWater < 0d) baselineWater = Math.Max(1d, trackedWater);
                            if (baselineMagma < 0) baselineMagma = magma;
                            double drift = Math.Abs(trackedWater - baselineWater) / baselineWater;
                            bool waterPass = drift <= Math.Max(host.Config.conservationTolerance, 0.05f);
                            int magmaGrowth = magma - baselineMagma;
                            bool magmaPass = magmaGrowth <= Math.Max(64, baselineMagma + host.Resources.Grid.angularResolution);
                            bool passed = waterPass && magmaPass;
                            string message = passed
                                ? $"Fields finite; water drift {drift:P2}; magma {magma}."
                                : !waterPass
                                    ? $"Tracked water drift {drift:P2} exceeds tolerance."
                                    : $"Magma cell count grew from {baselineMagma} to {magma}.";
                            Complete(passed, message);
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
