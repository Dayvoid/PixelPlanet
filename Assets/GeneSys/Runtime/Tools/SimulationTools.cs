using System;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.UI;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace GeneSys.Tools
{
    public enum BrushMode { Material, Heat, Water, Pressure, Vapor }

    public sealed class SimulationTools : MonoBehaviour
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private PlanetoidDisplayRenderer display;
        [SerializeField] private SimulationUIController ui;

        public BrushMode Mode { get; set; }
        public uint SelectedMaterialId { get; set; } = MaterialIds.Soil;
        public int Radius { get; set; } = 5;
        public float Strength { get; set; } = 1f;
        public event Action<CellInspection> Inspected;

        private bool readbackPending;

        private void Update()
        {
            if (host == null || !host.IsReady || Mouse.current == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            if (ui != null && ui.IsPointerOverUi(pointer)) return;

            if (Mouse.current.leftButton.isPressed && display.TryScreenToCell(pointer, out Vector2Int cell))
            {
                Vector4 values = Mode switch
                {
                    BrushMode.Heat => new Vector4(1f, Strength, 0f, 0f),
                    BrushMode.Water => new Vector4(2f, Strength, 0f, 0f),
                    BrushMode.Pressure => new Vector4(3f, Strength, 0f, 0f),
                    BrushMode.Vapor => new Vector4(6f, Strength, 0f, 0f),
                    _ => Vector4.zero
                };
                host.QueueBrush(new GpuPassScheduler.BrushCommand
                {
                    center = cell,
                    radius = Mathf.Max(1, Radius),
                    materialId = SelectedMaterialId,
                    values = values
                });
            }

            if (Mouse.current.rightButton.wasPressedThisFrame && !readbackPending && display.TryScreenToCell(pointer, out Vector2Int inspectCell))
                Inspect(inspectCell);
        }

        private void Inspect(Vector2Int cell)
        {
            readbackPending = true;
            var inspection = new CellInspection { cell = cell };
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, cell.x, 1, cell.y, 1, 0, 1, request =>
            {
                if (!request.hasError)
                {
                    NativeArray<uint> data = request.GetData<uint>();
                    if (data.Length > 0) inspection.materialId = data[0];
                }
                AsyncGPUReadback.Request(host.Resources.StateRead, 0, cell.x, 1, cell.y, 1, 0, 1, stateRequest =>
                {
                    if (!stateRequest.hasError)
                    {
                        NativeArray<Vector4> data = stateRequest.GetData<Vector4>();
                        if (data.Length > 0) inspection.state = data[0];
                    }
                    AsyncGPUReadback.Request(host.Resources.AuxRead, 0, cell.x, 1, cell.y, 1, 0, 1, auxRequest =>
                    {
                        if (!auxRequest.hasError)
                        {
                            NativeArray<Vector4> data = auxRequest.GetData<Vector4>();
                            if (data.Length > 0) inspection.aux = data[0];
                        }
                        AsyncGPUReadback.Request(host.Resources.FlowRead, 0, cell.x, 1, cell.y, 1, 0, 1, flowRequest =>
                        {
                            if (!flowRequest.hasError)
                            {
                                NativeArray<Vector2> data = flowRequest.GetData<Vector2>();
                                if (data.Length > 0) inspection.flow = data[0];
                            }
                            AsyncGPUReadback.Request(host.Resources.EcologyRead, 0, cell.x, 1, cell.y, 1, 0, 1, ecologyRequest =>
                            {
                                if (!ecologyRequest.hasError)
                                {
                                    NativeArray<Vector4> data = ecologyRequest.GetData<Vector4>();
                                    if (data.Length > 0) inspection.ecology = data[0];
                                }
                                readbackPending = false;
                                Inspected?.Invoke(inspection);
                            });
                        });
                    });
                });
            });
        }
    }

    [Serializable]
    public struct CellInspection
    {
        public Vector2Int cell;
        public uint materialId;
        public Vector4 state;
        public Vector4 aux;
        public Vector4 ecology;
        public Vector2 flow;
    }
}
