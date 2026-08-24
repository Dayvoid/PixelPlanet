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

        public void Save(SimulationHost host, string path, Action<bool> completed = null)
        {
            if (host == null || !host.IsReady) { completed?.Invoke(false); return; }
            byte[][] payloads = new byte[7][];
            int remaining = 7;
            bool failed = false;
            RenderTexture[] textures =
            {
                host.Resources.MaterialRead, host.Resources.StateRead,
                host.Resources.FlowRead, host.Resources.AuxRead,
                host.Resources.ShadeRead, host.Resources.EcologyRead,
                host.Resources.CombustionRead
            };
            for (int i = 0; i < textures.Length; i++)
            {
                int index = i;
                AsyncGPUReadback.Request(textures[i], 0, request =>
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
                        writer.Write(Version5);
                        writer.Write(host.Resources.Grid.angularResolution);
                        writer.Write(host.Resources.Grid.radialResolution);
                        writer.Write(config.seed);
                        writer.Write(host.Clock.TickCount);
                        WriteConfig(writer, config);
                        WriteEcologyConfig(writer, config);
                        foreach (byte[] payload in payloads)
                        {
                            writer.Write(payload.Length);
                            writer.Write(payload);
                        }
                    }
                    completed?.Invoke(!failed);
                });
            }
        }

        public bool Load(SimulationHost host, string path)
        {
            if (host == null || !host.IsReady || !File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic) return false;
            int version = reader.ReadInt32();
            if (version != Version1 && version != Version2 && version != Version3 && version != Version4 && version != Version5) return false;

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
                Texture2D staging = CreateStagingTexture(width, height, target.graphicsFormat);
                staging.LoadRawTextureData(payload);
                staging.Apply(false, false);
                Graphics.CopyTexture(staging, target);
                UnityEngine.Object.Destroy(staging);
            }

            if (version >= Version3)
            {
                int length = reader.ReadInt32();
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length) return false;
                Texture2D staging = CreateStagingTexture(width, height, host.Resources.ShadeRead.graphicsFormat);
                staging.LoadRawTextureData(payload);
                staging.Apply(false, false);
                Graphics.CopyTexture(staging, host.Resources.ShadeRead);
                UnityEngine.Object.Destroy(staging);
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

        private static Texture2D CreateStagingTexture(int width, int height, GraphicsFormat format)
        {
            TextureFormat textureFormat = format switch
            {
                GraphicsFormat.R32_UInt => TextureFormat.RFloat,
                GraphicsFormat.R32G32_SFloat => TextureFormat.RGFloat,
                _ => TextureFormat.RGBAFloat
            };
            return new Texture2D(width, height, textureFormat, false, true);
        }
    }
}
