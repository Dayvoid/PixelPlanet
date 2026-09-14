using System;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Simulation.Climate;
using GeneSys.Simulation.Gpu;
using GeneSys.UI;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace GeneSys.Tools
{
    public enum BrushMode { Off, Material, Heat, Water, Pressure, Humidity, Ignite, Life }

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

        public void RequestInspection(Vector2Int cell, Action<CellInspection> completed)
        {
            if (host == null || !host.IsReady || host.Resources == null)
            {
                completed?.Invoke(new CellInspection { cell = cell });
                return;
            }

            if (completed != null)
            {
                void OneShot(CellInspection inspection)
                {
                    Inspected -= OneShot;
                    completed(inspection);
                }

                Inspected += OneShot;
            }

            Inspect(cell);
        }

        private void OnDisable()
        {
            readbackPending = false;
        }

        private void Update()
        {
            if (host == null || !host.IsReady || Mouse.current == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            if (ui != null && ui.IsPointerOverUi(pointer)) return;

            if (Mode != BrushMode.Off && Mouse.current.leftButton.isPressed && display.TryScreenToCell(pointer, out Vector2Int cell))
            {
                if (TryBuildBrushCommand(Mode, SelectedMaterialId, cell, Mathf.Max(1, Radius), Strength, out var command, out bool grassSeed))
                {
                    if (SelectedMaterialId == BrushSelectionIds.TreeSprouts) host.QueueTreeSprout(command.center, command.radius);
                    else if (grassSeed) host.QueueGrassSeed(command.center, command.radius);
                    else if (SelectedMaterialId == MaterialIds.Algae) host.QueueFloraPaint(command.center, command.radius, FloraGenome.ArchetypeAlgae);
                    else host.QueueBrush(command);
                }
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

        public static bool TryBuildBrushCommand(
            BrushMode mode,
            uint selectedMaterialId,
            Vector2Int cell,
            int radius,
            float strength,
            out GpuPassScheduler.BrushCommand command,
            out bool grassSeed)
        {
            command = default;
            grassSeed = false;
            if (mode == BrushMode.Off) return false;

            command.center = cell;
            command.radius = radius;
            command.materialId = selectedMaterialId;
            if (mode == BrushMode.Life && selectedMaterialId == BrushSelectionIds.GrassSeeds)
            {
                grassSeed = true;
                command.materialId = MaterialIds.Soil;
                command.values = Vector4.zero;
                return true;
            }
            if (mode == BrushMode.Life && selectedMaterialId == BrushSelectionIds.TreeSprouts)
            {
                command.materialId = MaterialIds.Soil;
                command.values = Vector4.zero;
                return true;
            }

            command.values = mode switch
            {
                BrushMode.Heat => new Vector4(1f, strength, 0f, 0f),
                BrushMode.Water => new Vector4(2f, strength, 0f, 0f),
                BrushMode.Pressure => new Vector4(3f, strength, 0f, 0f),
                BrushMode.Humidity => new Vector4(6f, strength, 0f, 0f),
                BrushMode.Ignite => new Vector4(9f, strength, 0f, 0f),
                BrushMode.Life when selectedMaterialId == MaterialIds.Cricket || selectedMaterialId == MaterialIds.CricketEgg
                    || selectedMaterialId == MaterialIds.Wasp || selectedMaterialId == MaterialIds.WaspEgg => Vector4.zero,
                BrushMode.Life when selectedMaterialId == BrushSelectionIds.MycoSpores =>
                    new Vector4(7f, strength, 0f, BrushSelectionIds.MycoRandomTraitSentinel),
                BrushMode.Life => new Vector4(14f, strength, 0f, 0f),
                _ => Vector4.zero
            };
            return true;
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
            if (host.Resources.GrassRead == null || host.Resources.GrassRead.volumeDepth < GrassGenome.GrassSliceCount)
            {
                ReadWaspSlices(host, cell, inspection);
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
                ReadWaspSlices(host, cell, inspection);
            }
        }

        private void ReadWaspSlices(SimulationHost host, Vector2Int cell, CellInspection inspection)
        {
            inspection.waspCargo = new FaunaGenome.Packed[WaspGenome.CargoSlots];
            if (host.Resources.WaspRead == null)
            {
                ReadTreeSlices(host, cell, inspection);
                return;
            }

            int remaining = 3 + WaspGenome.CargoSlots;
            int vitalsSlice = host.Resources.FaunaRead != null && host.Resources.FaunaRead.volumeDepth >= FaunaGenome.FaunaSliceCount
                ? FaunaGenome.WaspVitalsSlice : 0;
            int motionSlice = host.Resources.FaunaRead != null && host.Resources.FaunaRead.volumeDepth >= FaunaGenome.FaunaSliceCount
                ? FaunaGenome.WaspMotionSlice : 1;
            int genomeSlice = host.Resources.FaunaRead != null && host.Resources.FaunaRead.volumeDepth >= FaunaGenome.FaunaSliceCount
                ? FaunaGenome.WaspGenomeSlice : 2;
            int cargoBaseSlice = host.Resources.FaunaRead != null && host.Resources.FaunaRead.volumeDepth >= FaunaGenome.FaunaSliceCount
                ? FaunaGenome.WaspCargoSlice : WaspGenome.CargoSlice;

            AsyncGPUReadback.Request(host.Resources.WaspRead, 0, cell.x, 1, cell.y, 1, vitalsSlice, 1, request =>
            {
                if (!request.hasError)
                {
                    NativeArray<Vector4> data = request.GetData<Vector4>();
                    if (data.Length > 0) inspection.waspVitals = data[0];
                }
                CompleteWasp();
            });
            AsyncGPUReadback.Request(host.Resources.WaspRead, 0, cell.x, 1, cell.y, 1, motionSlice, 1, request =>
            {
                if (!request.hasError)
                {
                    NativeArray<Vector4> data = request.GetData<Vector4>();
                    if (data.Length > 0) inspection.waspMotion = data[0];
                }
                CompleteWasp();
            });
            AsyncGPUReadback.Request(host.Resources.WaspRead, 0, cell.x, 1, cell.y, 1, genomeSlice, 1, request =>
            {
                if (!request.hasError)
                {
                    NativeArray<Vector4> data = request.GetData<Vector4>();
                    if (data.Length > 0)
                        inspection.waspGenome = WaspGenome.Sanitize(WaspGenome.FromFloatBits(data[0]));
                }
                CompleteWasp();
            });
            for (int slot = 0; slot < WaspGenome.CargoSlots; slot++)
            {
                int capture = slot;
                int slice = cargoBaseSlice + capture;
                AsyncGPUReadback.Request(host.Resources.WaspRead, 0, cell.x, 1, cell.y, 1, slice, 1, request =>
                {
                    if (!request.hasError)
                    {
                        NativeArray<Vector4> data = request.GetData<Vector4>();
                        if (data.Length > 0) inspection.waspCargo[capture] = WaspGenome.FromFloatBits(data[0]);
                    }
                    CompleteWasp();
                });
            }

            void CompleteWasp()
            {
                remaining--;
                if (remaining > 0) return;
                ReadTreeSlices(host, cell, inspection);
            }
        }

        private void ReadTreeSlices(SimulationHost host, Vector2Int cell, CellInspection inspection)
        {
            if (host.Resources.TreeRead == null)
            {
                ReadClimate(host, cell, inspection);
                return;
            }

            int topoSlice = host.Resources.FloraRead != null && host.Resources.FloraRead.volumeDepth >= FloraGenome.SliceCount
                ? FloraGenome.TopologySlice : TreeGenome.TopologySlice;
            int genSlice = host.Resources.FloraRead != null && host.Resources.FloraRead.volumeDepth >= FloraGenome.SliceCount
                ? FloraGenome.GenomeSlice : TreeGenome.GenomeSlice;

            int remaining = 3;
            AsyncGPUReadback.Request(host.Resources.TreeRead, 0, cell.x, 1, cell.y, 1, FloraGenome.PhysiologySlice, 1, request =>
            {
                if (!request.hasError)
                {
                    NativeArray<Vector4> data = request.GetData<Vector4>();
                    if (data.Length > 0) inspection.treePhysiology = data[0];
                }
                CompleteTree();
            });
            AsyncGPUReadback.Request(host.Resources.TreeRead, 0, cell.x, 1, cell.y, 1, topoSlice, 1, request =>
            {
                if (!request.hasError)
                {
                    NativeArray<Vector4> data = request.GetData<Vector4>();
                    if (data.Length > 0) inspection.treeTopology = TreeGenome.FromFloatBits(data[0]);
                }
                CompleteTree();
            });
            AsyncGPUReadback.Request(host.Resources.TreeRead, 0, cell.x, 1, cell.y, 1, genSlice, 1, request =>
            {
                if (!request.hasError)
                {
                    NativeArray<Vector4> data = request.GetData<Vector4>();
                    if (data.Length > 0)
                        inspection.treeGenome = TreeGenome.Sanitize(TreeGenome.FromFloatBits(data[0]));
                }
                CompleteTree();
            });

            void CompleteTree()
            {
                remaining--;
                if (remaining > 0) return;
                ReadClimate(host, cell, inspection);
            }
        }

        private void ReadClimate(SimulationHost host, Vector2Int cell, CellInspection inspection)
        {
            int bins = host.Config != null ? ClimateGrid.ClampBinCount(host.Config.climateBinCount) : ClimateGrid.DefaultBins;
            int width = host.Grid.angularResolution;
            int bin = ClimateGrid.BinOf(cell.x, width, bins);
            inspection.climateBin = bin;
            if (host.Resources.ClimateState == null)
            {
                readbackPending = false;
                Inspected?.Invoke(inspection);
                return;
            }

            AsyncGPUReadback.Request(host.Resources.ClimateState, request =>
            {
                if (!request.hasError)
                {
                    NativeArray<Vector4> data = request.GetData<Vector4>();
                    int index = ClimateGrid.StateIndex(bin, ClimateGrid.StateMemory);
                    if (index + 2 < data.Length)
                    {
                        inspection.climateMemory = data[index];
                        inspection.climateLand = data[index + 1];
                        inspection.climateDiag = data[index + 2];
                    }
                }
                readbackPending = false;
                Inspected?.Invoke(inspection);
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
        public Vector4 combustion;
        public Vector4 storm;
        public Vector4 life;
        public FloraGenome.Packed genome;
        public Vector4 faunaVitals;
        public FaunaGenome.Packed faunaGenome;
        public Vector4 waspVitals;
        public Vector4 waspMotion;
        public FaunaGenome.Packed waspGenome;
        public FaunaGenome.Packed[] waspCargo;
        public Vector2 acoustic;
        public float light;
        public Vector2 flow;
        public Vector4[] grassLife;
        public Vector4[] grassTiming;
        public GrassGenome.Packed[] grassGenomes;
        public GrassGenome.Packed[] grassDonors;
        public Vector4 treePhysiology;
        public TreeGenome.Packed treeTopology;
        public TreeGenome.Packed treeGenome;
        public int climateBin;
        public Vector4 climateMemory;
        public Vector4 climateLand;
        public Vector4 climateDiag;
    }
}
