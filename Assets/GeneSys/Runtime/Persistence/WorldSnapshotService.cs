using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Geodynamics;
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
        private const int Version12 = 12;
        private const int Version13 = 13;
        private const int Version14 = 14;
        private const int Version15 = 15;
        private const int Version16 = 16;
        private const int PayloadCountV8 = 10;
        private const int PayloadCountV9 = 16;
        private const int PayloadCountV10 = 31;
        private const int PayloadCountV11 = 38;
        private const int PayloadCountV12 = 41;
        private const int PayloadCountV14 = 47;
        private const int PayloadCountV15 = 49;
        private const int PayloadCountV16 = 43;

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
            Save(host, path, Version16, completed);

        public void Save(SimulationHost host, string path, int version, Action<bool> completed)
        {
            if (host == null || !host.IsReady) { completed?.Invoke(false); return; }
            int writeVersion = Mathf.Clamp(version, Version1, Version16);
            bool includeGrass = writeVersion >= Version10;
            bool includeWasp = writeVersion >= Version11;
            bool includeTree = writeVersion >= Version12;
            bool includeGeodynamics = writeVersion >= Version15;
            int payloadCount = writeVersion >= Version16 ? PayloadCountV16
                : writeVersion >= Version15 ? PayloadCountV15
                : writeVersion >= Version14 ? PayloadCountV14
                : writeVersion >= Version12 ? PayloadCountV12
                : writeVersion >= Version11 ? PayloadCountV11
                : writeVersion >= Version10 ? PayloadCountV10
                : PayloadCountV9;
            byte[][] payloads = new byte[payloadCount][];
            int remaining = PayloadCountV9;
            bool failed = false;
            bool grassBatchStarted = !includeGrass;
            bool waspBatchStarted = !includeWasp;
            bool treeBatchStarted = !includeTree;
            bool geodynamicsBatchStarted = !includeGeodynamics;
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
                    int capture = Math.Min(slice, grass.volumeDepth - 1);
                    AsyncGPUReadback.Request(grass, 0, 0, grass.width, 0, grass.height, capture, 1,
                        request => CompletePayload(index, request));
                }
                RenderTexture propagule = host.Resources.PropaguleRead;
                for (int slice = 0; slice < 3; slice++)
                {
                    int index = 28 + slice;
                    int capture = Math.Min(slice, propagule.volumeDepth - 1);
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
                    int capture = Math.Min(slice, wasp.volumeDepth - 1);
                    AsyncGPUReadback.Request(wasp, 0, 0, wasp.width, 0, wasp.height, capture, 1,
                        request => CompletePayload(index, request));
                }
            }

            void RequestTreeBatch()
            {
                RenderTexture tree = host.Resources.TreeRead;
                for (int slice = 0; slice < SimulationResources.TreeSliceCount; slice++)
                {
                    int index = PayloadCountV11 + slice;
                    int capture = Math.Min(slice, tree.volumeDepth - 1);
                    AsyncGPUReadback.Request(tree, 0, 0, tree.width, 0, tree.height, capture, 1,
                        request => CompletePayload(index, request));
                }
            }

            void RequestGeodynamicsBatch()
            {
                int geoBase = writeVersion >= Version16 ? PayloadCountV12 : PayloadCountV14;
                AsyncGPUReadback.Request(host.Resources.GeodynamicsStateRead,
                    request => CompletePayload(geoBase, request));
                AsyncGPUReadback.Request(host.Resources.GeodynamicsEvents,
                    request => CompletePayload(geoBase + 1, request));
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
                if (includeTree && !treeBatchStarted)
                {
                    treeBatchStarted = true;
                    remaining = PayloadCountV12 - PayloadCountV11;
                    RequestTreeBatch();
                    return;
                }
                if (includeGeodynamics && !geodynamicsBatchStarted)
                {
                    geodynamicsBatchStarted = true;
                    remaining = 2;
                    RequestGeodynamicsBatch();
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
                    if (writeVersion >= Version13)
                        WriteConfig(writer, config);
                    else
                        WriteConfigLegacy(writer, config);
                    WriteEcologyConfig(writer, config);
                    WriteFloraConfig(writer, config);
                    WriteFaunaConfig(writer, config);
                    if (includeGrass)
                        WriteGrassConfig(writer, config);
                    if (includeWasp)
                        WriteWaspConfig(writer, config);
                    if (includeTree)
                        WriteTreeConfig(writer, config);
                    if (writeVersion >= Version13)
                        WriteJsonConfig(writer, config);
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
            if (version < Version1 || version > Version16) return false;

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int seed = reader.ReadInt32();
            long tick = reader.ReadInt64();
            if (width != host.Resources.Grid.angularResolution || height != host.Resources.Grid.radialResolution) return false;

            host.Config.seed = seed;
            if (version >= Version2)
            {
                if (version >= Version13)
                    ReadConfig(reader, host.Config);
                else
                    ReadConfigLegacy(reader, host.Config);
            }
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
            if (version >= Version12)
                ReadTreeConfig(reader, host.Config);
            if (version >= Version13)
                ReadJsonConfig(reader, host.Config);

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
                if (target == host.Resources.MaterialRead)
                    MigrateLegacyVaporPixels(payload);
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
                    if (slice < host.Resources.GrassRead.volumeDepth)
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
                    if (slice < host.Resources.PropaguleRead.volumeDepth)
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
                    if (slice < host.Resources.WaspRead.volumeDepth)
                        Graphics.CopyTexture(staging, 0, 0, host.Resources.WaspRead, slice, 0);
                    UnityEngine.Object.Destroy(staging);
                }
            }
            else
            {
                host.Resources.ClearWasp();
            }

            if (version >= Version12)
            {
                for (int slice = 0; slice < SimulationResources.TreeSliceCount; slice++)
                {
                    int length = reader.ReadInt32();
                    byte[] payload = reader.ReadBytes(length);
                    if (payload.Length != length) return false;
                    Texture2D staging = CreateStagingTexture(width, height, GraphicsFormat.R32G32B32A32_SFloat);
                    staging.LoadRawTextureData(payload);
                    staging.Apply(false, false);
                    if (slice < host.Resources.TreeRead.volumeDepth)
                        Graphics.CopyTexture(staging, 0, 0, host.Resources.TreeRead, slice, 0);
                    UnityEngine.Object.Destroy(staging);
                }
            }
            else
            {
                host.Resources.ClearTree();
            }

            if (version == Version14 || version == Version15)
            {
                const int legacyMobileSliceCount = 6;
                for (int slice = 0; slice < legacyMobileSliceCount; slice++)
                {
                    int length = reader.ReadInt32();
                    if (length < 0 || reader.BaseStream.Position + length > reader.BaseStream.Length) return false;
                    reader.BaseStream.Seek(length, SeekOrigin.Current);
                }
            }

            if (version >= Version15)
            {
                if (!TryLoadGeodynamicsPayload(reader, host.Resources.GeodynamicsStateRead, GeodynamicsGrid.StateBufferCount()))
                    return false;
                if (!TryLoadGeodynamicsPayload(reader, host.Resources.GeodynamicsEvents, GeodynamicsGrid.EventBufferCount()))
                    return false;
                host.Resources.CopyGeodynamicsReadToWrite();
            }
            else
            {
                host.Resources.ClearGeodynamics();
                host.RebuildGeodynamics(true);
                host.ClearDeepTectonicStress();
            }

            host.Resources.CopyReadToWrite();
            host.RestoreSimulationTick(tick);
            host.RebuildClimate();
            if (version < Version15)
                host.RebuildGeodynamics(true);
            return true;
        }

        private static bool TryLoadGeodynamicsPayload(BinaryReader reader, ComputeBuffer buffer, int count)
        {
            int length = reader.ReadInt32();
            byte[] payload = reader.ReadBytes(length);
            if (payload.Length != length || buffer == null)
                return false;
            int expected = count * sizeof(float) * 4;
            if (payload.Length != expected)
                return false;
            var floats = new float[count * 4];
            Buffer.BlockCopy(payload, 0, floats, 0, expected);
            var values = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                int src = i * 4;
                values[i] = new Vector4(floats[src], floats[src + 1], floats[src + 2], floats[src + 3]);
            }
            buffer.SetData(values);
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

        private static void WriteConfigLegacy(BinaryWriter writer, SimulationConfig config)
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
            writer.Write(0f);
            writer.Write(config.springDischargeRate);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(config.hydrothermalStrength);
            writer.Write(config.ventChemicalRate);
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
            writer.Write(config.springDischargeRate);
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
            config.springDischargeRate = reader.ReadSingle();
            config.hydrothermalStrength = reader.ReadSingle();
            config.ventChemicalRate = reader.ReadSingle();
        }

        private static void ReadConfigLegacy(BinaryReader reader, SimulationConfig config)
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
            reader.ReadSingle();
            config.springDischargeRate = reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadSingle();
            config.hydrothermalStrength = reader.ReadSingle();
            config.ventChemicalRate = reader.ReadSingle();
        }

        private static void WriteJsonConfig(BinaryWriter writer, SimulationConfig config)
        {
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(config, false));
            writer.Write(json.Length);
            writer.Write(json);
        }

        private static void ReadJsonConfig(BinaryReader reader, SimulationConfig config)
        {
            int length = reader.ReadInt32();
            byte[] json = reader.ReadBytes(length);
            if (json.Length != length || config == null)
                return;
            string text = Encoding.UTF8.GetString(json);
            JsonUtility.FromJsonOverwrite(text, config);
            ApplyLegacyGeologyJsonAliases(text, config);
        }

        public static void ApplyLegacyGeologyJsonAliases(string json, SimulationConfig config)
        {
            if (string.IsNullOrEmpty(json) || config == null) return;
            ApplyLegacyFloat(json, "geodynamicsPressureBuildRate", "mantlePressure", v => config.geodynamicsPressureBuildRate = v);
            ApplyLegacyFloat(json, "tectonicStrainGain", "fractureRate", v => config.tectonicStrainGain = v);
            ApplyLegacyFloat(json, "volcanicCoolingRate", "volcanicCooling", v => config.volcanicCoolingRate = v);
            ApplyLegacyFloat(json, "eruptionDriveScale", "magmaEruption", v => config.eruptionDriveScale = v);
            ApplyLegacyFloat(json, "hydrothermalNutrientRate", "hydrothermalHeatTransferRate", v => config.hydrothermalNutrientRate = v);
            ApplyLegacyFloat(json, "hydrothermalNutrientRate", "hydrothermalStrength", v => config.hydrothermalNutrientRate = v);
            ApplyLegacyFloat(json, "hydrothermalNutrientYield", "ventChemicalRate", v => config.hydrothermalNutrientYield = v);
            ApplyLegacyFloat(json, "corePulseHeat", "coreReactionMagnitude", v => config.corePulseHeat = v);
            ApplyLegacyFloat(json, "surfaceStressRecoveryRate", "stressDecayRate", v => config.surfaceStressRecoveryRate = v);
            ApplyLegacyInt(json, "tectonicFaultSeedCount", "faultCount", v => config.tectonicFaultSeedCount = v);
            ApplyLegacyInt(json, "corePulsePeriodTicks", "coreReactionFrequency", v => config.corePulsePeriodTicks = v);
        }

        private static void ApplyLegacyFloat(string json, string currentName, string legacyName, Action<float> assign)
        {
            if (json.Contains("\"" + currentName + "\"")) return;
            if (!TryReadJsonNumber(json, legacyName, out float value)) return;
            assign(value);
        }

        private static void ApplyLegacyInt(string json, string currentName, string legacyName, Action<int> assign)
        {
            if (json.Contains("\"" + currentName + "\"")) return;
            if (!TryReadJsonNumber(json, legacyName, out float value)) return;
            assign(Mathf.RoundToInt(value));
        }

        private static bool TryReadJsonNumber(string json, string name, out float value)
        {
            value = 0f;
            string token = "\"" + name + "\":";
            int index = json.IndexOf(token, StringComparison.Ordinal);
            if (index < 0) return false;
            int start = index + token.Length;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] is '.' or '-' or '+' or 'e' or 'E'))
                end++;
            return float.TryParse(json.Substring(start, end - start), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static void MigrateLegacyVaporPixels(byte[] payload)
        {
            if (payload == null)
                return;
            byte[] air = BitConverter.GetBytes(MaterialIds.Air);
            for (int i = 0; i + 3 < payload.Length; i += 4)
            {
                if (BitConverter.ToUInt32(payload, i) != MaterialIds.Vapor)
                    continue;
                Buffer.BlockCopy(air, 0, payload, i, 4);
            }
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

        private static void WriteTreeConfig(BinaryWriter writer, SimulationConfig config)
        {
            writer.Write(config.treeSeedAtWorldgen);
            writer.Write(config.treeInitialEnergy);
            writer.Write(config.treeInitialHydration);
            writer.Write(config.treeInitialNutrient);
            writer.Write(config.treeInitialHealth);
            writer.Write(config.treePhotosynthesisRate);
            writer.Write(config.treeGrowthRate);
            writer.Write(config.treeDecayRate);
            writer.Write(config.treeMaintenanceRate);
            writer.Write(config.treeNightDrain);
            writer.Write(config.treeWaterUptakeRate);
            writer.Write(config.treeNutrientUptakeRate);
            writer.Write(config.treeVascularRate);
            writer.Write(config.treeGrowthCost);
            writer.Write(config.treeWindBias);
            writer.Write(config.treeGeneExpressionRange);
            writer.Write(config.treeGrowthTempMin);
            writer.Write(config.treeGrowthTempMax);
            writer.Write(config.treeGrowthMoistureMin);
            writer.Write(config.treeGrowthMoistureMax);
            writer.Write(config.treeSurvivalTempMin);
            writer.Write(config.treeSurvivalTempMax);
            writer.Write(config.treeSurvivalMoistureMin);
            writer.Write(config.treeSurvivalMoistureMax);
            writer.Write(config.treeMinLight);
            writer.Write(config.treeSproutHeight);
            writer.Write(config.treeSaplingHeight);
            writer.Write(config.treeMaxHeight);
            writer.Write(config.treeMaxTrunkWidth);
            writer.Write(config.treeSaplingBranchMin);
            writer.Write(config.treeSaplingBranchMax);
            writer.Write(config.treeRootCohesionBonus);
            writer.Write(config.treeCanopyOpacity);
            writer.Write(config.treeExposureDamage);
            writer.Write(config.treeLeafLifeTicks);
            writer.Write(config.treeRotTicks);
            writer.Write(config.treeDisconnectTicks);
        }

        private static void ReadTreeConfig(BinaryReader reader, SimulationConfig config)
        {
            config.treeSeedAtWorldgen = reader.ReadBoolean();
            config.treeInitialEnergy = reader.ReadSingle();
            config.treeInitialHydration = reader.ReadSingle();
            config.treeInitialNutrient = reader.ReadSingle();
            config.treeInitialHealth = reader.ReadSingle();
            config.treePhotosynthesisRate = reader.ReadSingle();
            config.treeGrowthRate = reader.ReadSingle();
            config.treeDecayRate = reader.ReadSingle();
            config.treeMaintenanceRate = reader.ReadSingle();
            config.treeNightDrain = reader.ReadSingle();
            config.treeWaterUptakeRate = reader.ReadSingle();
            config.treeNutrientUptakeRate = reader.ReadSingle();
            config.treeVascularRate = reader.ReadSingle();
            config.treeGrowthCost = reader.ReadSingle();
            config.treeWindBias = reader.ReadSingle();
            config.treeGeneExpressionRange = reader.ReadSingle();
            config.treeGrowthTempMin = reader.ReadSingle();
            config.treeGrowthTempMax = reader.ReadSingle();
            config.treeGrowthMoistureMin = reader.ReadSingle();
            config.treeGrowthMoistureMax = reader.ReadSingle();
            config.treeSurvivalTempMin = reader.ReadSingle();
            config.treeSurvivalTempMax = reader.ReadSingle();
            config.treeSurvivalMoistureMin = reader.ReadSingle();
            config.treeSurvivalMoistureMax = reader.ReadSingle();
            config.treeMinLight = reader.ReadSingle();
            config.treeSproutHeight = reader.ReadInt32();
            config.treeSaplingHeight = reader.ReadInt32();
            config.treeMaxHeight = reader.ReadInt32();
            config.treeMaxTrunkWidth = reader.ReadInt32();
            config.treeSaplingBranchMin = reader.ReadInt32();
            config.treeSaplingBranchMax = reader.ReadInt32();
            config.treeRootCohesionBonus = reader.ReadSingle();
            config.treeCanopyOpacity = reader.ReadSingle();
            config.treeExposureDamage = reader.ReadSingle();
            config.treeLeafLifeTicks = reader.ReadInt32();
            config.treeRotTicks = reader.ReadInt32();
            config.treeDisconnectTicks = reader.ReadInt32();
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
