using System;
using System.IO;
using GeneSys.Simulation;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GeneSys.Persistence
{
    public sealed class WorldSnapshotService
    {
        private const uint Magic = 0x47535953;

        public void Save(SimulationHost host, string path, Action<bool> completed = null)
        {
            if (host == null || !host.IsReady) { completed?.Invoke(false); return; }
            byte[][] payloads = new byte[4][];
            int remaining = 4;
            bool failed = false;
            RenderTexture[] textures =
            {
                host.Resources.MaterialRead, host.Resources.StateRead,
                host.Resources.FlowRead, host.Resources.AuxRead
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
                        writer.Write(Magic);
                        writer.Write(1);
                        writer.Write(host.Resources.Grid.angularResolution);
                        writer.Write(host.Resources.Grid.radialResolution);
                        writer.Write(host.Config.seed);
                        writer.Write(host.Clock.TickCount);
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
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != 1) return false;
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int seed = reader.ReadInt32();
            _ = reader.ReadInt64();
            if (width != host.Resources.Grid.angularResolution || height != host.Resources.Grid.radialResolution) return false;
            host.Config.seed = seed;
            RenderTexture[] targets =
            {
                host.Resources.MaterialRead, host.Resources.StateRead,
                host.Resources.FlowRead, host.Resources.AuxRead
            };
            foreach (RenderTexture target in targets)
            {
                int length = reader.ReadInt32();
                byte[] payload = reader.ReadBytes(length);
                var texture = new Texture2D(width, height, target.graphicsFormat, TextureCreationFlags.None);
                texture.LoadRawTextureData(payload);
                texture.Apply(false, false);
                Graphics.CopyTexture(texture, target);
                UnityEngine.Object.Destroy(texture);
            }
            host.Resources.CopyReadToWrite();
            return true;
        }
    }
}
