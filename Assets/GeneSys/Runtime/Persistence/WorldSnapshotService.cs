using System;
using System.IO;
using GeneSys.Configuration;
using GeneSys.Simulation;
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

        public void Save(SimulationHost host, string path, Action<bool> completed = null)
        {
            if (host == null || !host.IsReady) { completed?.Invoke(false); return; }
            byte[][] payloads = new byte[5][];
            int remaining = 5;
            bool failed = false;
            RenderTexture[] textures =
            {
                host.Resources.MaterialRead, host.Resources.StateRead,
                host.Resources.FlowRead, host.Resources.AuxRead,
                host.Resources.WaterRead
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
                        writer.Write(Version3);
                        writer.Write(host.Resources.Grid.angularResolution);
                        writer.Write(host.Resources.Grid.radialResolution);
                        writer.Write(config.seed);
                        writer.Write(host.Clock.TickCount);
                        WriteConfig(writer, config);
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
            if (version < Version1 || version > Version3) return false;

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int seed = reader.ReadInt32();
            long tick = reader.ReadInt64();
            if (width != host.Resources.Grid.angularResolution || height != host.Resources.Grid.radialResolution) return false;

            host.Config.seed = seed;
            if (version >= Version2)
                ReadConfig(reader, host.Config);

            RenderTexture[] targets =
            {
                host.Resources.MaterialRead, host.Resources.StateRead,
                host.Resources.FlowRead, host.Resources.AuxRead
            };
            foreach (RenderTexture target in targets)
            {
                if (!LoadTexture(reader, width, height, target)) return false;
            }

            if (version >= Version3)
            {
                if (!LoadTexture(reader, width, height, host.Resources.WaterRead)) return false;
                host.Resources.CopyReadToWrite();
                host.RecomputeHydrostatic();
            }
            else
            {
                host.Resources.CopyReadToWrite();
                host.MigrateLegacyWater();
            }

            host.RestoreSimulationTick(tick);
            return true;
        }

        private static bool LoadTexture(BinaryReader reader, int width, int height, RenderTexture target)
        {
            int length = reader.ReadInt32();
            byte[] payload = reader.ReadBytes(length);
            if (payload.Length != length) return false;
            Texture2D staging = CreateStagingTexture(width, height, target.graphicsFormat);
            staging.LoadRawTextureData(payload);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, target);
            UnityEngine.Object.Destroy(staging);
            return true;
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
