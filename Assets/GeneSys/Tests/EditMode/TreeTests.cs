using System.IO;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GeneSys.Tests
{
    public sealed class TreeTests
    {
        [Test]
        public void TreeOnValidateEnforcesSurvivalOutsideGrowth()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.treeGrowthTempMin = 20f;
            config.treeGrowthTempMax = 10f;
            config.treeSurvivalTempMin = 12f;
            config.treeSurvivalTempMax = 15f;
            config.treeGrowthMoistureMin = 0.8f;
            config.treeGrowthMoistureMax = 0.2f;
            config.treeSproutHeight = 9;
            config.treeSaplingHeight = 4;
            config.treeMaxHeight = 8;
            config.treeSaplingBranchMin = 6;
            config.treeSaplingBranchMax = 1;
            config.treeRootCohesionBonus = 4f;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.treeGrowthTempMin, Is.LessThan(config.treeGrowthTempMax));
            Assert.That(config.treeSurvivalTempMin, Is.LessThanOrEqualTo(config.treeGrowthTempMin));
            Assert.That(config.treeSurvivalTempMax, Is.GreaterThanOrEqualTo(config.treeGrowthTempMax));
            Assert.That(config.treeSproutHeight, Is.InRange(1, 8));
            Assert.That(config.treeSaplingHeight, Is.GreaterThan(config.treeSproutHeight));
            Assert.That(config.treeMaxHeight, Is.GreaterThan(config.treeSaplingHeight));
            Assert.That(config.treeSaplingBranchMin, Is.LessThanOrEqualTo(config.treeSaplingBranchMax));
            Assert.That(config.treeRootCohesionBonus, Is.InRange(0f, 1f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void GeneEncodeDecodeCombineAndMutationStayInRange()
        {
            var mother = new TreeGenome.Packed();
            var partner = new TreeGenome.Packed();
            for (int i = 0; i < TreeGenome.GeneCount; i++)
            {
                mother = TreeGenome.EncodeGene(mother, i, (byte)(i * 18));
                partner = TreeGenome.EncodeGene(partner, i, (byte)(255 - i * 10));
            }
            mother = TreeGenome.PackMeta(mother, TreeGenome.StageTree, 4, 21, 180);
            partner = TreeGenome.PackMeta(partner, TreeGenome.StageSapling, 2, 9, 40);
            mother = TreeGenome.Sanitize(mother);
            Assert.That(TreeGenome.Stage(mother), Is.EqualTo(TreeGenome.StageTree));
            Assert.That(TreeGenome.DecodeGene(mother, 2), Is.EqualTo((byte)36));

            var mixed = TreeGenome.Combine(mother, partner, 42u);
            for (int i = 0; i < TreeGenome.GeneCount; i++)
            {
                byte gene = TreeGenome.DecodeGene(mixed, i);
                Assert.That(gene == TreeGenome.DecodeGene(mother, i) || gene == TreeGenome.DecodeGene(partner, i), Is.True);
            }

            var child = TreeGenome.Inherit(mother, partner, true, 0.2f, 1.2f, 7u);
            Assert.That(TreeGenome.Stage(child), Is.EqualTo(TreeGenome.StageSprout));
            Assert.That(TreeGenome.Lineage(child), Is.EqualTo(TreeGenome.Lineage(mother)));
            Assert.That(TreeGenome.Generation(child), Is.EqualTo(5u));
            for (int i = 0; i < TreeGenome.GeneCount; i++)
                Assert.That(TreeGenome.DecodeGene(child, i), Is.InRange(0, 255));
            Assert.That(TreeGenome.SaplingBranchCount(mother, 2, 4), Is.InRange(2, 4));
            Assert.That(TreeGenome.MatureHeightScale(mother), Is.InRange(0.75f, 1.1f));
            Assert.That(TreeGenome.LeafLifeScale(mother), Is.InRange(0.5f, 1.5f));
        }

        [Test]
        public void TopologyPackingAndInvalidStageSanitize()
        {
            uint packed = TreeGenome.PackTopology(TreeGenome.StageSapling, TreeGenome.RoleTrunk, TreeGenome.FlagAnchor, 7u);
            Assert.That(TreeGenome.TopologyStage(packed), Is.EqualTo(TreeGenome.StageSapling));
            Assert.That(TreeGenome.Role(packed), Is.EqualTo(TreeGenome.RoleTrunk));
            Assert.That(TreeGenome.Flags(packed), Is.EqualTo(TreeGenome.FlagAnchor));
            Assert.That(TreeGenome.Cause(packed), Is.EqualTo(7u));
            Assert.That(TreeGenome.IsValidRole(TreeGenome.RoleLeaf), Is.True);
            Assert.That(TreeGenome.IsValidRole(99u), Is.False);

            var genome = TreeGenome.PackMeta(default, 99u, 0, 0, 0);
            genome = TreeGenome.Sanitize(genome);
            Assert.That(TreeGenome.Stage(genome), Is.EqualTo(TreeGenome.StageEmpty));
            Assert.That(TreeGenome.IsLivingStage(TreeGenome.StageSprout), Is.True);
            Assert.That(TreeGenome.IsLivingStage(TreeGenome.StageDead), Is.False);
        }

        [Test]
        public void LeafAndWoodMaterialsMatchCombustionAndFoodRoles()
        {
            Assert.That(MaterialIds.Leaf, Is.EqualTo(134u));
            Assert.That(MaterialIds.Wood, Is.EqualTo(135u));
            MaterialDefinition leaf = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/134_Leaf.asset");
            MaterialDefinition wood = AssetDatabase.LoadAssetAtPath<MaterialDefinition>("Assets/GeneSys/Data/Materials/135_Wood.asset");
            Assert.That(leaf, Is.Not.Null);
            Assert.That(wood, Is.Not.Null);
            Assert.That(leaf.stableId, Is.EqualTo((int)MaterialIds.Leaf));
            Assert.That(wood.stableId, Is.EqualTo((int)MaterialIds.Wood));
            Assert.That(leaf.category, Is.EqualTo(MaterialCategory.Biological));
            Assert.That(wood.category, Is.EqualTo(MaterialCategory.Solid));
            Assert.That(leaf.caloricContent, Is.GreaterThan(wood.caloricContent));
            Assert.That(leaf.ignitionTemperature, Is.LessThan(wood.ignitionTemperature));
            Assert.That(wood.rigidity, Is.GreaterThan(0.7f));

            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/GeneSys/Data/MaterialRegistry.asset");
            Assert.That(registry.Validate(out string error), Is.True, error);
            Assert.That(registry.Get((int)MaterialIds.Leaf), Is.Not.Null);
            Assert.That(registry.Get((int)MaterialIds.Wood), Is.Not.Null);
        }

        [Test]
        public void ResourcesAndKernelsMatchSidecarLayout()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.TreeRead.volumeDepth, Is.EqualTo(TreeGenome.SliceCount));
            Assert.That(resources.TreeWrite.volumeDepth, Is.EqualTo(SimulationResources.TreeSliceCount));
            Assert.That(resources.TreeRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.TreeGrowthClaims.graphicsFormat, Is.EqualTo(GraphicsFormat.R32_UInt));
            Assert.That(resources.PlantRootFlux, Is.SameAs(resources.GrassRootFlux));

            ComputeShader tree = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Tree.compute");
            Assert.That(tree, Is.Not.Null);
            Assert.That(tree.FindKernel("Physiology"), Is.GreaterThanOrEqualTo(0));
            Assert.That(tree.FindKernel("ClearGrowthClaims"), Is.GreaterThanOrEqualTo(0));
            Assert.That(tree.FindKernel("ClaimGrowth"), Is.GreaterThanOrEqualTo(0));
            Assert.That(tree.FindKernel("ApplyWorld"), Is.GreaterThanOrEqualTo(0));
            Assert.That(tree.FindKernel("ApplyState"), Is.GreaterThanOrEqualTo(0));
            Assert.That(tree.FindKernel("SeedTree"), Is.GreaterThanOrEqualTo(0));
            Assert.That(tree.FindKernel("PaintTree"), Is.GreaterThanOrEqualTo(0));

            ComputeShader plants = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/PlantResources.compute");
            Assert.That(plants, Is.Not.Null);
            Assert.That(plants.FindKernel("RootDemand"), Is.GreaterThanOrEqualTo(0));
            Assert.That(plants.FindKernel("SoilDebit"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ShaderConstantsMatchCSharpLayoutsAndExcludeAttachedMotion()
        {
            string structs = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl");
            Assert.That(structs.Contains("#define TREE_LEAF_ID 134u"));
            Assert.That(structs.Contains("#define TREE_WOOD_ID 135u"));
            Assert.That(structs.Contains("TreeFuel"));
            Assert.That(structs.Contains("CountTreeRootsOnCell"));
            string motion = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/MargolusCommon.hlsl");
            Assert.That(motion.Contains("IsTreeMaterial(c.material)"));
            string builder = File.ReadAllText("Assets/GeneSys/Editor/GeneSysProjectBuilder.cs");
            Assert.That(builder.Contains("PlantResources.compute"));
            Assert.That(builder.Contains("Tree.compute"));
            Assert.That(builder.Contains("Define(134, \"Leaf\""));
            Assert.That(builder.Contains("Define(135, \"Wood\""));
            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display.Contains("_TreeTex"));
            Assert.That(display.Contains("_OverlayMode == 26"));
            Assert.That(TreeVisuals.OverlayMode, Is.EqualTo(26));
        }

        [Test]
        public void MetricsCountAnchorsAndStages()
        {
            var materials = new[] { MaterialIds.Wood, MaterialIds.Leaf, MaterialIds.Wood, MaterialIds.Soil };
            var phys = new[]
            {
                new Vector4(0.4f, 0.5f, 0.3f, 0.9f),
                new Vector4(0.2f, 0.4f, 0.2f, 0.8f),
                new Vector4(0.1f, 0.1f, 0.1f, 0.2f),
                Vector4.zero
            };
            var topology = new[]
            {
                TreeGenome.Packed.FromUint4(1, 0, TreeGenome.PackTopology(TreeGenome.StageSprout, TreeGenome.RoleRoot, TreeGenome.FlagAnchor, 0), 0),
                TreeGenome.Packed.FromUint4(1, 1, TreeGenome.PackTopology(TreeGenome.StageSapling, TreeGenome.RoleLeaf, 0, 0), 0),
                TreeGenome.Packed.FromUint4(4, 0, TreeGenome.PackTopology(TreeGenome.StageDead, TreeGenome.RoleTrunk, TreeGenome.FlagAnchor, 0), 0),
                default
            };
            var genomes = new[]
            {
                TreeGenome.PackMeta(default, TreeGenome.StageSprout, 0, 1, 0),
                TreeGenome.PackMeta(default, TreeGenome.StageSapling, 0, 1, 0),
                TreeGenome.PackMeta(default, TreeGenome.StageDead, 1, 2, 0),
                default
            };
            TreeMetrics metrics = SimulationMetrics.ComputeTreeMetrics(materials, phys, topology, genomes);
            Assert.That(metrics.PixelCount, Is.EqualTo(3));
            Assert.That(metrics.AnchorCount, Is.EqualTo(2));
            Assert.That(metrics.SproutCount, Is.EqualTo(1));
            Assert.That(metrics.SaplingCount, Is.EqualTo(1));
            Assert.That(metrics.DeadCount, Is.EqualTo(1));
            Assert.That(metrics.TotalEnergy, Is.EqualTo(0.7d).Within(0.001d));
        }
    }
}
