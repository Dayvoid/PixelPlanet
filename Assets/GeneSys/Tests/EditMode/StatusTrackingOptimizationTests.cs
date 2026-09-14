using GeneSys.Simulation;
using GeneSys.Validation;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class StatusTrackingOptimizationTests
    {
        [Test]
        public void DefaultStatusSampleStrideIsFour()
        {
            Assert.That(SimulationMetrics.DefaultStatusSampleStride, Is.EqualTo(4));
        }

        [Test]
        public void SnapshotFormatIncludesAllKeySystems()
        {
            var snapshot = new SimulationStatusSnapshot
            {
                AngularResolution = 1024,
                RadialResolution = 512,
                OceanCoverage = 0.425f,
                BasinCount = 3,
                SurfaceWaterMass = 1250.4,
                GroundwaterMass = 342.1,
                VaporMass = 88.5,
                TotalTrackedWaterMass = 1681.0,
                MeanTemperature = 18.5f,
                MeanPressure = 1.002f,
                MeanRelativeHumidity = 0.64f,
                CloudCover = 0.12f,
                MeanWindSpeed = 0.082f,
                BurningCellCount = 2,
                TotalFireIntensity = 1.4f,
                MeanOxygen = 0.21f,
                SootMass = 0.5,
                MeanStrain = 0.041f,
                MaxStrain = 0.180f,
                MeanOverpressure = 0.120f,
                MaxOverpressure = 0.450f,
                MeanFaultWeakness = 0.08f,
                ActiveGeoEvents = 2,
                ReleasedGeoEnergy = 14.2f,
                AffectedGeoArcFraction = 0.06f,
                TotalOrganisms = 193,
                AlgaeCount = 120,
                CricketAdults = 45,
                CricketEggs = 12,
                WaspAdults = 18,
                WaspEggs = 4,
                TreeAnchors = 8,
                TreeTotalPixels = 142
            };

            string formatted = snapshot.Format();
            Assert.That(formatted, Is.Not.Null.And.Not.Empty);

            // Verify Grid and Ocean
            Assert.That(formatted, Does.Contain("Grid 1024×512"));
            Assert.That(formatted, Does.Contain("Ocean 42.5%"));
            Assert.That(formatted, Does.Contain("3 basins"));

            // Verify Water
            Assert.That(formatted, Does.Contain("Water"));
            Assert.That(formatted, Does.Contain("surf 1250.4"));
            Assert.That(formatted, Does.Contain("ground 342.1"));
            Assert.That(formatted, Does.Contain("vapor 88.5"));
            Assert.That(formatted, Does.Contain("total 1681.0"));

            // Verify Geodynamics
            Assert.That(formatted, Does.Contain("Geo"));
            Assert.That(formatted, Does.Contain("strain 0.041"));
            Assert.That(formatted, Does.Contain("peak 0.180"));
            Assert.That(formatted, Does.Contain("melt P 0.120"));
            Assert.That(formatted, Does.Contain("peak 0.450"));
            Assert.That(formatted, Does.Contain("events 2"));
            Assert.That(formatted, Does.Contain("E: 14.2"));

            // Verify Atmosphere
            Assert.That(formatted, Does.Contain("Atmo"));
            Assert.That(formatted, Does.Contain("T 18.5°"));
            Assert.That(formatted, Does.Contain("P 1.002"));
            Assert.That(formatted, Does.Contain("RH 64%"));
            Assert.That(formatted, Does.Contain("cloud 12%"));
            Assert.That(formatted, Does.Contain("wind 0.082"));
            Assert.That(formatted, Does.Contain("fire 2"));
            Assert.That(formatted, Does.Contain("O2 0.21"));

            // Verify Life
            Assert.That(formatted, Does.Contain("Life"));
            Assert.That(formatted, Does.Contain("algae 120"));
            Assert.That(formatted, Does.Contain("cricket 45 (12 egg)"));
            Assert.That(formatted, Does.Contain("wasp 18 (4 egg)"));
            Assert.That(formatted, Does.Contain("trees 8 (142 px)"));
        }

        [Test]
        public void ComputeGeodynamicsMetricsExtractsValuesAccurately()
        {
            const int angularBins = 16;
            const int radialBins = 8;
            int totalCells = angularBins * radialBins;
            int stateSlots = totalCells * 2;

            var state = new NativeArray<Vector4>(stateSlots, Allocator.Temp);
            var events = new NativeArray<Vector4>(totalCells, Allocator.Temp);
            try
            {
                // Populate specific reservoir and kinematic values
                // SlotReservoir: (temp, overpressure, strain, melt)
                // SlotKinematics: (flowTheta, flowR, faultWeakness, activeStress)
                state[0] = new Vector4(100f, 0.25f, 0.10f, 0.05f);
                state[1] = new Vector4(0f, 0f, 0.30f, 0f);

                state[2] = new Vector4(120f, 0.75f, 0.50f, 0.15f);
                state[3] = new Vector4(0f, 0f, 0.10f, 0f);

                // Event at bin 0: (eventType, eventIntensity, depth, releasedEnergy)
                events[0] = new Vector4(1f, 0.8f, 2f, 25.5f);

                WorldGeodynamicsMetrics metrics = SimulationMetrics.ComputeGeodynamicsMetrics(state, events, angularBins, radialBins);

                Assert.That(metrics.HasNonFinite, Is.False);
                Assert.That(metrics.MaxStrain, Is.EqualTo(0.50f).Within(0.001f));
                Assert.That(metrics.MaxOverpressure, Is.EqualTo(0.75f).Within(0.001f));
                Assert.That(metrics.ActiveEventCount, Is.EqualTo(1));
                Assert.That(metrics.ReleasedEnergy, Is.EqualTo(25.5f).Within(0.001f));
                Assert.That(metrics.MeanStrain, Is.GreaterThan(0f));
                Assert.That(metrics.MeanOverpressure, Is.GreaterThan(0f));
                Assert.That(metrics.MeanFaultWeakness, Is.GreaterThan(0f));
            }
            finally
            {
                state.Dispose();
                events.Dispose();
            }
        }

        [Test]
        public void RelativeHumidityComputesExpectedSaturation()
        {
            // Low temperature should saturate easily
            float rhCold = SimulationMetrics.RelativeHumidity(0.02f, -10f, 0.01f);
            // High temperature has high saturation vapor capacity, so same vapor yields lower RH
            float rhWarm = SimulationMetrics.RelativeHumidity(0.02f, 40f, 0.01f);

            Assert.That(rhCold, Is.GreaterThan(rhWarm));
            Assert.That(rhCold, Is.InRange(0f, 1f));
            Assert.That(rhWarm, Is.InRange(0f, 1f));
        }
    }
}
