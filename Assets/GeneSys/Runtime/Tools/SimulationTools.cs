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
    public enum BrushMode { Off, Material, Heat, Water, Pressure, Vapor, Ignite, Life }

    public sealed class SimulationTools : MonoBehaviour
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private PlanetoidDisplayRenderer display;
        [SerializeField] private SimulationUIController ui;

        public BrushMode Mode { get; set; } = BrushMode.Off;
        public uint SelectedMaterialId { get; set; } = MaterialIds.Soil;
        public int Radius { get; set; } = 5;
        public float Strength { get; set; } = 1f;
        public event Action<CellInspection> Inspected;

        private bool readbackPending;
        private float inspectPollTimer;
        private const float InspectPollInterval = 0.4f;

        private void Update()
        {
            if (host == null || !host.IsReady || Mouse.current == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            if (ui != null && ui.IsPointerOverUi(pointer)) return;

            if (Mode != BrushMode.Off && Mouse.current.leftButton.isPressed && display.TryScreenToCell(pointer, out Vector2Int cell))
            {
                Vector4 values = Mode switch
                {
                    BrushMode.Heat => new Vector4(1f, Strength, 0f, 0f),
                    BrushMode.Water => new Vector4(2f, Strength, 0f, 0f),
                    BrushMode.Pressure => new Vector4(3f, Strength, 0f, 0f),
                    BrushMode.Vapor => new Vector4(6f, Strength, 0f, 0f),
                    BrushMode.Ignite => new Vector4(9f, Strength, 0f, 0f),
                    BrushMode.Life when SelectedMaterialId == MaterialIds.Cricket || SelectedMaterialId == MaterialIds.CricketEgg => Vector4.zero,
                    BrushMode.Life => new Vector4(14f, Strength, 0f, 0f),
                    _ => Vector4.zero
                };
                host.QueueBrush(new GpuPassScheduler.BrushCommand
                {
                    center = cell,
                    radius = Mathf.Max(1, Radius),
                    materialId = Mode == BrushMode.Life && (SelectedMaterialId == MaterialIds.Cricket || SelectedMaterialId == MaterialIds.CricketEgg)
                        ? SelectedMaterialId
                        : SelectedMaterialId,
                    values = values
                });
            }

            if (Mouse.current.rightButton.isPressed)
            {
                inspectPollTimer -= Time.deltaTime;
                if (!readbackPending && inspectPollTimer <= 0f && display.TryScreenToCell(pointer, out Vector2Int inspectCell))
                {
                    inspectPollTimer = InspectPollInterval;
                    Inspect(inspectCell);
                }
            }
            else
            {
                inspectPollTimer = 0f;
            }
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
                                AsyncGPUReadback.Request(host.Resources.CombustionRead, 0, cell.x, 1, cell.y, 1, 0, 1, combustionRequest =>
                                {
                                    if (!combustionRequest.hasError)
                                    {
                                        NativeArray<Vector4> data = combustionRequest.GetData<Vector4>();
                                        if (data.Length > 0) inspection.combustion = data[0];
                                    }
                                        AsyncGPUReadback.Request(host.Resources.StormRead, 0, cell.x, 1, cell.y, 1, 0, 1, stormRequest =>
                                        {
                                            if (!stormRequest.hasError)
                                            {
                                                NativeArray<Vector4> data = stormRequest.GetData<Vector4>();
                                                if (data.Length > 0) inspection.storm = data[0];
                                            }
                                            AsyncGPUReadback.Request(host.Resources.LifeGenomeRead, 0, cell.x, 1, cell.y, 1, 0, 1, lifeRequest =>
                                            {
                                                if (!lifeRequest.hasError)
                                                {
                                                    NativeArray<Vector4> data = lifeRequest.GetData<Vector4>();
                                                    if (data.Length > 0) inspection.life = data[0];
                                                }
                                                AsyncGPUReadback.Request(host.Resources.LifeGenomeRead, 0, cell.x, 1, cell.y, 1, 1, 1, genomeRequest =>
                                                {
                                                    if (!genomeRequest.hasError)
                                                    {
                                                        NativeArray<Vector4> data = genomeRequest.GetData<Vector4>();
                                                        if (data.Length > 0)
                                                            inspection.genome = FloraGenome.Sanitize(FloraGenome.FromFloatBits(data[0]));
                                                    }
                                                    AsyncGPUReadback.Request(host.Resources.LightField, 0, cell.x, 1, cell.y, 1, 0, 1, lightRequest =>
                                                    {
                                                        if (!lightRequest.hasError)
                                                        {
                                                            NativeArray<float> data = lightRequest.GetData<float>();
                                                            if (data.Length > 0) inspection.light = data[0];
                                                        }
                                                        AsyncGPUReadback.Request(host.Resources.FaunaRead, 0, cell.x, 1, cell.y, 1, 0, 1, faunaRequest =>
                                                        {
                                                            if (!faunaRequest.hasError)
                                                            {
                                                                NativeArray<Vector4> data = faunaRequest.GetData<Vector4>();
                                                                if (data.Length > 0) inspection.faunaVitals = data[0];
                                                            }
                                                            AsyncGPUReadback.Request(host.Resources.FaunaRead, 0, cell.x, 1, cell.y, 1, 2, 1, faunaGenomeRequest =>
                                                            {
                                                                if (!faunaGenomeRequest.hasError)
                                                                {
                                                                    NativeArray<Vector4> data = faunaGenomeRequest.GetData<Vector4>();
                                                                    if (data.Length > 0)
                                                                        inspection.faunaGenome = FaunaGenome.Sanitize(FaunaGenome.FromFloatBits(data[0]));
                                                                }
                                                                AsyncGPUReadback.Request(host.Resources.AcousticRead, 0, cell.x, 1, cell.y, 1, 0, 1, acousticRequest =>
                                                                {
                                                                    if (!acousticRequest.hasError)
                                                                    {
                                                                        NativeArray<Vector2> data = acousticRequest.GetData<Vector2>();
                                                                        if (data.Length > 0) inspection.acoustic = data[0];
                                                                    }
                                                                    ReadGrassSlots(host, cell, inspection);
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

        private void ReadGrassSlots(SimulationHost host, Vector2Int cell, CellInspection inspection)
        {
            inspection.grassLife = new Vector4[GrassGenome.SlotCount];
            inspection.grassTiming = new Vector4[GrassGenome.SlotCount];
            inspection.grassGenomes = new GrassGenome.Packed[GrassGenome.SlotCount];
            inspection.grassDonors = new GrassGenome.Packed[GrassGenome.SlotCount];
            if (host.Resources.GrassRead == null)
            {
                readbackPending = false;
                Inspected?.Invoke(inspection);
                return;
            }

            int remaining = GrassGenome.SlotCount * 4;
            for (int slot = 0; slot < GrassGenome.SlotCount; slot++)
            {
                int capture = slot;
                int lifeSlice = GrassGenome.Slice(capture, GrassGenome.LifeOffset);
                int genomeSlice = GrassGenome.Slice(capture, GrassGenome.GenomeOffset);
                int timingSlice = GrassGenome.Slice(capture, GrassGenome.TimingOffset);
                int donorSlice = GrassGenome.Slice(capture, GrassGenome.DonorOffset);
                AsyncGPUReadback.Request(host.Resources.GrassRead, 0, cell.x, 1, cell.y, 1, lifeSlice, 1, request =>
                {
                    if (!request.hasError)
                    {
                        NativeArray<Vector4> data = request.GetData<Vector4>();
                        if (data.Length > 0) inspection.grassLife[capture] = data[0];
                    }
                    CompleteGrass();
                });
                AsyncGPUReadback.Request(host.Resources.GrassRead, 0, cell.x, 1, cell.y, 1, genomeSlice, 1, request =>
                {
                    if (!request.hasError)
                    {
                        NativeArray<Vector4> data = request.GetData<Vector4>();
                        if (data.Length > 0)
                            inspection.grassGenomes[capture] = GrassGenome.Sanitize(GrassGenome.FromFloatBits(data[0]));
                    }
                    CompleteGrass();
                });
                AsyncGPUReadback.Request(host.Resources.GrassRead, 0, cell.x, 1, cell.y, 1, timingSlice, 1, request =>
                {
                    if (!request.hasError)
                    {
                        NativeArray<Vector4> data = request.GetData<Vector4>();
                        if (data.Length > 0) inspection.grassTiming[capture] = data[0];
                    }
                    CompleteGrass();
                });
                AsyncGPUReadback.Request(host.Resources.GrassRead, 0, cell.x, 1, cell.y, 1, donorSlice, 1, request =>
                {
                    if (!request.hasError)
                    {
                        NativeArray<Vector4> data = request.GetData<Vector4>();
                        if (data.Length > 0)
                            inspection.grassDonors[capture] = GrassGenome.FromFloatBits(data[0]);
                    }
                    CompleteGrass();
                });
            }

            void CompleteGrass()
            {
                remaining--;
                if (remaining > 0) return;
                readbackPending = false;
                Inspected?.Invoke(inspection);
            }
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
        public Vector4 combustion;
        public Vector4 storm;
        public Vector4 life;
        public FloraGenome.Packed genome;
        public Vector4 faunaVitals;
        public FaunaGenome.Packed faunaGenome;
        public Vector2 acoustic;
        public float light;
        public Vector2 flow;
        public Vector4[] grassLife;
        public Vector4[] grassTiming;
        public GrassGenome.Packed[] grassGenomes;
        public GrassGenome.Packed[] grassDonors;
    }
}
