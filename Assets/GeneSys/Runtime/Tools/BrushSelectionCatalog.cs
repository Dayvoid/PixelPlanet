using System.Collections.Generic;
using GeneSys.Materials;

namespace GeneSys.Tools
{
    public static class BrushSelectionIds
    {
        public const uint MycoSpores = 252;
        public const uint GrassSeeds = 253;
        public const uint TreeSprouts = 251;
        public const float MycoRandomTraitSentinel = -1f;
    }

    public readonly struct BrushSelection
    {
        public BrushSelection(uint id, string label)
        {
            Id = id;
            Label = label;
        }

        public uint Id { get; }
        public string Label { get; }
    }

    public static class BrushSelectionCatalog
    {
        public static bool IsRegistryMaterial(uint id) =>
            id != BrushSelectionIds.MycoSpores && id != BrushSelectionIds.GrassSeeds && id != BrushSelectionIds.TreeSprouts;

        public static List<BrushSelection> BuildChoices(BrushMode mode, MaterialRegistry registry)
        {
            var choices = new List<BrushSelection>();
            if (registry?.materials == null) return choices;

            if (mode == BrushMode.Material)
            {
                foreach (MaterialDefinition definition in registry.materials)
                {
                    if (definition == null) continue;
                    uint id = (uint)definition.stableId;
                    if (id <= MaterialIds.Metal || id == MaterialIds.Detritus)
                        choices.Add(new BrushSelection(id, FormatRegistry(definition)));
                }
            }
            else if (mode == BrushMode.Life)
            {
                foreach (MaterialDefinition definition in registry.materials)
                {
                    if (definition == null) continue;
                    uint id = (uint)definition.stableId;
                    if (id == MaterialIds.Algae || id == MaterialIds.Cricket || id == MaterialIds.CricketEgg)
                        choices.Add(new BrushSelection(id, FormatRegistry(definition)));
                }
                choices.Add(new BrushSelection(BrushSelectionIds.MycoSpores, "Myco Spores"));
                choices.Add(new BrushSelection(BrushSelectionIds.GrassSeeds, "Grass Seeds"));
                choices.Add(new BrushSelection(BrushSelectionIds.TreeSprouts, "Tree Sprout"));
            }

            return choices;
        }

        public static int IndexOf(IReadOnlyList<BrushSelection> choices, uint id)
        {
            if (choices == null) return -1;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].Id == id) return i;
            }
            return -1;
        }

        private static string FormatRegistry(MaterialDefinition definition) =>
            $"{definition.stableId}: {definition.displayName}";
    }
}
