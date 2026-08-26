using System;
using System.IO;
using GeneSys.Configuration;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GeneSys.Persistence
{
    public sealed class WorldSnapshotService
    {
        private const uint Magic = 0x47535953;
        private const int Version1 = 1;
        private const int Version2 = 2;
        private const int Version3 = 3;
        private const int Version4 = 4;
        private const int Version5 = 5;
        private const int Version6 = 6;
        private const int Version7 = 7;
        private const int Version8 = 8;

        public void Save(SimulationHost host, string path, Action<bool> completed = null)
        {
            if (host == null || !host.IsReady) { completed?.Invoke(false); return; }
            byte[][] payloads = new byte[10][];
            int remaining = 10;
            bool failed = false;
            RenderTexture[] textures =
            {
                host.Resources.MaterialRead, host.Resources.StateRead,
                host.Resources.FlowRead, host.Resources.AuxRead,
                host.Resources.ShadeRead, host.Resources.EcologyRead,
                host.Resources.CombustionRead, host.Resources.StormRead
            };
            for (int i = 0; i < textures.Length; i++)
            {
                int index = i;
                AsyncGPUReadback.Request(textures[i], 0, request => CompletePayload(index, request));
            }
            RenderTexture lifeGenome = host.Resources.LifeGenomeRead;
            AsyncGPUReadback.Request(lifeGenome, 0, 0, lifeGenome.width, 0, lifeGenome.height, 0, 1,
                request => CompletePayload(8, request));
            AsyncGPUReadback.Request(lifeGenome, 0, 0, lifeGenome.width, 0, lifeGenome.height, 1, 1,
                request => CompletePayload(9, request));

            void CompletePayload(int index, UnityEngine.Rendering.AsyncGPUReadbackRequest request)
            {
                if (request.hasError) failed = true;
                else payloads[index] = request.GetData<byte>().ToArray();
                remaining--;
                if (remaining != 0) return;
                if (!failed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.persistentDataPath);
                    using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
                    using var writer = new BinaryWriter(stream);
                    SimulationConfig config = host.Config;
                    writer.Write(Magic);
                    writer.Write(Version8);
                    writer.Write(host.Resources.Grid.angularResolution);
                    writer.Write(host.Resources.Grid.radialResolution);
                    writer.Write(config.seed);
                    writer.Write(host.Clock.TickCount);
                    WriteConfig(writer, config);
                    WriteEcologyConfig(writer, config);
                    WriteFloraConfig(writer, config);
                    WriteFloraMovementConfig(writer, config);
                    foreach (byte[] payload in payloads)
                    {
                        writer.Write(payload.Length);
                        writer.Write(payload);
                    }
                }
                completed?.Invoke(!failed);
            }
        }

        public bool Load(SimulationHost host, string path)
        {
            if (host == null || !host.IsReady || !File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic) return false;
            int version = reader.ReadInt32();
            if (version != Version1 && version != Version2 && version != Version3 && version != Version4 && version != Version5 && version != Version6 && version != Version7 && version != Version8) return false;

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int seed = reader.ReadInt32();
            long tick = reader.ReadInt64();
            if (width != host.Resources.Grid.angularResolution || height != host.Resources.Grid.radialResolution) return false;

            host.Config.seed = seed;
            if (version >= Version2)
                ReadConfig(reader, host.Config);
            if (version >= Version4)
                ReadEcologyConfig(reader, host.Config);
            if (version >= Version7)
                ReadFloraConfig(reader, host.Config);
            if (version >= Version8)
                ReadFloraMovementConfig(reader, host.Config);
            else
                ApplyFloraMovementDefaults(host.Config);

            RenderTexture[] coreTargets =
            {
                host.Resources.MaterialRead, host.Resources.StateRead,
                host.Resources.FlowRead, host.Resources.AuxRead
            };
            foreach (RenderTexture target in coreTargets)
            {
                int length = reader.ReadInt32();
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length) return false;
                UploadTexturePayload(host, target, payload, width, height);
            }

            if (version >= Version3)
            {
                int length = reader.ReadInt32();
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length) return false;
                UploadTexturePayload(host, host.Resources.ShadeRead, payload, width, height);
            }
            else
            {
                host.FillShadesFromMaterials();
            }

            if (version >= Version4)
            {
                int length = reader.ReadInt32();
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length) return false;
                Texture2D staging = CreateStagingTexture(width, height, host.Resources.EcologyRead.graphicsFormat);
                staging.LoadRawTextureData(payload);
                staging.Apply(false, false);
                Graphics.CopyTexture(staging, host.Resources.EcologyRead);
                UnityEngine.Object.Destroy(staging);
            }
            else
            {
                ClearEcology(host.Resources);
            }

            if (version >= Version5)
            {
                int length = reader.ReadInt32();
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length) return false;
                Texture2D staging = CreateStagingTexture(width, height, host.Resources.CombustionRead.graphicsFormat);
                staging.LoadRawTextureData(payload);
                staging.Apply(false, false);
                Graphics.CopyTexture(staging, host.Resources.CombustionRead);
                UnityEngine.Object.Destroy(staging);
            }
            else
            {
                ClearCombustion(host.Resources);
            }

            if (version >= Version6)
            {
                int length = reader.ReadInt32();
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length) return false;
                Texture2D staging = CreateStagingTexture(width, height, host.Resources.StormRead.graphicsFormat);
                staging.LoadRawTextureData(payload);
                staging.Apply(false, false);
                Graphics.CopyTexture(staging, host.Resources.StormRead);
                UnityEngine.Object.Destroy(staging);
            }
            else
            {
                ClearStorm(host.Resources);
            }

            if (version >= Version7)
            {
                int lifeLength = reader.ReadInt32();
                byte[] lifePayload = reader.ReadBytes(lifeLength);
                if (lifePayload.Length != lifeLength) return false;
                Texture2D lifeStaging = CreateStagingTexture(width, height, GraphicsFormat.R32G32B32A32_SFloat);
                lifeStaging.LoadRawTextureData(lifePayload);
                lifeStaging.Apply(false, false);
                Graphics.CopyTexture(lifeStaging, 0, 0, host.Resources.LifeGenomeRead, 0, 0);
                UnityEngine.Object.Destroy(lifeStaging);

                int genomeLength = reader.ReadInt32();
                byte[] genomePayload = reader.ReadBytes(genomeLength);
                if (genomePayload.Length != genomeLength) return false;
                Texture2D genomeStaging = CreateStagingTexture(width, height, GraphicsFormat.R32G32B32A32_SFloat);
                genomeStaging.LoadRawTextureData(genomePayload);
                genomeStaging.Apply(false, false);
                Graphics.CopyTexture(genomeStaging, 0, 0, host.Resources.LifeGenomeRead, 1, 0);
                UnityEngine.Object.Destroy(genomeStaging);
            }
            else
            {
                ClearFlora(host.Resources);
            }

            host.Resources.CopyReadToWrite();
            host.RestoreSimulationTick(tick);
            return true;
        }

        private static void ClearEcology(SimulationResources resources)
        {
            int width = resources.Grid.angularResolution;
            int height = resources.Grid.radialResolution;
            var staging = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            staging.SetPixels(new Color[width * height]);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, resources.EcologyRead);
            Graphics.CopyTexture(staging, resources.EcologyWrite);
            UnityEngine.Object.Destroy(staging);
        }

        private static void ClearCombustion(SimulationResources resources)
        {
            int width = resources.Grid.angularResolution;
            int height = resources.Grid.radialResolution;
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(1f, 0f, 0f, 0f);
            var staging = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            staging.SetPixels(pixels);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, resources.CombustionRead);
            Graphics.CopyTexture(staging, resources.CombustionWrite);
            UnityEngine.Object.Destroy(staging);
        }

        private static void ClearStorm(SimulationResources resources)
        {
            int width = resources.Grid.angularResolution;
            int height = resources.Grid.radialResolution;
            var staging = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            staging.SetPixels(new Color[width * height]);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, resources.StormRead);
            Graphics.CopyTexture(staging, resources.StormWrite);
            UnityEngine.Object.Destroy(staging);
        }

        private static void ClearFlora(SimulationResources resources)
        {
            int width = resources.Grid.angularResolution;
            int height = resources.Grid.radialResolution;
            var lifeStaging = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            lifeStaging.SetPixels(new Color[width * height]);
            lifeStaging.Apply(false, false);
            Graphics.CopyTexture(lifeStaging, 0, 0, resources.LifeGenomeRead, 0, 0);
            Graphics.CopyTexture(lifeStaging, 0, 0, resources.LifeGenomeWrite, 0, 0);
            UnityEngine.Object.Destroy(lifeStaging);

            var genomeStaging = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            genomeStaging.SetPixels(new Color[width * height]);
            genomeStaging.Apply(false, false);
            Graphics.CopyTexture(genomeStaging, 0, 0, resources.LifeGenomeRead, 1, 0);
            Graphics.CopyTexture(genomeStaging, 0, 0, resources.LifeGenomeWrite, 1, 0);
            UnityEngine.Object.Destroy(genomeStaging);
        }

        private static void WriteConfig(BinaryWriter writer, SimulationConfig config)
        {
            writer.Write(config.targetOceanCoverage);
            writer.Write(config.minOceanBasins);
            writer.Write(config.maxOceanBasins);
            writer.Write(config.seaLevelRadius);
            writer.Write(config.basinDepth);
            writer.Write(config.terrainRelief);
            writer.Write(config.coastRoughness);
            writer.Write(config.initialGroundwaterSaturation);
            writer.Write(config.initialAtmosphericHumidity);
            writer.Write(config.groundwaterDepth);
            writer.Write(config.runoffRate);
            writer.Write(config.pondingRate);
            writer.Write(config.springHeadThreshold);
            writer.Write(config.springDischargeRate);
            writer.Write(config.geyserHeatThreshold);
            writer.Write(config.geyserDischargeRate);
            writer.Write(config.geyserCooldownSeconds);
            writer.Write(config.hydrothermalStrength);
            writer.Write(config.ventChemicalRate);
        }

        private static void ReadConfig(BinaryReader reader, SimulationConfig config)
        {
            config.targetOceanCoverage = reader.ReadSingle();
            config.minOceanBasins = reader.ReadInt32();
            config.maxOceanBasins = reader.ReadInt32();
            config.seaLevelRadius = reader.ReadSingle();
            config.basinDepth = reader.ReadSingle();
            config.terrainRelief = reader.ReadSingle();
            config.coastRoughness = reader.ReadSingle();
            config.initialGroundwaterSaturation = reader.ReadSingle();
            config.initialAtmosphericHumidity = reader.ReadSingle();
            config.groundwaterDepth = reader.ReadSingle();
            config.runoffRate = reader.ReadSingle();
            config.pondingRate = reader.ReadSingle();
            config.springHeadThreshold = reader.ReadSingle();
            config.springDischargeRate = reader.ReadSingle();
            config.geyserHeatThreshold = reader.ReadSingle();
            config.geyserDischargeRate = reader.ReadSingle();
            config.geyserCooldownSeconds = reader.ReadSingle();
            config.hydrothermalStrength = reader.ReadSingle();
            config.ventChemicalRate = reader.ReadSingle();
        }

        private static void WriteEcologyConfig(BinaryWriter writer, SimulationConfig config)
        {
            writer.Write(config.mycologyInitialSporeLoad);
            writer.Write(config.mycologyRareStrainChance);
            writer.Write(config.mycologyAirTransportRate);
            writer.Write(config.mycologyWaterTransportRate);
            writer.Write(config.mycologyDiffusionRate);
            writer.Write(config.mycologySettlingRate);
            writer.Write(config.mycologySporulationRate);
            writer.Write(config.mycologyGrowthRate);
            writer.Write(config.mycologyDecayRate);
            writer.Write(config.mycologyGrowthTempMin);
            writer.Write(config.mycologyGrowthTempMax);
            writer.Write(config.mycologyGrowthMoistureMin);
            writer.Write(config.mycologyGrowthMoistureMax);
            writer.Write(config.mycologySurvivalTempMin);
            writer.Write(config.mycologySurvivalTempMax);
            writer.Write(config.mycologySurvivalMoistureMin);
            writer.Write(config.mycologySurvivalMoistureMax);
            writer.Write(config.mycologyElectricalTolerance);
            writer.Write(config.mycologyTraitEffectStrength);
        }

        private static void ReadEcologyConfig(BinaryReader reader, SimulationConfig config)
        {
            config.mycologyInitialSporeLoad = reader.ReadSingle();
            config.mycologyRareStrainChance = reader.ReadSingle();
            config.mycologyAirTransportRate = reader.ReadSingle();
            config.mycologyWaterTransportRate = reader.ReadSingle();
            config.mycologyDiffusionRate = reader.ReadSingle();
            config.mycologySettlingRate = reader.ReadSingle();
            config.mycologySporulationRate = reader.ReadSingle();
            config.mycologyGrowthRate = reader.ReadSingle();
            config.mycologyDecayRate = reader.ReadSingle();
            config.mycologyGrowthTempMin = reader.ReadSingle();
            config.mycologyGrowthTempMax = reader.ReadSingle();
            config.mycologyGrowthMoistureMin = reader.ReadSingle();
            config.mycologyGrowthMoistureMax = reader.ReadSingle();
            config.mycologySurvivalTempMin = reader.ReadSingle();
            config.mycologySurvivalTempMax = reader.ReadSingle();
            config.mycologySurvivalMoistureMin = reader.ReadSingle();
            config.mycologySurvivalMoistureMax = reader.ReadSingle();
            config.mycologyElectricalTolerance = reader.ReadSingle();
            config.mycologyTraitEffectStrength = reader.ReadSingle();
        }

        private static void WriteFloraConfig(BinaryWriter writer, SimulationConfig config)
        {
            writer.Write(config.floraSeedAtWorldgen);
            writer.Write(config.floraInitialSporeLoad);
            writer.Write(config.floraAirTransportRate);
            writer.Write(config.floraWaterTransportRate);
            writer.Write(config.floraDiffusionRate);
            writer.Write(config.floraSettlingRate);
            writer.Write(config.floraSporulationRate);
            writer.Write(config.floraGrowthRate);
            writer.Write(config.floraDecayRate);
            writer.Write(config.floraPhotosynthesisRate);
            writer.Write(config.floraOxygenYield);
            writer.Write(config.floraExudationRate);
            writer.Write(config.floraReproductionThreshold);
            writer.Write(config.floraBaseMutationRate);
            writer.Write(config.floraToxinMutationScale);
            writer.Write(config.floraGeneExpressionRange);
            writer.Write(config.floraGrowthTempMin);
            writer.Write(config.floraGrowthTempMax);
            writer.Write(config.floraGrowthMoistureMin);
            writer.Write(config.floraGrowthMoistureMax);
            writer.Write(config.floraSurvivalTempMin);
            writer.Write(config.floraSurvivalTempMax);
            writer.Write(config.floraSurvivalMoistureMin);
            writer.Write(config.floraSurvivalMoistureMax);
            writer.Write(config.floraMinLight);
            writer.Write(config.floraGerminationSporeThreshold);
            writer.Write(config.floraMaintenanceRate);
            writer.Write(config.floraNightDrain);
            writer.Write(config.floraDormancyMetabolicScale);
        }

        private static void ReadFloraConfig(BinaryReader reader, SimulationConfig config)
        {
            config.floraSeedAtWorldgen = reader.ReadBoolean();
            config.floraInitialSporeLoad = reader.ReadSingle();
            config.floraAirTransportRate = reader.ReadSingle();
            config.floraWaterTransportRate = reader.ReadSingle();
            config.floraDiffusionRate = reader.ReadSingle();
            config.floraSettlingRate = reader.ReadSingle();
            config.floraSporulationRate = reader.ReadSingle();
            config.floraGrowthRate = reader.ReadSingle();
            config.floraDecayRate = reader.ReadSingle();
            config.floraPhotosynthesisRate = reader.ReadSingle();
            config.floraOxygenYield = reader.ReadSingle();
            config.floraExudationRate = reader.ReadSingle();
            config.floraReproductionThreshold = reader.ReadSingle();
            config.floraBaseMutationRate = reader.ReadSingle();
            config.floraToxinMutationScale = reader.ReadSingle();
            config.floraGeneExpressionRange = reader.ReadSingle();
            config.floraGrowthTempMin = reader.ReadSingle();
            config.floraGrowthTempMax = reader.ReadSingle();
            config.floraGrowthMoistureMin = reader.ReadSingle();
            config.floraGrowthMoistureMax = reader.ReadSingle();
            config.floraSurvivalTempMin = reader.ReadSingle();
            config.floraSurvivalTempMax = reader.ReadSingle();
            config.floraSurvivalMoistureMin = reader.ReadSingle();
            config.floraSurvivalMoistureMax = reader.ReadSingle();
            config.floraMinLight = reader.ReadSingle();
            config.floraGerminationSporeThreshold = reader.ReadSingle();
            config.floraMaintenanceRate = reader.ReadSingle();
            config.floraNightDrain = reader.ReadSingle();
            config.floraDormancyMetabolicScale = reader.ReadSingle();
        }

        private static void WriteFloraMovementConfig(BinaryWriter writer, SimulationConfig config)
        {
            writer.Write(config.floraWindDispersalRate);
            writer.Write(config.floraRainDispersalRate);
            writer.Write(config.floraStackMigrationRate);
        }

        private static void ReadFloraMovementConfig(BinaryReader reader, SimulationConfig config)
        {
            config.floraWindDispersalRate = reader.ReadSingle();
            config.floraRainDispersalRate = reader.ReadSingle();
            config.floraStackMigrationRate = reader.ReadSingle();
        }

        private static void ApplyFloraMovementDefaults(SimulationConfig config)
        {
            config.floraWindDispersalRate = 0.35f;
            config.floraRainDispersalRate = 0.45f;
            config.floraStackMigrationRate = 0.2f;
        }

        private static void UploadTexturePayload(SimulationHost host, RenderTexture target, byte[] payload, int width, int height)
        {
            if (target.graphicsFormat == GraphicsFormat.R32_UInt)
            {
                host.UploadUIntTexture(target, payload);
                return;
            }

            Texture2D staging = CreateStagingTexture(width, height, target.graphicsFormat);
            staging.LoadRawTextureData(payload);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, target);
            UnityEngine.Object.Destroy(staging);
        }

        private static Texture2D CreateStagingTexture(int width, int height, GraphicsFormat format)
        {
            return new Texture2D(width, height, format, TextureCreationFlags.DontInitializePixels);
        }
    }
}
