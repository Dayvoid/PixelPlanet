using System.Linq;
using GeneSys.Materials;
using GeneSys.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class BrushSelectionTests
    {
        [Test]
        public void MaterialCatalogContainsGeologyAndDetritusButNotLife()
        {
            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry, Is.Not.Null);
            var ids = BrushSelectionCatalog.BuildChoices(BrushMode.Material, registry).Select(choice => choice.Id).ToList();

            Assert.That(ids.Contains(MaterialIds.Void), Is.True);
            Assert.That(ids.Contains(MaterialIds.Metal), Is.True);
            Assert.That(ids.Contains(MaterialIds.Soil), Is.True);
            Assert.That(ids.Contains(MaterialIds.Detritus), Is.True);
            Assert.That(ids.Contains(MaterialIds.Algae), Is.False);
            Assert.That(ids.Contains(MaterialIds.Cricket), Is.False);
            Assert.That(ids.Contains(MaterialIds.CricketEgg), Is.False);
            Assert.That(ids.Contains(BrushSelectionIds.MycoSpores), Is.False);
            Assert.That(ids.Contains(BrushSelectionIds.GrassSeeds), Is.False);
            Assert.That(ids.Contains(BrushSelectionIds.TreeSprouts), Is.False);
            Assert.That(ids.Contains(MaterialIds.Leaf), Is.False);
            Assert.That(ids.Contains(MaterialIds.Wood), Is.False);
            Assert.That(ids.Count, Is.EqualTo(15));
        }

        [Test]
        public void LifeCatalogContainsOrganismsAndVirtualSeeds()
        {
            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry, Is.Not.Null);
            var choices = BrushSelectionCatalog.BuildChoices(BrushMode.Life, registry);
            var ids = choices.Select(choice => choice.Id).ToList();

            Assert.That(ids.Contains(MaterialIds.Algae), Is.True);
            Assert.That(ids.Contains(MaterialIds.Cricket), Is.True);
            Assert.That(ids.Contains(MaterialIds.CricketEgg), Is.True);
            Assert.That(ids.Contains(BrushSelectionIds.MycoSpores), Is.True);
            Assert.That(ids.Contains(BrushSelectionIds.GrassSeeds), Is.True);
            Assert.That(ids.Contains(BrushSelectionIds.TreeSprouts), Is.True);
            Assert.That(ids.Contains(MaterialIds.Leaf), Is.False);
            Assert.That(ids.Contains(MaterialIds.Wood), Is.False);
            Assert.That(ids.Contains(MaterialIds.Detritus), Is.False);
            Assert.That(ids.Contains(MaterialIds.Soil), Is.False);
            Assert.That(ids[ids.Count - 3], Is.EqualTo(BrushSelectionIds.MycoSpores));
            Assert.That(ids[ids.Count - 2], Is.EqualTo(BrushSelectionIds.GrassSeeds));
            Assert.That(ids[ids.Count - 1], Is.EqualTo(BrushSelectionIds.TreeSprouts));
            Assert.That(BrushSelectionCatalog.IsRegistryMaterial(MaterialIds.Algae), Is.True);
            Assert.That(BrushSelectionCatalog.IsRegistryMaterial(BrushSelectionIds.MycoSpores), Is.False);
            Assert.That(BrushSelectionCatalog.IsRegistryMaterial(BrushSelectionIds.GrassSeeds), Is.False);
            Assert.That(BrushSelectionCatalog.IsRegistryMaterial(BrushSelectionIds.TreeSprouts), Is.False);
        }

        [Test]
        public void PaintBuilderRoutesLifeSelections()
        {
            var cell = new Vector2Int(4, 9);
            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Off, MaterialIds.Soil, cell, 2, 1f, out _, out _), Is.False);

            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Material, MaterialIds.Detritus, cell, 3, 1f, out var material, out bool grass), Is.True);
            Assert.That(grass, Is.False);
            Assert.That(material.materialId, Is.EqualTo(MaterialIds.Detritus));
            Assert.That(material.values.x, Is.EqualTo(0f));

            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Life, MaterialIds.Algae, cell, 0, 1.5f, out var algae, out grass), Is.True);
            Assert.That(grass, Is.False);
            Assert.That(algae.values.x, Is.EqualTo(14f));
            Assert.That(algae.values.y, Is.EqualTo(1.5f));

            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Life, MaterialIds.Cricket, cell, 1, 1f, out var cricket, out grass), Is.True);
            Assert.That(cricket.materialId, Is.EqualTo(MaterialIds.Cricket));
            Assert.That(cricket.values, Is.EqualTo(Vector4.zero));

            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Life, MaterialIds.CricketEgg, cell, 1, 1f, out var egg, out grass), Is.True);
            Assert.That(egg.materialId, Is.EqualTo(MaterialIds.CricketEgg));
            Assert.That(egg.values, Is.EqualTo(Vector4.zero));

            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Life, BrushSelectionIds.MycoSpores, cell, 2, 0.8f, out var myco, out grass), Is.True);
            Assert.That(grass, Is.False);
            Assert.That(myco.values.x, Is.EqualTo(7f));
            Assert.That(myco.values.y, Is.EqualTo(0.8f));
            Assert.That(myco.values.w, Is.EqualTo(BrushSelectionIds.MycoRandomTraitSentinel));

            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Life, BrushSelectionIds.GrassSeeds, cell, 2, 1f, out var grassCommand, out grass), Is.True);
            Assert.That(grass, Is.True);
            Assert.That(grassCommand.center, Is.EqualTo(cell));
            Assert.That(grassCommand.radius, Is.EqualTo(2));
            Assert.That(grassCommand.materialId, Is.EqualTo(MaterialIds.Soil));

            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Life, BrushSelectionIds.TreeSprouts, cell, 3, 1f, out var treeCommand, out grass), Is.True);
            Assert.That(grass, Is.False);
            Assert.That(treeCommand.center, Is.EqualTo(cell));
            Assert.That(treeCommand.radius, Is.EqualTo(3));
            Assert.That(treeCommand.materialId, Is.EqualTo(MaterialIds.Soil));
        }

        [Test]
        public void OffHeatAndIgniteModesKeepExistingGpuChannels()
        {
            var cell = new Vector2Int(1, 2);
            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Heat, MaterialIds.Soil, cell, 4, 3f, out var heat, out bool grass), Is.True);
            Assert.That(grass, Is.False);
            Assert.That(heat.values, Is.EqualTo(new Vector4(1f, 3f, 0f, 0f)));
            Assert.That(SimulationTools.TryBuildBrushCommand(BrushMode.Ignite, MaterialIds.Soil, cell, 4, 0.4f, out var ignite, out grass), Is.True);
            Assert.That(ignite.values.x, Is.EqualTo(9f));
            Assert.That(ignite.values.y, Is.EqualTo(0.4f));
        }
    }
}
