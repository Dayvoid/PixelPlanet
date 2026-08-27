using System;
using System.Collections.Generic;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Rendering;

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
        public float MeanWindSpeed;
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

    public static class SimulationMetrics
    {
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
                                completed?.Invoke(ComputeMetrics(grid, materials, states, aux, flow, combustion));
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

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux)
            => ComputeMetrics(grid, materials, states, aux, null, null);

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux, Vector2[] flow)
            => ComputeMetrics(grid, materials, states, aux, flow, null);

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux, Vector2[] flow, Vector4[] combustion)
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
                    if (material == MaterialIds.Water || material == MaterialIds.Ice || state.z > 0.25f)
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

                    if (material != MaterialIds.Void)
                    {
                        temperatureSum += state.x;
                        pressureSum += state.y;
                        moistureSum += Math.Max(0f, state.z);
                        sampleCount++;
                    }

                    if (material == MaterialIds.Algae || material == MaterialIds.Cricket || material == MaterialIds.CricketEgg)
                        metrics.OrganismCount++;

                    if (flow != null && (material == MaterialIds.Air || material == MaterialIds.Vapor))
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
            return metrics;
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
                    bool atmosphere = material == MaterialIds.Air || material == MaterialIds.Vapor;

                    if (atmosphere)
                    {
                        atmosphereCells++;
                        float cloud = Math.Max(0f, state.z);
                        float vapor = Math.Max(0f, auxValue.x);
                        metrics.CloudMass += cloud;
                        vaporMass += vapor;
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
