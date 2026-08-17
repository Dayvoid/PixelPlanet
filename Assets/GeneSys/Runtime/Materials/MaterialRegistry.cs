using System;
using System.Collections.Generic;
using UnityEngine;

namespace GeneSys.Materials
{
    [CreateAssetMenu(menuName = "GeneSys/Material Registry", fileName = "MaterialRegistry")]
    public sealed class MaterialRegistry : ScriptableObject
    {
        public const int MaxMaterials = 256;
        public List<MaterialDefinition> materials = new();

        public int Count
        {
            get
            {
                int max = 0;
                foreach (MaterialDefinition definition in materials)
                    if (definition != null) max = Mathf.Max(max, definition.stableId + 1);
                return Mathf.Max(1, max);
            }
        }

        public MaterialDefinition Get(int stableId)
        {
            foreach (MaterialDefinition definition in materials)
                if (definition != null && definition.stableId == stableId) return definition;
            return null;
        }

        public bool Validate(out string error)
        {
            var ids = new HashSet<int>();
            foreach (MaterialDefinition definition in materials)
            {
                if (definition == null) { error = "Registry contains a null material."; return false; }
                if (definition.stableId < 0 || definition.stableId >= MaxMaterials)
                { error = $"Material {definition.name} has invalid ID {definition.stableId}."; return false; }
                if (!ids.Add(definition.stableId))
                { error = $"Duplicate material ID {definition.stableId}."; return false; }
            }
            error = string.Empty;
            return true;
        }

        public MaterialGpuData[] BuildGpuData()
        {
            if (!Validate(out string error)) throw new InvalidOperationException(error);
            var data = new MaterialGpuData[MaxMaterials];
            foreach (MaterialDefinition definition in materials) data[definition.stableId] = definition.ToGpuData();
            return data;
        }

        public GraphicsBuffer CreateGpuBuffer()
        {
            MaterialGpuData[] data = BuildGpuData();
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, MaterialGpuData.Stride);
            buffer.SetData(data);
            return buffer;
        }
    }

    public static class MaterialIds
    {
        public const uint Void = 0;
        public const uint Air = 1;
        public const uint Core = 2;
        public const uint Mantle = 3;
        public const uint Rock = 4;
        public const uint Basalt = 5;
        public const uint Magma = 6;
        public const uint Soil = 7;
        public const uint Sediment = 8;
        public const uint Water = 9;
        public const uint Ice = 10;
        public const uint Vapor = 11;
        public const uint BiologicalStart = 128;
    }
}
