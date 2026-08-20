using System.Reflection;
using GeneSys.Configuration;
using GeneSys.UI;
using NUnit.Framework;

namespace GeneSys.Tests
{
    public sealed class SimulationSettingTooltipTests
    {
        [Test]
        public void EveryPublicSimulationConfigFieldHasATooltip()
        {
            foreach (FieldInfo field in typeof(SimulationConfig).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                Assert.That(SimulationSettingTooltips.TryGet(field.Name, out string tooltip), Is.True,
                    $"Missing tooltip for SimulationConfig.{field.Name}");
                Assert.That(tooltip, Is.Not.Null.And.Not.Empty, field.Name);
                Assert.That(tooltip.Length, Is.GreaterThan(40), field.Name);
            }
        }

        [Test]
        public void MaterialPropertyFieldsShownInToolsHaveTooltips()
        {
            foreach (FieldInfo field in typeof(GeneSys.Materials.MaterialDefinition).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.Name is nameof(GeneSys.Materials.MaterialDefinition.stableId)
                    or nameof(GeneSys.Materials.MaterialDefinition.displayColor))
                    continue;
                if (field.FieldType != typeof(float) && field.FieldType != typeof(int) && field.FieldType != typeof(bool))
                    continue;
                Assert.That(SimulationSettingTooltips.TryGetMaterial(field.Name, out string tooltip), Is.True,
                    $"Missing tooltip for MaterialDefinition.{field.Name}");
                Assert.That(tooltip.Length, Is.GreaterThan(40), field.Name);
            }
        }
    }
}
