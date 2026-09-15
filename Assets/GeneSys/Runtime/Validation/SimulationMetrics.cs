using System;
using System.Collections.Generic;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;

namespace GeneSys.Validation
{
    public struct WorldWaterMetrics
    {
        public int AngularResolution;
        public int RadialResolution;
        public float OceanCoverage;
        public int BasinCount;
        public double SurfaceWaterMass;
        public double GroundwaterMass;
        public double VaporMass;
        public double TotalTrackedWaterMass;
        public float MeanTemperature;
        public float MeanPressure;
        public float MeanMoisture;
        public float MeanRelativeHumidity;
        public float CloudCover;
        public double TranspirationMass;
        public float MeanWindSpeed;
        public float MaxAtmosphericTemperature;
        public int MaxAtmosphericTemperatureX;
        public int MaxAtmosphericTemperatureY;
        public float MaxVaporMass;
        public int MaxVaporX;
        public int MaxVaporY;
        public int BurningCellCount;
        public float TotalFireIntensity;
        public float MeanOxygen;
        public double SootMass;
        public int OrganismCount;
    }

    public struct AtmosphericCirculationMetrics
    {
        public double CloudMass;
        public double VaporMass;
        public float VaporAltitudeMean;
        public float VaporAltitudeVariance;
        public float MeanVerticalFlow;
        public float RmsVerticalFlow;
        public float SignedMeanVerticalDrift;
        public float PressureAnomalyRms;
        public float DayNightSurfaceTemperatureDelta;
        public float CirculationEnergy;
        public int UpdraftCells;
        public int DowndraftCells;
        public float MaxAtmosphericTemperature;
        public int MaxAtmosphericTemperatureX;
        public int MaxAtmosphericTemperatureY;
        public float MaxVaporMass;
        public int MaxVaporX;
        public int MaxVaporY;
    }

    public struct FloraMetrics
    {
        public int LivingCount;
        public int DormantCount;
        public int DesiccatedCount;
        public int SporeCarrierCount;
        public double TotalBiomass;
        public float MeanEnergy;
        public float MeanGeneration;
        public float[] GeneMeans;
        public float[] GeneVariance;
    }

    public struct FaunaMetrics
    {
        public int AdultCount;
        public int JuvenileCount;
        public int EggCount;
        public double TotalCalories;
        public double TotalHydration;
        public float MeanAge;
        public float MeanGeneration;
    }

    public struct WaspMetrics
    {
        public int AdultCount;
        public int JuvenileCount;
        public int EggCount;
        public int PollenCarrierCount;
        public int PollenSampleCount;
        public double TotalCalories;
        public double TotalHydration;
        public float MeanAge;
        public float MeanGeneration;
    }

    public struct TreeMetrics
    {
        public int AnchorCount;
        public int PixelCount;
        public int SproutCount;
        public int SaplingCount;
        public int TreeCount;
        public int DeadCount;
        public double TotalEnergy;
        public double TotalHydration;
        public double TotalHealth;
    }

    public struct WorldGeodynamicsMetrics
    {
        public float MeanStrain;
        public float MaxStrain;
        public float MeanOverpressure;
        public float MaxOverpressure;
        public float MeanFaultWeakness;
        public int ActiveEventCount;
        public float ReleasedEnergy;
        public float AffectedAngularFraction;
        public int KinematicCellsMoved;
        public bool HasNonFinite;
    }

    public struct SimulationStatusSnapshot
    {
        public int AngularResolution;
        public int RadialResolution;

        public float OceanCoverage;
        public int BasinCount;
        public double SurfaceWaterMass;
        public double GroundwaterMass;
        public double VaporMass;
        public double TotalTrackedWaterMass;

        public float MeanTemperature;
        public float MeanPressure;
        public float MeanMoisture;
        public float MeanRelativeHumidity;
        public float CloudCover;
        public float MeanWindSpeed;
        public int BurningCellCount;
        public float TotalFireIntensity;
        public float MeanOxygen;
        public double SootMass;

        public float MeanStrain;
        public float MaxStrain;
        public float MeanOverpressure;
        public float MaxOverpressure;
        public float MeanFaultWeakness;
        public int ActiveGeoEvents;
        public float ReleasedGeoEnergy;
        public float AffectedGeoArcFraction;
        public int KinematicCellsMoved;

        public int TotalOrganisms;
        public int AlgaeCount;
        public int CricketAdults;
        public int CricketEggs;
        public int WaspAdults;
        public int WaspEggs;
        public int TreeAnchors;
        public int TreeTotalPixels;

        public string Format()
        {
            return
                $"Grid {AngularResolution}×{RadialResolution} | Ocean {OceanCoverage * 100f:F1}% ({BasinCount} basins)\n" +
                $"Water  surf {SurfaceWaterMass:F1}  ground {GroundwaterMass:F1}  vapor {VaporMass:F1}  total {TotalTrackedWaterMass:F1}\n" +
                $"Geo  strain {MeanStrain:F3} (peak {MaxStrain:F3})  melt P {MeanOverpressure:F3} (peak {MaxOverpressure:F3})  events {ActiveGeoEvents} (E: {ReleasedGeoEnergy:F1})  moved {KinematicCellsMoved}\n" +
                $"Atmo  T {MeanTemperature:F1}°  P {MeanPressure:F3}  RH {MeanRelativeHumidity * 100f:F0}%  cloud {CloudCover * 100f:F0}%  wind {MeanWindSpeed:F3}  fire {BurningCellCount} (O2 {MeanOxygen:F2})\n" +
                $"Life  algae {AlgaeCount}  cricket {CricketAdults} ({CricketEggs} egg)  wasp {WaspAdults} ({WaspEggs} egg)  trees {TreeAnchors} ({TreeTotalPixels} px)";
        }
    }

    public static class SimulationMetrics
    {
        public const int DefaultStatusSampleStride = 4;

        public static void MeasureStatusSampledAsync(SimulationHost host, Action<SimulationStatusSnapshot> completed, int sampleStride = DefaultStatusSampleStride)
        {
            if (host == null || !host.IsReady || host.Resources == null)
            {
                completed?.Invoke(default);
                return;
            }

            PolarGridDefinition grid = host.Grid;
            int width = grid.angularResolution;
            int height = grid.radialResolution;
            if (width <= 0 || height <= 0)
            {
                completed?.Invoke(default);
                return;
            }

            RenderTexture materialTex = host.Resources.MaterialRead;
            RenderTexture stateTex = host.Resources.StateRead;
            RenderTexture auxTex = host.Resources.AuxRead;
            RenderTexture flowTex = host.Resources.FlowRead;
            RenderTexture combustionTex = host.Resources.CombustionRead;
            RenderTexture treeTex = host.Resources.TreeRead;
            ComputeBuffer geoState = host.Resources.GeodynamicsStateRead;
            ComputeBuffer geoEvents = host.Resources.GeodynamicsEvents;

            if (materialTex == null || stateTex == null || auxTex == null || flowTex == null || combustionTex == null)
            {
                completed?.Invoke(default);
                return;
            }

            sampleStride = Mathf.Clamp(sampleStride, 1, 16);
            float vaporScale = host.Config != null ? host.Config.vaporCapacityScale : 0.01f;

            var snapshot = new SimulationStatusSnapshot
            {
                AngularResolution = width,
                RadialResolution = height
            };

            int pendingRequests = 5; // material, state, aux, flow, combustion
            bool hasTreeTopo = treeTex != null && treeTex.volumeDepth >= FloraGenome.SliceCount;
            if (hasTreeTopo) pendingRequests++;
            bool hasGeoState = geoState != null && (host.Config == null || host.Config.geodynamicsLayerEnable);
            if (hasGeoState) pendingRequests++;
            bool hasGeoEvents = geoEvents != null && (host.Config == null || host.Config.geodynamicsLayerEnable);
            if (hasGeoEvents) pendingRequests++;

            bool failed = false;

            void CheckDone()
            {
                if (failed) return;
                pendingRequests--;
                if (pendingRequests <= 0)
                {
                    snapshot.TotalTrackedWaterMass = snapshot.SurfaceWaterMass + snapshot.GroundwaterMass + snapshot.VaporMass;
                    snapshot.TotalOrganisms = snapshot.AlgaeCount + snapshot.CricketAdults + snapshot.CricketEggs + snapshot.WaspAdults + snapshot.WaspEggs + snapshot.TreeTotalPixels;
                    try
                    {
                        completed?.Invoke(snapshot);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }

            void OnFailed()
            {
                if (failed) return;
                failed = true;
                try
                {
                    completed?.Invoke(default);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            // 1. Material (surface ocean & exact organism counts)
            try
            {
                AsyncGPUReadback.Request(materialTex, 0, req =>
                {
                    if (req.hasError || failed) { OnFailed(); return; }
                    NativeArray<uint> materials = req.GetData<uint>();
                    int cellCount = materials.Length;

                    int sampledCols = Mathf.Max(1, width / sampleStride);
                    bool[] oceanCols = new bool[sampledCols];
                    int oceanColIndex = 0;
                    for (int x = 0; x < width; x += sampleStride)
                    {
                        for (int y = height - 1; y >= 0; y--)
                        {
                            int idx = y * width + x;
                            if (idx >= cellCount) break;
                            uint m = materials[idx];
                            if (m == MaterialIds.Air || m == MaterialIds.Void) continue;
                            if (m == MaterialIds.Water || m == MaterialIds.Ice)
                                oceanCols[oceanColIndex] = true;
                            break;
                        }
                        oceanColIndex++;
                        if (oceanColIndex >= sampledCols) break;
                    }
                    int oceanCount = 0;
                    for (int i = 0; i < sampledCols; i++)
                        if (oceanCols[i]) oceanCount++;
                    snapshot.OceanCoverage = oceanCount / (float)sampledCols;
                    snapshot.BasinCount = CountOceanAngleBasins(oceanCols);

                    int algae = 0, crickets = 0, cricketEggs = 0, wasps = 0, waspEggs = 0, treePx = 0;
                    for (int i = 0; i < cellCount; i++)
                    {
                        uint m = materials[i];
                        if (m == MaterialIds.Algae) algae++;
                        else if (m == MaterialIds.Cricket) crickets++;
                        else if (m == MaterialIds.CricketEgg) cricketEggs++;
                        else if (m == MaterialIds.Wasp) wasps++;
                        else if (m == MaterialIds.WaspEgg) waspEggs++;
                        else if (m == MaterialIds.Wood || m == MaterialIds.Leaf) treePx++;
                    }
                    snapshot.AlgaeCount = algae;
                    snapshot.CricketAdults = crickets;
                    snapshot.CricketEggs = cricketEggs;
                    snapshot.WaspAdults = wasps;
                    snapshot.WaspEggs = waspEggs;
                    snapshot.TreeTotalPixels = treePx;

                    CheckDone();
                });
            }
            catch { OnFailed(); }

            // 2. State (sampled temperature, pressure, moisture, surface water, cloud)
            try
            {
                AsyncGPUReadback.Request(stateTex, 0, req =>
                {
                    if (req.hasError || failed) { OnFailed(); return; }
                    NativeArray<Vector4> states = req.GetData<Vector4>();
                    int cellCount = states.Length;
                    int areaFactor = sampleStride * sampleStride;
                    double tempSum = 0d;
                    double presSum = 0d;
                    double moistureSum = 0d;
                    double surfWaterSum = 0d;
                    int atmoCells = 0;
                    int cloudCells = 0;
                    int sampledCount = 0;

                    float atmoStart = grid.atmosphereStartRadius;
                    for (int y = 0; y < height; y += sampleStride)
                    {
                        float r = grid.Radius01(y);
                        bool isAtmo = r >= atmoStart;
                        for (int x = 0; x < width; x += sampleStride)
                        {
                            int idx = y * width + x;
                            if (idx >= cellCount) continue;
                            Vector4 s = states[idx];
                            tempSum += s.x;
                            presSum += s.y;
                            moistureSum += Math.Max(0f, s.z);
                            surfWaterSum += Math.Max(0d, s.z);
                            sampledCount++;

                            if (isAtmo)
                            {
                                atmoCells++;
                                if (s.z > 0.05f) cloudCells++;
                            }
                        }
                    }

                    if (sampledCount > 0)
                    {
                        snapshot.MeanTemperature = (float)(tempSum / sampledCount);
                        snapshot.MeanPressure = (float)(presSum / sampledCount);
                        snapshot.MeanMoisture = (float)(moistureSum / sampledCount);
                    }
                    snapshot.SurfaceWaterMass = surfWaterSum * areaFactor;
                    snapshot.CloudCover = atmoCells > 0 ? (float)cloudCells / atmoCells : 0f;

                    CheckDone();
                });
            }
            catch { OnFailed(); }

            // 3. Aux (sampled groundwater, vapor, relative humidity)
            try
            {
                AsyncGPUReadback.Request(auxTex, 0, req =>
                {
                    if (req.hasError || failed) { OnFailed(); return; }
                    NativeArray<Vector4> aux = req.GetData<Vector4>();
                    int cellCount = aux.Length;
                    int areaFactor = sampleStride * sampleStride;
                    double vaporSum = 0d;
                    double groundSum = 0d;
                    double atmoVaporSum = 0d;
                    int atmoCells = 0;

                    float atmoStart = grid.atmosphereStartRadius;
                    for (int y = 0; y < height; y += sampleStride)
                    {
                        float r = grid.Radius01(y);
                        bool isAtmo = r >= atmoStart;
                        for (int x = 0; x < width; x += sampleStride)
                        {
                            int idx = y * width + x;
                            if (idx >= cellCount) continue;
                            Vector4 a = aux[idx];
                            vaporSum += Math.Max(0d, a.x);
                            groundSum += Math.Max(0d, a.y);

                            if (isAtmo)
                            {
                                atmoVaporSum += Math.Max(0f, a.x);
                                atmoCells++;
                            }
                        }
                    }

                    snapshot.GroundwaterMass = groundSum * areaFactor;
                    snapshot.VaporMass = vaporSum * areaFactor;
                    float meanVapor = atmoCells > 0 ? (float)(atmoVaporSum / atmoCells) : 0f;
                    snapshot.MeanRelativeHumidity = RelativeHumidity(meanVapor, snapshot.MeanTemperature, vaporScale);

                    CheckDone();
                });
            }
            catch { OnFailed(); }

            // 4. Flow (sampled wind speed)
            try
            {
                AsyncGPUReadback.Request(flowTex, 0, req =>
                {
                    if (req.hasError || failed) { OnFailed(); return; }
                    NativeArray<Vector2> flow = req.GetData<Vector2>();
                    int cellCount = flow.Length;
                    double windSpeedSum = 0d;
                    int atmoCells = 0;

                    float atmoStart = grid.atmosphereStartRadius;
                    for (int y = 0; y < height; y += sampleStride)
                    {
                        float r = grid.Radius01(y);
                        if (r < atmoStart) continue;
                        for (int x = 0; x < width; x += sampleStride)
                        {
                            int idx = y * width + x;
                            if (idx >= cellCount) continue;
                            Vector2 f = flow[idx];
                            windSpeedSum += Math.Sqrt(f.x * f.x + f.y * f.y);
                            atmoCells++;
                        }
                    }

                    if (atmoCells > 0)
                        snapshot.MeanWindSpeed = (float)(windSpeedSum / atmoCells);

                    CheckDone();
                });
            }
            catch { OnFailed(); }

            // 5. Combustion (sampled fire, oxygen, soot)
            try
            {
                AsyncGPUReadback.Request(combustionTex, 0, req =>
                {
                    if (req.hasError || failed) { OnFailed(); return; }
                    NativeArray<Vector4> combustion = req.GetData<Vector4>();
                    int cellCount = combustion.Length;
                    int areaFactor = sampleStride * sampleStride;
                    int burningCount = 0;
                    double fireIntensitySum = 0d;
                    double sootSum = 0d;
                    double o2Sum = 0d;
                    int sampledCount = 0;

                    for (int y = 0; y < height; y += sampleStride)
                    {
                        for (int x = 0; x < width; x += sampleStride)
                        {
                            int idx = y * width + x;
                            if (idx >= cellCount) continue;
                            Vector4 c = combustion[idx];
                            if (c.y > 0.02f)
                            {
                                burningCount++;
                                fireIntensitySum += c.y;
                            }
                            sootSum += Math.Max(0d, c.z);
                            o2Sum += Math.Max(0f, c.x);
                            sampledCount++;
                        }
                    }

                    snapshot.BurningCellCount = burningCount * areaFactor;
                    snapshot.TotalFireIntensity = (float)(fireIntensitySum * areaFactor);
                    snapshot.SootMass = sootSum * areaFactor;
                    if (sampledCount > 0)
                        snapshot.MeanOxygen = (float)(o2Sum / sampledCount);

                    CheckDone();
                });
            }
            catch { OnFailed(); }

            // 6. Tree Topology (anchor count)
            if (hasTreeTopo)
            {
                try
                {
                    int topoSlice = FloraGenome.TopologySlice;
                    AsyncGPUReadback.Request(treeTex, 0, 0, treeTex.width, 0, treeTex.height, topoSlice, 1, req =>
                    {
                        if (req.hasError || failed) { OnFailed(); return; }
                        NativeArray<Vector4> topoBits = req.GetData<Vector4>();
                        int count = topoBits.Length;
                        int anchors = 0;
                        for (int i = 0; i < count; i++)
                        {
                            TreeGenome.Packed topo = TreeGenome.FromFloatBits(topoBits[i]);
                            if (topo.X != 0 && (TreeGenome.Flags(topo.Z) & TreeGenome.FlagAnchor) != 0)
                                anchors++;
                        }
                        snapshot.TreeAnchors = anchors;
                        CheckDone();
                    });
                }
                catch { OnFailed(); }
            }

            // 7. Geodynamics State (strain, overpressure, fault weakness)
            if (hasGeoState)
            {
                try
                {
                    AsyncGPUReadback.Request(geoState, stateReq =>
                    {
                        try
                        {
                            if (stateReq.hasError || failed) { OnFailed(); return; }
                            NativeArray<Vector4> stateValues = stateReq.GetData<Vector4>();
                            int angBins = host.Config != null ? host.Config.geodynamicsAngularBins : 64;
                            int radBins = host.Config != null ? host.Config.geodynamicsRadialBins : 16;
                            angBins = Mathf.Clamp(angBins, 16, 128);
                            radBins = Mathf.Clamp(radBins, 8, 32);
                            int counted = 0;
                            float meanStrain = 0f, maxStrain = 0f, meanOverpressure = 0f, maxOverpressure = 0f, meanWeakness = 0f;
                            for (int a = 0; a < angBins; a++)
                            {
                                for (int r = 0; r < radBins; r++)
                                {
                                    int stateIndex = ((a * radBins) + r) * 2;
                                    if (stateIndex + 1 >= stateValues.Length) continue;
                                    Vector4 reservoir = stateValues[stateIndex];
                                    Vector4 kinematics = stateValues[stateIndex + 1];
                                    meanStrain += Mathf.Max(0f, reservoir.z);
                                    maxStrain = Mathf.Max(maxStrain, reservoir.z);
                                    meanOverpressure += Mathf.Max(0f, reservoir.y);
                                    maxOverpressure = Mathf.Max(maxOverpressure, reservoir.y);
                                    meanWeakness += Mathf.Max(0f, kinematics.z);
                                    counted++;
                                }
                            }
                            if (counted > 0)
                            {
                                snapshot.MeanStrain = meanStrain / counted;
                                snapshot.MaxStrain = maxStrain;
                                snapshot.MeanOverpressure = meanOverpressure / counted;
                                snapshot.MaxOverpressure = maxOverpressure;
                                snapshot.MeanFaultWeakness = meanWeakness / counted;
                            }
                            CheckDone();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[MeasureStatusSampledAsync] GeodynamicsState error: {ex}");
                            OnFailed();
                        }
                    });
                }
                catch { OnFailed(); }
            }

            // 8. Geodynamics Events (active events, energy release, affected arc fraction)
            if (hasGeoEvents)
            {
                try
                {
                    AsyncGPUReadback.Request(geoEvents, eventReq =>
                    {
                        try
                        {
                            if (eventReq.hasError || failed) { OnFailed(); return; }
                            NativeArray<Vector4> eventValues = eventReq.GetData<Vector4>();
                            int angBins = host.Config != null ? host.Config.geodynamicsAngularBins : 64;
                            int radBins = host.Config != null ? host.Config.geodynamicsRadialBins : 16;
                            angBins = Mathf.Clamp(angBins, 16, 128);
                            radBins = Mathf.Clamp(radBins, 8, 32);
                            var activeAngles = new bool[angBins];
                            int activeEvents = 0;
                            float releasedEnergy = 0f;
                            for (int a = 0; a < angBins; a++)
                            {
                                for (int r = 0; r < radBins; r++)
                                {
                                    int eventIndex = a * radBins + r;
                                    if (eventIndex < eventValues.Length && eventValues[eventIndex].y > 0.05f)
                                    {
                                        activeEvents++;
                                        releasedEnergy += Mathf.Max(0f, eventValues[eventIndex].w);
                                        activeAngles[a] = true;
                                    }
                                }
                            }
                            snapshot.ActiveGeoEvents = activeEvents;
                            snapshot.ReleasedGeoEnergy = releasedEnergy;
                            int active = 0;
                            for (int i = 0; i < activeAngles.Length; i++)
                                if (activeAngles[i]) active++;
                            snapshot.AffectedGeoArcFraction = active / (float)Mathf.Max(1, angBins);
                            CheckDone();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[MeasureStatusSampledAsync] GeodynamicsEvents error: {ex}");
                            OnFailed();
                        }
                    });
                }
                catch { OnFailed(); }
            }
        }

        public static void MeasureAsync(SimulationHost host, Action<WorldWaterMetrics> completed)
        {
            if (!TryCaptureFields(host, out PolarGridDefinition grid, out RenderTexture materialTex, out RenderTexture stateTex, out RenderTexture auxTex, out RenderTexture flowTex, out RenderTexture combustionTex))
            {
                completed?.Invoke(default);
                return;
            }

            Action fail = () => completed?.Invoke(default);
            RequestField(materialTex, fail, materialRequest =>
            {
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                RequestField(stateTex, fail, stateRequest =>
                {
                    Vector4[] states = stateRequest.GetData<Vector4>().ToArray();
                    RequestField(auxTex, fail, auxRequest =>
                    {
                        Vector4[] aux = auxRequest.GetData<Vector4>().ToArray();
                        RequestField(flowTex, fail, flowRequest =>
                        {
                            Vector2[] flow = flowRequest.GetData<Vector2>().ToArray();
                            RequestField(combustionTex, fail, combustionRequest =>
                            {
                                Vector4[] combustion = combustionRequest.GetData<Vector4>().ToArray();
                                float vaporScale = host != null && host.Config != null ? host.Config.vaporCapacityScale : 0.01f;
                                completed?.Invoke(ComputeMetrics(grid, materials, states, aux, flow, combustion, vaporScale));
                            });
                        });
                    });
                });
            });
        }

        public static void MeasureFloraAsync(SimulationHost host, Action<FloraMetrics> completed)
        {
            if (host == null || !host.IsReady || host.Resources == null)
            {
                completed?.Invoke(default);
                return;
            }
            RenderTexture materialTex = host.Resources.MaterialRead;
            RenderTexture lifeGenomeTex = host.Resources.LifeGenomeRead;
            if (materialTex == null || lifeGenomeTex == null)
            {
                completed?.Invoke(default);
                return;
            }
            Action fail = () => completed?.Invoke(default);
            RequestField(materialTex, fail, materialRequest =>
            {
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                RequestFieldSlice(lifeGenomeTex, 0, fail, lifeRequest =>
                {
                    Vector4[] life = lifeRequest.GetData<Vector4>().ToArray();
                    RequestFieldSlice(lifeGenomeTex, 1, fail, genomeRequest =>
                    {
                        Vector4[] genomeBits = genomeRequest.GetData<Vector4>().ToArray();
                        var genomes = new FloraGenome.Packed[genomeBits.Length];
                        for (int i = 0; i < genomeBits.Length; i++)
                            genomes[i] = FloraGenome.FromFloatBits(genomeBits[i]);
                        completed?.Invoke(ComputeFloraMetrics(materials, life, genomes));
                    });
                });
            });
        }

        public static void MeasureAtmosphereAsync(SimulationHost host, float solarAngle01, Action<AtmosphericCirculationMetrics> completed)
        {
            if (!TryCaptureFields(host, out PolarGridDefinition grid, out RenderTexture materialTex, out RenderTexture stateTex, out RenderTexture auxTex, out RenderTexture flowTex, out _))
            {
                completed?.Invoke(default);
                return;
            }

            Action fail = () => completed?.Invoke(default);
            RequestField(materialTex, fail, materialRequest =>
            {
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                RequestField(stateTex, fail, stateRequest =>
                {
                    Vector4[] states = stateRequest.GetData<Vector4>().ToArray();
                    RequestField(auxTex, fail, auxRequest =>
                    {
                        Vector4[] aux = auxRequest.GetData<Vector4>().ToArray();
                        RequestField(flowTex, fail, flowRequest =>
                        {
                            Vector2[] flow = flowRequest.GetData<Vector2>().ToArray();
                            completed?.Invoke(ComputeAtmosphericMetrics(grid, materials, states, aux, flow, solarAngle01));
                        });
                    });
                });
            });
        }

        private static bool TryCaptureFields(SimulationHost host, out PolarGridDefinition grid, out RenderTexture materialTex, out RenderTexture stateTex, out RenderTexture auxTex, out RenderTexture flowTex, out RenderTexture combustionTex)
        {
            grid = default;
            materialTex = null;
            stateTex = null;
            auxTex = null;
            flowTex = null;
            combustionTex = null;
            if (host == null || !host.IsReady || host.Resources == null) return false;
            grid = host.Grid;
            materialTex = host.Resources.MaterialRead;
            stateTex = host.Resources.StateRead;
            auxTex = host.Resources.AuxRead;
            flowTex = host.Resources.FlowRead;
            combustionTex = host.Resources.CombustionRead;
            return materialTex != null && stateTex != null && auxTex != null && flowTex != null;
        }

        private static void RequestFieldSlice(RenderTexture texture, int slice, Action onFailed, Action<AsyncGPUReadbackRequest> onSuccess)
        {
            if (texture == null)
            {
                onFailed();
                return;
            }
            try
            {
                AsyncGPUReadback.Request(texture, 0, 0, texture.width, 0, texture.height, slice, 1, request =>
                {
                    if (request.hasError) onFailed();
                    else onSuccess(request);
                });
            }
            catch (Exception)
            {
                onFailed();
            }
        }

        private static void RequestField(RenderTexture texture, Action onFailed, Action<AsyncGPUReadbackRequest> onSuccess)
        {
            if (texture == null)
            {
                onFailed();
                return;
            }
            try
            {
                AsyncGPUReadback.Request(texture, 0, request =>
                {
                    if (request.hasError) onFailed();
                    else onSuccess(request);
                });
            }
            catch (Exception)
            {
                onFailed();
            }
        }

        public const float WaterMagnusA = 17.27f;
        public const float WaterMagnusB = 237.7f;

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux)
            => ComputeMetrics(grid, materials, states, aux, null, null, 0.01f);

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux, Vector2[] flow)
            => ComputeMetrics(grid, materials, states, aux, flow, null, 0.01f);

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux, Vector2[] flow, Vector4[] combustion)
            => ComputeMetrics(grid, materials, states, aux, flow, combustion, 0.01f);

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux, Vector2[] flow, Vector4[] combustion, float vaporCapacityScale)
        {
            int width = grid.angularResolution;
            int height = grid.radialResolution;
            int cellCount = Math.Min(materials.Length, Math.Min(states.Length, aux.Length));
            if (flow != null) cellCount = Math.Min(cellCount, flow.Length);
            if (combustion != null) cellCount = Math.Min(cellCount, combustion.Length);
            var metrics = new WorldWaterMetrics
            {
                AngularResolution = width,
                RadialResolution = height,
                OrganismCount = 0
            };
            bool[] oceanAngles = new bool[width];
            bool[] oceanMask = new bool[width * height];
            double temperatureSum = 0d;
            double pressureSum = 0d;
            double moistureSum = 0d;
            double relativeHumiditySum = 0d;
            int humiditySampleCount = 0;
            int cloudCells = 0;
            int atmosphereCells = 0;
            double windSpeedSum = 0d;
            double oxygenSum = 0d;
            int sampleCount = 0;
            int windSampleCount = 0;
            int oxygenSampleCount = 0;

            for (int x = 0; x < width; x++)
            {
                for (int y = height - 1; y >= 0; y--)
                {
                    int index = y * width + x;
                    if (index >= cellCount) break;
                    uint material = materials[index];
                    Vector4 state = states[index];
                    if (material == MaterialIds.Air || material == MaterialIds.Void) continue;
                    if (material == MaterialIds.Water || material == MaterialIds.Ice)
                        oceanAngles[x] = true;
                    break;
                }
            }

            for (int y = 0; y < height; y++)
            {
                float radius = grid.Radius01(y);
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (index >= cellCount) break;
                    uint material = materials[index];
                    Vector4 state = states[index];
                    Vector4 auxValue = aux[index];

                    metrics.SurfaceWaterMass += Math.Max(0d, state.z);
                    metrics.GroundwaterMass += Math.Max(0d, auxValue.y);
                    metrics.VaporMass += Math.Max(0d, auxValue.x);
                    if (material == MaterialIds.Air)
                    {
                        atmosphereCells++;
                        if (state.z > 0.05f) cloudCells++;
                        relativeHumiditySum += RelativeHumidity(auxValue.x, state.x, vaporCapacityScale);
                        humiditySampleCount++;
                        if (state.x > metrics.MaxAtmosphericTemperature)
                        {
                            metrics.MaxAtmosphericTemperature = state.x;
                            metrics.MaxAtmosphericTemperatureX = x;
                            metrics.MaxAtmosphericTemperatureY = y;
                        }
                        float vapor = Math.Max(0f, auxValue.x);
                        if (vapor > metrics.MaxVaporMass)
                        {
                            metrics.MaxVaporMass = vapor;
                            metrics.MaxVaporX = x;
                            metrics.MaxVaporY = y;
                        }
                    }

                    if (material != MaterialIds.Void)
                    {
                        temperatureSum += state.x;
                        pressureSum += state.y;
                        moistureSum += Math.Max(0f, state.z);
                        sampleCount++;
                    }

                    if (material == MaterialIds.Algae || material == MaterialIds.Cricket || material == MaterialIds.CricketEgg
                        || material == MaterialIds.Wasp || material == MaterialIds.WaspEgg
                        || material == MaterialIds.Leaf || material == MaterialIds.Wood)
                        metrics.OrganismCount++;

                    if (flow != null && material == MaterialIds.Air)
                    {
                        Vector2 cellFlow = flow[index];
                        windSpeedSum += Math.Sqrt(cellFlow.x * cellFlow.x + cellFlow.y * cellFlow.y);
                        windSampleCount++;
                    }

                    if (combustion != null)
                    {
                        Vector4 fire = combustion[index];
                        if (fire.y > 0.02f)
                        {
                            metrics.BurningCellCount++;
                            metrics.TotalFireIntensity += fire.y;
                        }
                        metrics.SootMass += Math.Max(0d, fire.z);
                        oxygenSum += Math.Max(0f, fire.x);
                        oxygenSampleCount++;
                    }

                    if (material != MaterialIds.Water && material != MaterialIds.Ice) continue;
                    if (radius < grid.atmosphereStartRadius * 0.95f)
                        oceanMask[index] = true;
                }
            }

            int oceanAngleCount = 0;
            for (int x = 0; x < width; x++)
                if (oceanAngles[x]) oceanAngleCount++;
            metrics.OceanCoverage = oceanAngleCount / (float)Math.Max(1, width);
            metrics.BasinCount = CountOceanAngleBasins(oceanAngles);
            metrics.TotalTrackedWaterMass = metrics.SurfaceWaterMass + metrics.GroundwaterMass + metrics.VaporMass;
            if (sampleCount > 0)
            {
                metrics.MeanTemperature = (float)(temperatureSum / sampleCount);
                metrics.MeanPressure = (float)(pressureSum / sampleCount);
                metrics.MeanMoisture = (float)(moistureSum / sampleCount);
            }
            if (windSampleCount > 0)
                metrics.MeanWindSpeed = (float)(windSpeedSum / windSampleCount);
            if (oxygenSampleCount > 0)
                metrics.MeanOxygen = (float)(oxygenSum / oxygenSampleCount);
            if (humiditySampleCount > 0)
                metrics.MeanRelativeHumidity = (float)(relativeHumiditySum / humiditySampleCount);
            if (atmosphereCells > 0)
                metrics.CloudCover = cloudCells / (float)atmosphereCells;
            return metrics;
        }

        public static float RelativeHumidity(float vapor, float temperature, float scale)
        {
            float t = Mathf.Clamp(temperature, -40f, 80f);
            float es = Mathf.Exp(WaterMagnusA * t / Mathf.Max(1e-3f, WaterMagnusB + t));
            float saturation = Mathf.Max(1e-4f, Mathf.Max(0.001f, scale) * es);
            return Mathf.Clamp01(Mathf.Max(0f, vapor) / saturation);
        }

        public static void MeasureFaunaAsync(SimulationHost host, Action<FaunaMetrics> completed)
        {
            if (host == null || !host.IsReady || host.Resources == null)
            {
                completed?.Invoke(default);
                return;
            }
            RenderTexture materialTex = host.Resources.MaterialRead;
            RenderTexture faunaTex = host.Resources.FaunaRead;
            if (materialTex == null || faunaTex == null)
            {
                completed?.Invoke(default);
                return;
            }
            Action fail = () => completed?.Invoke(default);
            RequestField(materialTex, fail, materialRequest =>
            {
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                RequestFieldSlice(faunaTex, 0, fail, vitalsRequest =>
                {
                    Vector4[] vitals = vitalsRequest.GetData<Vector4>().ToArray();
                    RequestFieldSlice(faunaTex, 2, fail, genomeRequest =>
                    {
                        Vector4[] genomeBits = genomeRequest.GetData<Vector4>().ToArray();
                        var genomes = new FaunaGenome.Packed[genomeBits.Length];
                        for (int i = 0; i < genomeBits.Length; i++)
                            genomes[i] = FaunaGenome.FromFloatBits(genomeBits[i]);
                        completed?.Invoke(ComputeFaunaMetrics(materials, vitals, genomes));
                    });
                });
            });
        }

        public static void MeasureWaspAsync(SimulationHost host, Action<WaspMetrics> completed)
        {
            if (host == null || !host.IsReady || host.Resources == null)
            {
                completed?.Invoke(default);
                return;
            }
            RenderTexture materialTex = host.Resources.MaterialRead;
            RenderTexture waspTex = host.Resources.WaspRead;
            if (materialTex == null || waspTex == null)
            {
                completed?.Invoke(default);
                return;
            }
            int vitalsSlice = waspTex.volumeDepth >= FaunaGenome.FaunaSliceCount ? FaunaGenome.WaspVitalsSlice : 0;
            int genomeSlice = waspTex.volumeDepth >= FaunaGenome.FaunaSliceCount ? FaunaGenome.WaspGenomeSlice : 2;

            Action fail = () => completed?.Invoke(default);
            RequestField(materialTex, fail, materialRequest =>
            {
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                RequestFieldSlice(waspTex, vitalsSlice, fail, vitalsRequest =>
                {
                    Vector4[] vitals = vitalsRequest.GetData<Vector4>().ToArray();
                    RequestFieldSlice(waspTex, genomeSlice, fail, genomeRequest =>
                    {
                        Vector4[] genomeBits = genomeRequest.GetData<Vector4>().ToArray();
                        var genomes = new FaunaGenome.Packed[genomeBits.Length];
                        for (int i = 0; i < genomeBits.Length; i++)
                            genomes[i] = WaspGenome.FromFloatBits(genomeBits[i]);
                        ReadCargo(waspTex, fail, materials, vitals, genomes, completed);
                    });
                });
            });
        }

        private static void ReadCargo(RenderTexture waspTex, Action fail, uint[] materials, Vector4[] vitals,
            FaunaGenome.Packed[] genomes, Action<WaspMetrics> completed)
        {
            int cargoBase = waspTex.volumeDepth >= FaunaGenome.FaunaSliceCount ? FaunaGenome.WaspCargoSlice : 4;
            RequestFieldSlice(waspTex, cargoBase, fail, first =>
            {
                Vector4[] cargo0 = first.GetData<Vector4>().ToArray();
                RequestFieldSlice(waspTex, cargoBase + 1, fail, second =>
                {
                    Vector4[] cargo1 = second.GetData<Vector4>().ToArray();
                    RequestFieldSlice(waspTex, cargoBase + 2, fail, third =>
                    {
                        Vector4[] cargo2 = third.GetData<Vector4>().ToArray();
                        completed?.Invoke(ComputeWaspMetrics(materials, vitals, genomes, cargo0, cargo1, cargo2));
                    });
                });
            });
        }

        public static WaspMetrics ComputeWaspMetrics(uint[] materials, Vector4[] vitals, FaunaGenome.Packed[] genomes,
            Vector4[] cargo0, Vector4[] cargo1, Vector4[] cargo2)
        {
            var metrics = new WaspMetrics();
            int count = Math.Min(materials.Length, Math.Min(vitals.Length, genomes.Length));
            int living = 0;
            double ageSum = 0d;
            double generationSum = 0d;
            for (int i = 0; i < count; i++)
            {
                if (materials[i] != MaterialIds.Wasp && materials[i] != MaterialIds.WaspEgg) continue;
                FaunaGenome.Packed genome = WaspGenome.Sanitize(genomes[i]);
                uint stage = WaspGenome.Stage(genome);
                if (stage == WaspGenome.StageAdult) metrics.AdultCount++;
                else if (stage == WaspGenome.StageJuvenile) metrics.JuvenileCount++;
                else if (stage == WaspGenome.StageEgg) metrics.EggCount++;
                metrics.TotalCalories += Math.Max(0d, vitals[i].x);
                metrics.TotalHydration += Math.Max(0d, vitals[i].y);
                ageSum += Math.Max(0f, vitals[i].z);
                generationSum += WaspGenome.Generation(genome);
                living++;

                int samples = CargoCountAt(cargo0, cargo1, cargo2, i);
                if (samples > 0)
                {
                    metrics.PollenCarrierCount++;
                    metrics.PollenSampleCount += samples;
                }
            }
            if (living > 0)
            {
                metrics.MeanAge = (float)(ageSum / living);
                metrics.MeanGeneration = (float)(generationSum / living);
            }
            return metrics;
        }

        public static void MeasureTreeAsync(SimulationHost host, Action<TreeMetrics> completed)
        {
            if (host == null || !host.IsReady || host.Resources == null)
            {
                completed?.Invoke(default);
                return;
            }
            RenderTexture materialTex = host.Resources.MaterialRead;
            RenderTexture treeTex = host.Resources.TreeRead;
            if (materialTex == null || treeTex == null)
            {
                completed?.Invoke(default);
                return;
            }
            int topoSlice = treeTex.volumeDepth >= FloraGenome.SliceCount ? FloraGenome.TopologySlice : TreeGenome.TopologySlice;
            int genSlice = treeTex.volumeDepth >= FloraGenome.SliceCount ? FloraGenome.GenomeSlice : TreeGenome.GenomeSlice;

            Action fail = () => completed?.Invoke(default);
            RequestField(materialTex, fail, materialRequest =>
            {
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                RequestFieldSlice(treeTex, TreeGenome.PhysiologySlice, fail, physRequest =>
                {
                    Vector4[] phys = physRequest.GetData<Vector4>().ToArray();
                    RequestFieldSlice(treeTex, topoSlice, fail, topoRequest =>
                    {
                        Vector4[] topologyBits = topoRequest.GetData<Vector4>().ToArray();
                        RequestFieldSlice(treeTex, genSlice, fail, genomeRequest =>
                        {
                            Vector4[] genomeBits = genomeRequest.GetData<Vector4>().ToArray();
                            var topology = new TreeGenome.Packed[topologyBits.Length];
                            var genomes = new TreeGenome.Packed[genomeBits.Length];
                            for (int i = 0; i < topologyBits.Length; i++)
                                topology[i] = TreeGenome.FromFloatBits(topologyBits[i]);
                            for (int i = 0; i < genomeBits.Length; i++)
                                genomes[i] = TreeGenome.FromFloatBits(genomeBits[i]);
                            completed?.Invoke(ComputeTreeMetrics(materials, phys, topology, genomes));
                        });
                    });
                });
            });
        }

        public static TreeMetrics ComputeTreeMetrics(uint[] materials, Vector4[] phys, TreeGenome.Packed[] topology, TreeGenome.Packed[] genomes)
        {
            var metrics = new TreeMetrics();
            int count = Math.Min(materials.Length, Math.Min(phys.Length, Math.Min(topology.Length, genomes.Length)));
            for (int i = 0; i < count; i++)
            {
                if (topology[i].X == 0) continue;
                TreeGenome.Packed genome = TreeGenome.Sanitize(genomes[i]);
                uint stage = TreeGenome.Stage(genome);
                metrics.PixelCount++;
                if ((TreeGenome.Flags(topology[i].Z) & TreeGenome.FlagAnchor) != 0)
                    metrics.AnchorCount++;
                if (stage == TreeGenome.StageSprout) metrics.SproutCount++;
                else if (stage == TreeGenome.StageSapling) metrics.SaplingCount++;
                else if (stage == TreeGenome.StageTree) metrics.TreeCount++;
                else if (stage == TreeGenome.StageDead) metrics.DeadCount++;
                metrics.TotalEnergy += Math.Max(0d, phys[i].x);
                metrics.TotalHydration += Math.Max(0d, phys[i].y);
                metrics.TotalHealth += Math.Max(0d, phys[i].w);
            }
            return metrics;
        }

        private static int CargoCountAt(Vector4[] cargo0, Vector4[] cargo1, Vector4[] cargo2, int index)
        {
            int samples = 0;
            if (cargo0 != null && index < cargo0.Length && WaspGenome.CargoValid(WaspGenome.FromFloatBits(cargo0[index]))) samples++;
            if (cargo1 != null && index < cargo1.Length && WaspGenome.CargoValid(WaspGenome.FromFloatBits(cargo1[index]))) samples++;
            if (cargo2 != null && index < cargo2.Length && WaspGenome.CargoValid(WaspGenome.FromFloatBits(cargo2[index]))) samples++;
            return samples;
        }

        public static FaunaMetrics ComputeFaunaMetrics(uint[] materials, Vector4[] vitals, FaunaGenome.Packed[] genomes)
        {
            var metrics = new FaunaMetrics();
            int count = Math.Min(materials.Length, Math.Min(vitals.Length, genomes.Length));
            int living = 0;
            double ageSum = 0d;
            double generationSum = 0d;
            for (int i = 0; i < count; i++)
            {
                if (materials[i] != MaterialIds.Cricket && materials[i] != MaterialIds.CricketEgg) continue;
                FaunaGenome.Packed genome = FaunaGenome.Sanitize(genomes[i]);
                uint stage = FaunaGenome.Stage(genome);
                if (stage == FaunaGenome.StageAdult) metrics.AdultCount++;
                else if (stage == FaunaGenome.StageJuvenile) metrics.JuvenileCount++;
                else if (stage == FaunaGenome.StageEgg) metrics.EggCount++;
                metrics.TotalCalories += Math.Max(0d, vitals[i].x);
                metrics.TotalHydration += Math.Max(0d, vitals[i].y);
                ageSum += Math.Max(0f, vitals[i].z);
                generationSum += FaunaGenome.Generation(genome);
                living++;
            }
            if (living > 0)
            {
                metrics.MeanAge = (float)(ageSum / living);
                metrics.MeanGeneration = (float)(generationSum / living);
            }
            return metrics;
        }

        public static AtmosphericCirculationMetrics ComputeAtmosphericMetrics(
            PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux, Vector2[] flow, float solarAngle01)
        {
            int width = grid.angularResolution;
            int height = grid.radialResolution;
            int cellCount = Math.Min(materials.Length, Math.Min(states.Length, Math.Min(aux.Length, flow.Length)));
            var metrics = new AtmosphericCirculationMetrics();

            double vaporMass = 0d;
            double vaporAltitudeMoment = 0d;
            double vaporAltitudeSecond = 0d;
            double verticalSum = 0d;
            double verticalSq = 0d;
            double pressureAnomalySq = 0d;
            double circulationEnergy = 0d;
            int atmosphereCells = 0;
            double daySurfaceTemp = 0d;
            double nightSurfaceTemp = 0d;
            int daySurfaceCount = 0;
            int nightSurfaceCount = 0;

            for (int y = 0; y < height; y++)
            {
                float radius = grid.Radius01(y);
                float equilibrium = Math.Min(grid.atmosphereStartRadius > 0f ? 2f : 2f, (1f - radius) * 2f);
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (index >= cellCount) continue;

                    uint material = materials[index];
                    Vector4 state = states[index];
                    Vector4 auxValue = aux[index];
                    Vector2 cellFlow = flow[index];
                    bool atmosphere = material == MaterialIds.Air;

                    if (atmosphere)
                    {
                        atmosphereCells++;
                        float cloud = Math.Max(0f, state.z);
                        float vapor = Math.Max(0f, auxValue.x);
                        metrics.CloudMass += cloud;
                        vaporMass += vapor;
                        if (state.x > metrics.MaxAtmosphericTemperature)
                        {
                            metrics.MaxAtmosphericTemperature = state.x;
                            metrics.MaxAtmosphericTemperatureX = x;
                            metrics.MaxAtmosphericTemperatureY = y;
                        }
                        if (vapor > metrics.MaxVaporMass)
                        {
                            metrics.MaxVaporMass = vapor;
                            metrics.MaxVaporX = x;
                            metrics.MaxVaporY = y;
                        }
                        vaporAltitudeMoment += vapor * radius;
                        vaporAltitudeSecond += vapor * radius * radius;

                        verticalSum += cellFlow.y;
                        verticalSq += cellFlow.y * cellFlow.y;
                        if (cellFlow.y > 0.01f) metrics.UpdraftCells++;
                        if (cellFlow.y < -0.01f) metrics.DowndraftCells++;

                        float anomaly = state.y - equilibrium;
                        pressureAnomalySq += anomaly * anomaly;
                        circulationEnergy += cellFlow.x * cellFlow.x + cellFlow.y * cellFlow.y;
                    }

                    bool exposedSurface = !atmosphere && material != MaterialIds.Void && y < height - 1 &&
                                          (materials[(y + 1) * width + x] == MaterialIds.Air ||
                                           materials[(y + 1) * width + x] == MaterialIds.Void);
                    if (exposedSurface)
                    {
                        float theta01 = (x + 0.5f) / width;
                        float angle = (theta01 - solarAngle01) * (Mathf.PI * 2f);
                        if (Mathf.Cos(angle) > 0f)
                        {
                            daySurfaceTemp += state.x;
                            daySurfaceCount++;
                        }
                        else
                        {
                            nightSurfaceTemp += state.x;
                            nightSurfaceCount++;
                        }
                    }
                }
            }

            metrics.VaporMass = vaporMass;
            if (vaporMass > 1e-6d)
            {
                metrics.VaporAltitudeMean = (float)(vaporAltitudeMoment / vaporMass);
                float mean = metrics.VaporAltitudeMean;
                metrics.VaporAltitudeVariance = Math.Max(0f, (float)(vaporAltitudeSecond / vaporMass) - mean * mean);
            }

            if (atmosphereCells > 0)
            {
                metrics.MeanVerticalFlow = (float)(verticalSum / atmosphereCells);
                metrics.RmsVerticalFlow = Mathf.Sqrt((float)(verticalSq / atmosphereCells));
                metrics.SignedMeanVerticalDrift = metrics.MeanVerticalFlow;
                metrics.PressureAnomalyRms = Mathf.Sqrt((float)(pressureAnomalySq / atmosphereCells));
                metrics.CirculationEnergy = (float)(circulationEnergy / atmosphereCells);
            }

            float dayMean = daySurfaceCount > 0 ? (float)(daySurfaceTemp / daySurfaceCount) : 0f;
            float nightMean = nightSurfaceCount > 0 ? (float)(nightSurfaceTemp / nightSurfaceCount) : 0f;
            metrics.DayNightSurfaceTemperatureDelta = dayMean - nightMean;
            return metrics;
        }

        public static FloraMetrics ComputeFloraMetrics(uint[] materials, Vector4[] life, FloraGenome.Packed[] genomes)
        {
            var metrics = new FloraMetrics
            {
                GeneMeans = new float[FloraGenome.GeneCount],
                GeneVariance = new float[FloraGenome.GeneCount]
            };
            int count = Math.Min(materials.Length, Math.Min(life.Length, genomes.Length));
            var geneSums = new double[FloraGenome.GeneCount];
            var geneSquares = new double[FloraGenome.GeneCount];
            int living = 0;
            double energySum = 0d;
            double generationSum = 0d;
            for (int i = 0; i < count; i++)
            {
                Vector4 cellLife = life[i];
                if (cellLife.x > 1e-4f) metrics.SporeCarrierCount++;
                if (materials[i] != MaterialIds.Algae) continue;
                FloraGenome.Packed genome = FloraGenome.Sanitize(genomes[i]);
                uint stage = FloraGenome.Stage(genome);
                if (stage == FloraGenome.StageActive) metrics.LivingCount++;
                else if (stage == FloraGenome.StageDormant) metrics.DormantCount++;
                else if (stage == FloraGenome.StageDesiccated) metrics.DesiccatedCount++;
                metrics.TotalBiomass += Math.Max(0d, cellLife.y);
                energySum += Math.Max(0f, cellLife.z);
                generationSum += FloraGenome.Generation(genome);
                living++;
                for (int g = 0; g < FloraGenome.GeneCount; g++)
                {
                    double gene = FloraGenome.DecodeGene(genome, g);
                    geneSums[g] += gene;
                    geneSquares[g] += gene * gene;
                }
            }
            if (living > 0)
            {
                metrics.MeanEnergy = (float)(energySum / living);
                metrics.MeanGeneration = (float)(generationSum / living);
                for (int g = 0; g < FloraGenome.GeneCount; g++)
                {
                    double mean = geneSums[g] / living;
                    metrics.GeneMeans[g] = (float)mean;
                    metrics.GeneVariance[g] = (float)Math.Max(0d, geneSquares[g] / living - mean * mean);
                }
            }
            return metrics;
        }

        public static void MeasureGeodynamicsAsync(SimulationHost host, Action<WorldGeodynamicsMetrics> completed)
        {
            if (host == null || !host.IsReady || host.Resources?.GeodynamicsStateRead == null)
            {
                completed?.Invoke(default);
                return;
            }

            ComputeBuffer state = host.Resources.GeodynamicsStateRead;
            ComputeBuffer events = host.Resources.GeodynamicsEvents;
            Action fail = () => completed?.Invoke(default);
            try
            {
                AsyncGPUReadback.Request(state, stateRequest =>
                {
                    if (stateRequest.hasError)
                    {
                        fail();
                        return;
                    }
                    Vector4[] reservoirs = stateRequest.GetData<Vector4>().ToArray();
                    AsyncGPUReadback.Request(events, eventRequest =>
                    {
                        if (eventRequest.hasError)
                        {
                            fail();
                            return;
                        }
                        Vector4[] eventValues = eventRequest.GetData<Vector4>().ToArray();
                        completed?.Invoke(ComputeGeodynamicsMetrics(reservoirs, eventValues,
                            host.Config != null ? host.Config.geodynamicsAngularBins : 64,
                            host.Config != null ? host.Config.geodynamicsRadialBins : 16));
                    });
                });
            }
            catch (Exception)
            {
                fail();
            }
        }

        public static WorldGeodynamicsMetrics ComputeGeodynamicsMetrics(NativeArray<Vector4> state, NativeArray<Vector4> events, int angularBins, int radialBins)
        {
            var metrics = new WorldGeodynamicsMetrics();
            angularBins = Mathf.Clamp(angularBins, 16, 128);
            radialBins = Mathf.Clamp(radialBins, 8, 32);
            int cells = angularBins * radialBins;
            var activeAngles = new bool[angularBins];
            int counted = 0;
            for (int a = 0; a < angularBins; a++)
            {
                for (int r = 0; r < radialBins; r++)
                {
                    int stateIndex = ((a * radialBins) + r) * 2;
                    if (stateIndex + 1 >= state.Length) continue;
                    Vector4 reservoir = state[stateIndex];
                    Vector4 kinematics = state[stateIndex + 1];
                    if (!Finite(reservoir) || !Finite(kinematics))
                        metrics.HasNonFinite = true;
                    metrics.MeanStrain += Mathf.Max(0f, reservoir.z);
                    metrics.MaxStrain = Mathf.Max(metrics.MaxStrain, reservoir.z);
                    metrics.MeanOverpressure += Mathf.Max(0f, reservoir.y);
                    metrics.MaxOverpressure = Mathf.Max(metrics.MaxOverpressure, reservoir.y);
                    metrics.MeanFaultWeakness += Mathf.Max(0f, kinematics.z);
                    counted++;
                    int eventIndex = a * radialBins + r;
                    if (eventIndex < events.Length && events[eventIndex].y > 0.05f)
                    {
                        metrics.ActiveEventCount++;
                        metrics.ReleasedEnergy += Mathf.Max(0f, events[eventIndex].w);
                        activeAngles[a] = true;
                    }
                }
            }
            if (counted > 0)
            {
                metrics.MeanStrain /= counted;
                metrics.MeanOverpressure /= counted;
                metrics.MeanFaultWeakness /= counted;
            }
            int active = 0;
            for (int i = 0; i < activeAngles.Length; i++)
                if (activeAngles[i]) active++;
            metrics.AffectedAngularFraction = active / (float)Mathf.Max(1, angularBins);
            return metrics;
        }

        private static WorldGeodynamicsMetrics ComputeGeodynamicsMetrics(Vector4[] state, Vector4[] events, int angularBins, int radialBins)
        {
            var stateNative = new NativeArray<Vector4>(state, Allocator.Temp);
            var eventsNative = new NativeArray<Vector4>(events, Allocator.Temp);
            try
            {
                return ComputeGeodynamicsMetrics(stateNative, eventsNative, angularBins, radialBins);
            }
            finally
            {
                stateNative.Dispose();
                eventsNative.Dispose();
            }
        }

        private static bool Finite(Vector4 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

        public static int CountOceanAngleBasins(bool[] oceanAngles)
        {
            int width = oceanAngles.Length;
            int basins = 0;
            for (int x = 0; x < width; x++)
            {
                if (!oceanAngles[x]) continue;
                int prev = (x - 1 + width) % width;
                if (oceanAngles[prev]) continue;
                basins++;
            }
            return basins;
        }

        public static int CountWrapAwareBasins(bool[] oceanMask, int width, int height)
        {
            var visited = new bool[oceanMask.Length];
            int basins = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (!oceanMask[index] || visited[index]) continue;
                    FloodFillBasin(oceanMask, visited, width, height, x, y);
                    basins++;
                }
            }
            return basins;
        }

        private static void FloodFillBasin(bool[] oceanMask, bool[] visited, int width, int height, int startX, int startY)
        {
            var stack = new Stack<(int x, int y)>();
            stack.Push((startX, startY));
            while (stack.Count > 0)
            {
                (int x, int y) = stack.Pop();
                int index = y * width + x;
                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                if (!oceanMask[index] || visited[index]) continue;
                visited[index] = true;
                stack.Push((x - 1, y));
                stack.Push((x + 1, y));
                stack.Push((x, y - 1));
                stack.Push((x, y + 1));
                if (x == 0) stack.Push((width - 1, y));
                if (x == width - 1) stack.Push((0, y));
            }
        }
    }
}
