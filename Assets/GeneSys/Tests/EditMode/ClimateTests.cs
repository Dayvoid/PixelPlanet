using System.IO;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Rendering;
using GeneSys.Simulation.Climate;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class ClimateTests
    {
        [Test]
        public void BinOfCoversEveryThetaAndWraps()
        {
            const int width = 64;
            const int bins = 16;
            var used = new int[bins];
            for (int theta = 0; theta < width; theta++)
            {
                int bin = ClimateGrid.BinOf(theta, width, bins);
                Assert.That(bin, Is.InRange(0, bins - 1));
                used[bin]++;
            }

            for (int bin = 0; bin < bins; bin++)
                Assert.That(used[bin], Is.GreaterThan(0));

            Assert.That(ClimateGrid.BinOf(-1, width, bins), Is.EqualTo(ClimateGrid.BinOf(width - 1, width, bins)));
            Assert.That(ClimateGrid.BinOf(width, width, bins), Is.EqualTo(ClimateGrid.BinOf(0, width, bins)));
        }

        [Test]
        public void ThetaRangeMatchesBinOf()
        {
            const int width = 256;
            const int bins = 32;
            for (int bin = 0; bin < bins; bin++)
            {
                ClimateGrid.ThetaRange(bin, width, bins, out int start, out int end);
                Assert.That(end, Is.GreaterThan(start));
                for (int theta = start; theta < end; theta++)
                    Assert.That(ClimateGrid.BinOf(theta, width, bins), Is.EqualTo(bin));
            }
        }

        [Test]
        public void ValidationPresetDisablesClimateLayer()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.climateLayerEnable = true;
            config.ApplyPreset(SimulationPreset.Validation);
            Assert.That(config.climateLayerEnable, Is.False);
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.climateBinCount, Is.InRange(8, 128));
            Assert.That(config.climateCouplePeriod, Is.InRange(1, 256));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void ComputeAndDisplayWireClimatePass()
        {
            string climate = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Climate.compute");
            Assert.That(climate.Contains("#pragma kernel ClimateAggregateColumns"));
            Assert.That(climate.Contains("#pragma kernel ClimateStep"));
            Assert.That(File.ReadAllText("Assets/GeneSys/Compute/Simulation/Weather.compute").Contains("Climate.hlsl"));
            Assert.That(File.ReadAllText("Assets/GeneSys/Compute/Simulation/Flora.compute").Contains("Climate.hlsl"));
            Assert.That(File.ReadAllText("Assets/GeneSys/Compute/Simulation/Hydrology.compute").Contains("Climate.hlsl"));
            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display.Contains("_ClimateState"));
            Assert.That(display.Contains("_OverlayMode == 28"));
            Assert.That(ClimateVisuals.OverlayMode, Is.EqualTo(28));
            string builder = File.ReadAllText("Assets/GeneSys/Editor/GeneSysProjectBuilder.cs");
            Assert.That(builder.Contains("Climate.compute"));
        }
    }
}
