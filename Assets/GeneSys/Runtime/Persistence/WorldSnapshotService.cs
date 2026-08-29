using System;
using System.Collections.Generic;
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
        public const string Extension = ".snapshot";
        public const string InvalidCharactersMessage = "Filename contains unacceptable characters.";

        private const uint Magic = 0x47535953;
        private const int Version1 = 1;
        private const int Version2 = 2;
        private const int Version3 = 3;
        private const int Version4 = 4;
        private const int Version5 = 5;
        private const int Version6 = 6;
        private const int Version7 = 7;
        private const int Version8 = 8;
        private const int Version9 = 9;
        private const int Version10 = 10;
        private const int Version11 = 11;
        private const int PayloadCountV8 = 10;
        private const int PayloadCountV9 = 16;
        private const int PayloadCountV10 = 31;
        private const int PayloadCountV11 = 38;

        private readonly string directoryOverride;
        private string resolvedDirectory;

        public string DirectoryPath => resolvedDirectory ??= string.IsNullOrWhiteSpace(directoryOverride)
            ? Path.Combine(Application.persistentDataPath, "worlds")
            : directoryOverride;

        public WorldSnapshotService(string directoryPath = null)
        {
            directoryOverride = directoryPath;
        }

        public static bool TryNormalizeFileName(string input, out string name, out string error)
        {
            name = null;
            error = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Enter a filename.";
                return false;
            }

            string trimmed = input.Trim();
            if (trimmed.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - Extension.Length).Trim();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "." || trimmed == "..")
            {
                error = "Enter a valid filename.";
                return false;
            }

            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = InvalidCharactersMessage;
                return false;
            }

            name = trimmed;
            return true;
        }

        public string GetPath(string name) => Path.Combine(DirectoryPath, name + Extension);

        public List<string> ListWorlds()
        {
            var names = new List<string>();
            if (!Directory.Exists(DirectoryPath))
                return names;

            foreach (string file in Directory.GetFiles(DirectoryPath, "*" + Extension))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public void Save(SimulationHost host, string path, Action<bool> completed = null) =>
            Save(host, path, Version11, completed);

        public void Save(SimulationHost host, string path, int version, Action<bool> completed)
        {
            if (host == null || !host.IsReady) { completed?.Invoke(false); return; }
            int writeVersion = version >= Version11 ? Version11 : (version >= Version10 ? Version10 : Version9);
            bool includeGrass = writeVersion >= Version10;
            bool includeWasp = writeVersion >= Version11;
            byte[][] payloads = new byte[PayloadCountV11][];
            int remaining = PayloadCountV9;
            bool failed = false;
            bool grassBatchStarted = !includeGrass;
            bool waspBatchStarted = !includeWasp;
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
            RenderTexture fauna = host.Resources.FaunaRead;
            for (int slice = 0; slice < 4; slice++)
            {
                int index = 10 + slice;
                int capture = slice;
                AsyncGPUReadback.Request(fauna, 0, 0, fauna.width, 0, fauna.height, capture, 1,
                    request => CompletePayload(index, request));
            }
            AsyncGPUReadback.Request(host.Resources.AcousticRead, 0, request => CompletePayload(14, request));
            AsyncGPUReadback.Request(host.Resources.AcousticPrev, 0, request => CompletePayload(15, request));

            void RequestGrassBatch()
            {
                RenderTexture grass = host.Resources.GrassRead;
                for (int slice = 0; slice < 12; slice++)
                {
                    int index = 16 + slice;
                    int capture = slice;
                    AsyncGPUReadback.Request(grass, 0, 0, grass.width, 0, grass.height, capture, 1,
                        request => CompletePayload(index, request));
                }
                RenderTexture propagule = host.Resources.PropaguleRead;
                for (int slice = 0; slice < 3; slice++)
                {
                    int index = 28 + slice;
                    int capture = slice;
                    AsyncGPUReadback.Request(propagule, 0, 0, propagule.width, 0, propagule.height, capture, 1,
                        request => CompletePayload(index, request));
                }
            }

            void RequestWaspBatch()
            {
                RenderTexture wasp = host.Resources.WaspRead;
                for (int slice = 0; slice < SimulationResources.WaspSliceCount; slice++)
                {
                    int index = PayloadCountV10 + slice;
                    int capture = slice;
                    AsyncGPUReadback.Request(wasp, 0, 0, wasp.width, 0, wasp.height, capture, 1,
                        request => CompletePayload(index, request));
                }
            }

            void CompletePayload(int index, UnityEngine.Rendering.AsyncGPUReadbackRequest request)
            {
                if (request.hasError) failed = true;
                else payloads[index] = request.GetData<byte>().ToArray();
                remaining--;
                if (remaining != 0) return;
                if (includeGrass && !grassBatchStarted)
                {
                    grassBatchStarted = true;
                    remaining = PayloadCountV10 - PayloadCountV9;
                    RequestGrassBatch();
                    return;
                }
                if (includeWasp && !waspBatchStarted)
                {
                    waspBatchStarted = true;
                    remaining = PayloadCountV11 - PayloadCountV10;
                    RequestWaspBatch();
                    return;
                }
                if (!failed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.persistentDataPath);
                    using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
                    using var writer = new BinaryWriter(stream);
                    SimulationConfig config = host.Config;
                    writer.Write(Magic);
                    writer.Write(writeVersion);
                    writer.Write(host.Resources.Grid.angularResolution);
                    writer.Write(host.Resources.Grid.radialResolution);
                    writer.Write(config.seed);
                    writer.Write(host.Clock.TickCount);
                    WriteConfig(writer, config);
                    WriteEcologyConfig(writer, config);
                    WriteFloraConfig(writer, config);
                    WriteFaunaConfig(writer, config);
                    if (includeGrass)
                        WriteGrassConfig(writer, config);
                    if (includeWasp)
                        WriteWaspConfig(writer, config);
                    int payloadCount = includeWasp ? PayloadCountV11 : (includeGrass ? PayloadCountV10 : PayloadCountV9);
                    for (int i = 0; i < payloadCount; i++)
                    {
                        writer.Write(payloads[i].Length);
                        writer.Write(payloads[i]);
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
            if (version < Version1 || version > Version11) return false;

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
                ReadFloraConfig(reader, host.Config, version);
            if (version >= Version9)
                ReadFaunaConfig(reader, host.Config);
            if (version >= Version10)
                ReadGrassConfig(reader, host.Config);
            if (version >= Version11)
                ReadWaspConfig(reader, host.Config);

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

            if (version >= Version9)
            {
                for (int slice = 0; slice < 4; slice++)
                {
                    int length = reader.ReadInt32();
                    byte[] payload = reader.ReadBytes(length);
                    if (payload.Length != length) return false;
                    Texture2D staging = CreateStagingTexture(width, height, GraphicsFormat.R32G32B32A32_SFloat);
                    staging.LoadRawTextureData(payload);
                    staging.Apply(false, false);
                    Graphics.CopyTexture(staging, 0, 0, host.Resources.FaunaRead, slice, 0);
                    UnityEngine.Object.Destroy(staging);
                }

                int acousticLength = reader.ReadInt32();
                byte[] acousticPayload = reader.ReadBytes(acousticLength);
                if (acousticPayload.Length != acousticLength) return false;
                Texture2D acousticStaging = CreateStagingTexture(width, height, host.Resources.AcousticRead.graphicsFormat);
                acousticStaging.LoadRawTextureData(acousticPayload);
                acousticStaging.Apply(false, false);
                Graphics.CopyTexture(acousticStaging, host.Resources.AcousticRead);
                UnityEngine.Object.Destroy(acousticStaging);

                int prevLength = reader.ReadInt32();
                byte[] prevPayload = reader.ReadBytes(prevLength);
                if (prevPayload.Length != prevLength) return false;
                Texture2D prevStaging = CreateStagingTexture(width, height, host.Resources.AcousticPrev.graphicsFormat);
                prevStaging.LoadRawTextureData(prevPayload);
                prevStaging.Apply(false, false);
                Graphics.CopyTexture(prevStaging, host.Resources.AcousticPrev);
                UnityEngine.Object.Destroy(prevStaging);
            }
            else
            {
                host.Resources.ClearFaunaAndAcoustic();
            }

            if (version >= Version10)
            {
                for (int slice = 0; slice < 12; slice++)
                {
                    int length = reader.ReadInt32();
                    byte[] payload = reader.ReadBytes(length);
                    if (payload.Length != length) return false;
                    Texture2D staging = CreateStagingTexture(width, height, GraphicsFormat.R32G32B32A32_SFloat);
                    staging.LoadRawTextureData(payload);
                    staging.Apply(false, false);
                    Graphics.CopyTexture(staging, 0, 0, host.Resources.GrassRead, slice, 0);
                    UnityEngine.Object.Destroy(staging);
                }

                for (int slice = 0; slice < 3; slice++)
                {
                    int length = reader.ReadInt32();
                    byte[] payload = reader.ReadBytes(length);
                    if (payload.Length != length) return false;
                    Texture2D staging = CreateStagingTexture(width, height, GraphicsFormat.R32G32B32A32_SFloat);
                    staging.LoadRawTextureData(payload);
                    staging.Apply(false, false);
                    Graphics.CopyTexture(staging, 0, 0, host.Resources.PropaguleRead, slice, 0);
                    UnityEngine.Object.Destroy(staging);
                }
            }
            else
            {
                host.Resources.ClearGrass();
            }

            if (version >= Version11)
            {
                for (int slice = 0; slice < SimulationResources.WaspSliceCount; slice++)
                {
                    int length = reader.ReadInt32();
                    byte[] payload = reader.ReadBytes(length);
                    if (payload.Length != length) return false;
                    Texture2D staging = CreateStagingTexture(width, height, GraphicsFormat.R32G32B32A32_SFloat);
                    staging.LoadRawTextureData(payload);
                    staging.Apply(false, false);
                    Graphics.CopyTexture(staging, 0, 0, host.Resources.WaspRead, slice, 0);
                    UnityEngine.Object.Destroy(staging);
                }
            }
            else
            {
                host.Resources.ClearWasp();
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
            writer.Write(config.floraPoleDriftRate);
            writer.Write(config.floraWindShearRate);
            writer.Write(config.floraRainShearRate);
            writer.Write(config.floraFragmentYield);
            writer.Write(config.floraAnchorGrip);
        }

        private static void ReadFloraConfig(BinaryReader reader, SimulationConfig config, int version)
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
            if (version >= Version8)
            {
                config.floraPoleDriftRate = reader.ReadSingle();
                config.floraWindShearRate = reader.ReadSingle();
                config.floraRainShearRate = reader.ReadSingle();
                config.floraFragmentYield = reader.ReadSingle();
                config.floraAnchorGrip = reader.ReadSingle();
            }
        }

        private static void WriteFaunaConfig(BinaryWriter writer, SimulationConfig config)
        {
            writer.Write(config.faunaSeedAtWorldgen);
            writer.Write(config.faunaInitialCalories);
            writer.Write(config.faunaInitialHydration);
            writer.Write(config.faunaMaturityTicks);
            writer.Write(config.faunaDecisionInterval);
            writer.Write(config.faunaMaintenanceRate);
            writer.Write(config.faunaHydrationDrain);
            writer.Write(config.faunaCalorieCapacity);
            writer.Write(config.faunaFullThreshold);
            writer.Write(config.faunaHungerThreshold);
            writer.Write(config.faunaReproductionCalorieThreshold);
            writer.Write(config.faunaHopImpulse);
            writer.Write(config.faunaHopCost);
            writer.Write(config.faunaFeedCost);
            writer.Write(config.faunaDryMass);
            writer.Write(config.faunaDrag);
            writer.Write(config.faunaWindResistance);
            writer.Write(config.faunaMoistureMass);
            writer.Write(config.faunaSupportBoost);
            writer.Write(config.faunaWetPenalty);
            writer.Write(config.faunaGeneExpressionRange);
            writer.Write(config.faunaBaseMutationRate);
            writer.Write(config.faunaSenseRadius);
            writer.Write(config.faunaHearingRange);
            writer.Write(config.faunaThreatTemperature);
            writer.Write(config.faunaAcousticSpeed);
            writer.Write(config.faunaAcousticDamping);
            writer.Write(config.faunaFeedCallAmplitude);
            writer.Write(config.faunaMateCallAmplitude);
            writer.Write(config.faunaMateCooldownTicks);
            writer.Write(config.faunaReproduceCooldownTicks);
            writer.Write(config.faunaClutchMin);
            writer.Write(config.faunaClutchMax);
            writer.Write(config.faunaHatchTicksMin);
            writer.Write(config.faunaHatchTicksMax);
            writer.Write(config.faunaEggDesiccationMoisture);
            writer.Write(config.faunaEggHeatDeath);
            writer.Write(config.faunaEggDisplacement);
            writer.Write(config.faunaWanderRate);
            writer.Write(config.faunaSurvivalTempMin);
            writer.Write(config.faunaSurvivalTempMax);
        }

        private static void ReadFaunaConfig(BinaryReader reader, SimulationConfig config)
        {
            config.faunaSeedAtWorldgen = reader.ReadBoolean();
            config.faunaInitialCalories = reader.ReadSingle();
            config.faunaInitialHydration = reader.ReadSingle();
            config.faunaMaturityTicks = reader.ReadInt32();
            config.faunaDecisionInterval = reader.ReadInt32();
            config.faunaMaintenanceRate = reader.ReadSingle();
            config.faunaHydrationDrain = reader.ReadSingle();
            config.faunaCalorieCapacity = reader.ReadSingle();
            config.faunaFullThreshold = reader.ReadSingle();
            config.faunaHungerThreshold = reader.ReadSingle();
            config.faunaReproductionCalorieThreshold = reader.ReadSingle();
            config.faunaHopImpulse = reader.ReadSingle();
            config.faunaHopCost = reader.ReadSingle();
            config.faunaFeedCost = reader.ReadSingle();
            config.faunaDryMass = reader.ReadSingle();
            config.faunaDrag = reader.ReadSingle();
            config.faunaWindResistance = reader.ReadSingle();
            config.faunaMoistureMass = reader.ReadSingle();
            config.faunaSupportBoost = reader.ReadSingle();
            config.faunaWetPenalty = reader.ReadSingle();
            config.faunaGeneExpressionRange = reader.ReadSingle();
            config.faunaBaseMutationRate = reader.ReadSingle();
            config.faunaSenseRadius = reader.ReadInt32();
            config.faunaHearingRange = reader.ReadSingle();
            config.faunaThreatTemperature = reader.ReadSingle();
            config.faunaAcousticSpeed = reader.ReadSingle();
            config.faunaAcousticDamping = reader.ReadSingle();
            config.faunaFeedCallAmplitude = reader.ReadSingle();
            config.faunaMateCallAmplitude = reader.ReadSingle();
            config.faunaMateCooldownTicks = reader.ReadInt32();
            config.faunaReproduceCooldownTicks = reader.ReadInt32();
            config.faunaClutchMin = reader.ReadInt32();
            config.faunaClutchMax = reader.ReadInt32();
            config.faunaHatchTicksMin = reader.ReadInt32();
            config.faunaHatchTicksMax = reader.ReadInt32();
            config.faunaEggDesiccationMoisture = reader.ReadSingle();
            config.faunaEggHeatDeath = reader.ReadSingle();
            config.faunaEggDisplacement = reader.ReadSingle();
            config.faunaWanderRate = reader.ReadSingle();
            config.faunaSurvivalTempMin = reader.ReadSingle();
            config.faunaSurvivalTempMax = reader.ReadSingle();
        }

        private static void WriteGrassConfig(BinaryWriter writer, SimulationConfig config)
        {
            writer.Write(config.grassSeedAtWorldgen);
            writer.Write(config.grassInitialBiomass);
            writer.Write(config.grassInitialEnergy);
            writer.Write(config.grassPhotosynthesisRate);
            writer.Write(config.grassGrowthRate);
            writer.Write(config.grassDecayRate);
            writer.Write(config.grassMaintenanceRate);
            writer.Write(config.grassNightDrain);
            writer.Write(config.grassWaterUptakeRate);
            writer.Write(config.grassNutrientUptakeRate);
            writer.Write(config.grassRootCohesionBonus);
            writer.Write(config.grassFlowerEnergyThreshold);
            writer.Write(config.grassGeneExpressionRange);
            writer.Write(config.grassGrowthTempMin);
            writer.Write(config.grassGrowthTempMax);
            writer.Write(config.grassGrowthMoistureMin);
            writer.Write(config.grassGrowthMoistureMax);
            writer.Write(config.grassSurvivalTempMin);
            writer.Write(config.grassSurvivalTempMax);
            writer.Write(config.grassSurvivalMoistureMin);
            writer.Write(config.grassSurvivalMoistureMax);
            writer.Write(config.grassMinLight);
            writer.Write(config.grassAdultBiomass);
            writer.Write(config.grassPollenEmitRate);
            writer.Write(config.grassPollenTransportRate);
            writer.Write(config.grassSeedTransportRate);
            writer.Write(config.grassPollenWindRate);
            writer.Write(config.grassPollenWaterRate);
            writer.Write(config.grassPollenSettlingRate);
            writer.Write(config.grassSeedWindRate);
            writer.Write(config.grassSeedWaterRate);
            writer.Write(config.grassSeedSettlingRate);
            writer.Write(config.grassNectarAmount);
            writer.Write(config.grassCanopyOpacity);
            writer.Write(config.detritusVaporAbsorbRate);
            writer.Write(config.detritusEvaporationRate);
            writer.Write(config.detritusMoistureDistributeRate);
            writer.Write(config.detritusNutrientLeachRate);
            writer.Write(config.detritusDecompositionRate);
            writer.Write(config.detritusInitialNutrient);
            writer.Write(config.detritusInitialMoisture);
        }

        private static void ReadGrassConfig(BinaryReader reader, SimulationConfig config)
        {
            config.grassSeedAtWorldgen = reader.ReadBoolean();
            config.grassInitialBiomass = reader.ReadSingle();
            config.grassInitialEnergy = reader.ReadSingle();
            config.grassPhotosynthesisRate = reader.ReadSingle();
            config.grassGrowthRate = reader.ReadSingle();
            config.grassDecayRate = reader.ReadSingle();
            config.grassMaintenanceRate = reader.ReadSingle();
            config.grassNightDrain = reader.ReadSingle();
            config.grassWaterUptakeRate = reader.ReadSingle();
            config.grassNutrientUptakeRate = reader.ReadSingle();
            config.grassRootCohesionBonus = reader.ReadSingle();
            config.grassFlowerEnergyThreshold = reader.ReadSingle();
            config.grassGeneExpressionRange = reader.ReadSingle();
            config.grassGrowthTempMin = reader.ReadSingle();
            config.grassGrowthTempMax = reader.ReadSingle();
            config.grassGrowthMoistureMin = reader.ReadSingle();
            config.grassGrowthMoistureMax = reader.ReadSingle();
            config.grassSurvivalTempMin = reader.ReadSingle();
            config.grassSurvivalTempMax = reader.ReadSingle();
            config.grassSurvivalMoistureMin = reader.ReadSingle();
            config.grassSurvivalMoistureMax = reader.ReadSingle();
            config.grassMinLight = reader.ReadSingle();
            config.grassAdultBiomass = reader.ReadSingle();
            config.grassPollenEmitRate = reader.ReadSingle();
            config.grassPollenTransportRate = reader.ReadSingle();
            config.grassSeedTransportRate = reader.ReadSingle();
            config.grassPollenWindRate = reader.ReadSingle();
            config.grassPollenWaterRate = reader.ReadSingle();
            config.grassPollenSettlingRate = reader.ReadSingle();
            config.grassSeedWindRate = reader.ReadSingle();
            config.grassSeedWaterRate = reader.ReadSingle();
            config.grassSeedSettlingRate = reader.ReadSingle();
            config.grassNectarAmount = reader.ReadSingle();
            config.grassCanopyOpacity = reader.ReadSingle();
            config.detritusVaporAbsorbRate = reader.ReadSingle();
            config.detritusEvaporationRate = reader.ReadSingle();
            config.detritusMoistureDistributeRate = reader.ReadSingle();
            config.detritusNutrientLeachRate = reader.ReadSingle();
            config.detritusDecompositionRate = reader.ReadSingle();
            config.detritusInitialNutrient = reader.ReadSingle();
            config.detritusInitialMoisture = reader.ReadSingle();
        }

        private static void WriteWaspConfig(BinaryWriter writer, SimulationConfig config)
        {
            writer.Write(config.waspSeedAtWorldgen);
            writer.Write(config.waspInitialCalories);
            writer.Write(config.waspInitialHydration);
            writer.Write(config.waspMaturityTicks);
            writer.Write(config.waspDecisionInterval);
            writer.Write(config.waspMaintenanceRate);
            writer.Write(config.waspFlightDrain);
            writer.Write(config.waspHydrationDrain);
            writer.Write(config.waspCalorieCapacity);
            writer.Write(config.waspFullThreshold);
            writer.Write(config.waspHungerThreshold);
            writer.Write(config.waspStarvationThreshold);
            writer.Write(config.waspCruiseAltitude);
            writer.Write(config.waspAltitudeGain);
            writer.Write(config.waspLiftPower);
            writer.Write(config.waspSurfaceScanRange);
            writer.Write(config.waspBodyMass);
            writer.Write(config.waspDrag);
            writer.Write(config.waspWindCoupling);
            writer.Write(config.waspUpdraftCoupling);
            writer.Write(config.waspSwoopImpulse);
            writer.Write(config.waspSenseRadius);
            writer.Write(config.waspPreyCalorieConversion);
            writer.Write(config.waspPreyHydrationTransfer);
            writer.Write(config.waspNectarDraw);
            writer.Write(config.waspNectarCalories);
            writer.Write(config.waspNectarHydration);
            writer.Write(config.waspPollenCapacity);
            writer.Write(config.waspGeneExpressionRange);
            writer.Write(config.waspBaseMutationRate);
            writer.Write(config.waspMateCooldownTicks);
            writer.Write(config.waspReproduceCooldownTicks);
            writer.Write(config.waspClutchMin);
            writer.Write(config.waspClutchMax);
            writer.Write(config.waspHatchTicksMin);
            writer.Write(config.waspHatchTicksMax);
            writer.Write(config.waspReproductionCalorieThreshold);
            writer.Write(config.waspEggDesiccationMoisture);
            writer.Write(config.waspEggHeatDeath);
            writer.Write(config.waspSurvivalTempMin);
            writer.Write(config.waspSurvivalTempMax);
            writer.Write(config.waspThreatTemperature);
        }

        private static void ReadWaspConfig(BinaryReader reader, SimulationConfig config)
        {
            config.waspSeedAtWorldgen = reader.ReadBoolean();
            config.waspInitialCalories = reader.ReadSingle();
            config.waspInitialHydration = reader.ReadSingle();
            config.waspMaturityTicks = reader.ReadInt32();
            config.waspDecisionInterval = reader.ReadInt32();
            config.waspMaintenanceRate = reader.ReadSingle();
            config.waspFlightDrain = reader.ReadSingle();
            config.waspHydrationDrain = reader.ReadSingle();
            config.waspCalorieCapacity = reader.ReadSingle();
            config.waspFullThreshold = reader.ReadSingle();
            config.waspHungerThreshold = reader.ReadSingle();
            config.waspStarvationThreshold = reader.ReadSingle();
            config.waspCruiseAltitude = reader.ReadSingle();
            config.waspAltitudeGain = reader.ReadSingle();
            config.waspLiftPower = reader.ReadSingle();
            config.waspSurfaceScanRange = reader.ReadInt32();
            config.waspBodyMass = reader.ReadSingle();
            config.waspDrag = reader.ReadSingle();
            config.waspWindCoupling = reader.ReadSingle();
            config.waspUpdraftCoupling = reader.ReadSingle();
            config.waspSwoopImpulse = reader.ReadSingle();
            config.waspSenseRadius = reader.ReadInt32();
            config.waspPreyCalorieConversion = reader.ReadSingle();
            config.waspPreyHydrationTransfer = reader.ReadSingle();
            config.waspNectarDraw = reader.ReadSingle();
            config.waspNectarCalories = reader.ReadSingle();
            config.waspNectarHydration = reader.ReadSingle();
            config.waspPollenCapacity = reader.ReadInt32();
            config.waspGeneExpressionRange = reader.ReadSingle();
            config.waspBaseMutationRate = reader.ReadSingle();
            config.waspMateCooldownTicks = reader.ReadInt32();
            config.waspReproduceCooldownTicks = reader.ReadInt32();
            config.waspClutchMin = reader.ReadInt32();
            config.waspClutchMax = reader.ReadInt32();
            config.waspHatchTicksMin = reader.ReadInt32();
            config.waspHatchTicksMax = reader.ReadInt32();
            config.waspReproductionCalorieThreshold = reader.ReadSingle();
            config.waspEggDesiccationMoisture = reader.ReadSingle();
            config.waspEggHeatDeath = reader.ReadSingle();
            config.waspSurvivalTempMin = reader.ReadSingle();
            config.waspSurvivalTempMax = reader.ReadSingle();
            config.waspThreatTemperature = reader.ReadSingle();
        }

        private static Texture2D CreateStagingTexture(int width, int height, GraphicsFormat format)
        {
            GraphicsFormat stagingFormat = format;
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample))
            {
                // D3D11 rejects Texture2D(R32_UInt) because that format is load/store only.
                // Same-size float textures copy bit-identically into the integer render targets.
                uint block = GraphicsFormatUtility.GetBlockSize(format);
                stagingFormat = block <= 4 ? GraphicsFormat.R32_SFloat
                    : block <= 8 ? GraphicsFormat.R32G32_SFloat
                    : GraphicsFormat.R32G32B32A32_SFloat;
            }

            return new Texture2D(width, height, stagingFormat, TextureCreationFlags.DontInitializePixels)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }
    }
}
