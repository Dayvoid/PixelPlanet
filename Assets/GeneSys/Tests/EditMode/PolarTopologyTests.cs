using System.Runtime.InteropServices;
using GeneSys.Materials;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class PolarTopologyTests
    {
        [Test]
        public void ThetaWrappingIsContinuous()
        {
            PolarGridDefinition grid = PolarGridDefinition.Validation;
            Assert.That(grid.WrapTheta(-1), Is.EqualTo(grid.angularResolution - 1));
            Assert.That(grid.WrapTheta(grid.angularResolution), Is.EqualTo(0));
        }

        [Test]
        public void DisplayMappingRoundTripsOutsideCore()
        {
            PolarGridDefinition grid = PolarGridDefinition.Validation;
            for (int i = 1; i < 10; i++)
            {
                float source = Mathf.Lerp(grid.visualCoreRadius, 1f, i / 10f);
                float display = PolarCoordinateTransforms.SimulationRadiusToDisplayRadius(grid, source);
                float restored = PolarCoordinateTransforms.DisplayRadiusToSimulationRadius(grid, display);
                Assert.That(restored, Is.EqualTo(source).Within(0.0001f));
            }
        }

        [Test]
        public void PolarAreaWeightIncreasesOutward()
        {
            PolarGridDefinition grid = PolarGridDefinition.Validation;
            Assert.That(grid.CellAreaWeight(grid.radialResolution - 1), Is.GreaterThan(grid.CellAreaWeight(1)));
        }

        [Test]
        public void GpuMaterialLayoutMatchesHlslContract()
        {
            Assert.That(Marshal.SizeOf<MaterialGpuData>(), Is.EqualTo(MaterialGpuData.Stride));
            Assert.That(MaterialGpuData.Stride, Is.EqualTo(112));
        }
    }
}
