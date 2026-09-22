using System;
using System.Collections;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Climate;
using GeneSys.Simulation.Geodynamics;
using GeneSys.Simulation.Gpu;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class MantleIntegrationTests
    {
        private SimulationConfigSnapshot _configSnapshot;

        private static IEnumerator WaitForHost()
        {
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            for (int i = 0; i < 180 && host == null; i++)
            {
                yield return null;
                host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            }
            Assert.That(host, Is.Not.Null);
            for (int i = 0; i < 60 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Clock.SetRunning(false);
        }

        private IEnumerator WaitForHostAndSnapshot()
        {
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            _configSnapshot = SimulationConfigSnapshot.Capture(host.Config);
        }

        [UnityTearDown]
        public IEnumerator RestoreConfig()
        {
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            if (host != null && host.Config != null && _configSnapshot != null)
                _configSnapshot.Restore(host.Config);
            _configSnapshot = null;
            yield return null;
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static IEnumerator ReadMaterialsAndState(SimulationHost host, Action<uint[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.StateRead, 0, stateRequest =>
                {
                    if (stateRequest.hasError) { failed = true; done = true; return; }
                    consume(materials, stateRequest.GetData<Vector4>().ToArray());
                    done = true;
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator ReadMaterialsStateAux(SimulationHost host, Action<uint[], Vector4[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.StateRead, 0, stateRequest =>
                {
                    if (stateRequest.hasError) { failed = true; done = true; return; }
                    Vector4[] states = stateRequest.GetData<Vector4>().ToArray();
                    AsyncGPUReadback.Request(host.Resources.AuxRead, 0, auxRequest =>
                    {
                        if (auxRequest.hasError) { failed = true; done = true; return; }
                        consume(materials, states, auxRequest.GetData<Vector4>().ToArray());
                        done = true;
                    });
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator ReadGeodynamics(SimulationHost host, Action<Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.GeodynamicsStateRead, stateRequest =>
            {
                if (stateRequest.hasError) { failed = true; done = true; return; }
                consume(stateRequest.GetData<Vector4>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator ReadMantleField(SimulationHost host, Action<Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MantleFieldRead, 0, GraphicsFormat.R32G32B32A32_SFloat, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<Vector4>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator ReadClimate(SimulationHost host, Action<Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.ClimateState, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<Vector4>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator MeasureGeodynamics(SimulationHost host, Action<WorldGeodynamicsMetrics> assign)
        {
            bool ready = false;
            WorldGeodynamicsMetrics metrics = default;
            SimulationMetrics.MeasureGeodynamicsAsync(host, result =>
            {
                metrics = result;
                ready = true;
            });
            for (int i = 0; i < 240 && !ready; i++)
                yield return null;
            Assert.That(ready, Is.True);
            assign(metrics);
        }

        private static IEnumerator MeasureGeology(SimulationHost host, Action<WorldGeologyMetrics> assign)
        {
            bool ready = false;
            WorldGeologyMetrics metrics = default;
            SimulationMetrics.MeasureGeologyAsync(host, result =>
            {
                metrics = result;
                ready = true;
            });
            for (int i = 0; i < 240 && !ready; i++)
                yield return null;
            Assert.That(ready, Is.True);
            assign(metrics);
        }

        private static void Paint(SimulationHost host, int x, int y, uint materialId)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = materialId,
                values = Vector4.zero
            });
        }

        private static void PaintHeat(SimulationHost host, int x, int y, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(1f, amount, 0f, 0f)
            });
        }

        private static void PaintPressure(SimulationHost host, int x, int y, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(3f, amount, 0f, 0f)
            });
        }

        private static void PaintGroundwater(SimulationHost host, int x, int y, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(5f, amount, 0f, 0f)
            });
        }

        private static void QuietSurface(SimulationHost host)
        {
            host.Config.eruptionDriveScale = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.volcanicMeltRate = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.erosionRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.tectonicKinematicCoupling = 0f;
            host.Config.enableRockChunks = false;
            host.Config.validationIntervalTicks = 100000;
        }

        private static void ForceVolcanicRelease(SimulationHost host, int angularBin, int radialBin, float intensity = 0.9f, float budget = 0.2f)
        {
            var events = new Vector4[GeodynamicsGrid.EventBufferCount()];
            host.Resources.GeodynamicsEvents.GetData(events);
            int index = GeodynamicsGrid.EventIndex(
                angularBin, radialBin, host.Config.geodynamicsAngularBins, host.Config.geodynamicsRadialBins);
            events[index] = new Vector4(GeodynamicsGrid.EventTypeVolcanic, intensity, budget, budget);
            host.Resources.GeodynamicsEvents.SetData(events);
        }

        private static float SumWater(Vector4[] states, Vector4[] aux)
        {
            double sum = 0d;
            for (int i = 0; i < states.Length; i++)
                sum += Math.Max(0d, states[i].z) + Math.Max(0d, aux[i].x) + Math.Max(0d, aux[i].y);
            return (float)sum;
        }

        private static int FindExposedY(uint[] mats, int width, int height, int x)
        {
            for (int y = height - 2; y >= 1; y--)
            {
                uint material = mats[y * width + x];
                uint above = mats[(y + 1) * width + x];
                bool aboveOpen = above == MaterialIds.Void || above == MaterialIds.Air || above == MaterialIds.Vapor;
                bool solid = material != MaterialIds.Void && material != MaterialIds.Air && material != MaterialIds.Vapor;
                if (aboveOpen && solid)
                    return y;
            }
            return Mathf.Clamp(height / 2, 1, height - 2);
        }

        private static int Count(uint[] materials, uint id)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == id) count++;
            return count;
        }

        [UnityTest]
        public IEnumerator PlumeRaisesFineMantleTemperatureUnderUpwelling()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 4401;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.mantleLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.slowPassInterval = 1;
            host.Config.mantlePlumeHeat = 2f;
            host.Config.mantleConvectionCells = 4;
            host.Config.thermalRate = 0f;
            host.Config.coreHeatRate = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.climateLayerEnable = false;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 80);

            int angularBins = host.Config.geodynamicsAngularBins;
            int radialBins = host.Config.geodynamicsRadialBins;
            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int midRadial = radialBins / 2;
            Vector4[] lattice = null;
            yield return ReadGeodynamics(host, state => lattice = state);
            int hotBin = 0;
            int coldBin = 0;
            float hotPlume = float.NegativeInfinity;
            float coldPlume = float.PositiveInfinity;
            for (int a = 0; a < angularBins; a++)
            {
                float plume = lattice[GeodynamicsGrid.StateIndex(a, midRadial, GeodynamicsGrid.SlotMantle, angularBins, radialBins)].x;
                if (plume > hotPlume) { hotPlume = plume; hotBin = a; }
                if (plume < coldPlume) { coldPlume = plume; coldBin = a; }
            }
            Assert.That(hotPlume, Is.GreaterThan(coldPlume + 0.2f));

            GeodynamicsGrid.ThetaRange(hotBin, width, angularBins, out int hotStart, out int hotEnd);
            GeodynamicsGrid.ThetaRange(coldBin, width, angularBins, out int coldStart, out int coldEnd);
            GeodynamicsGrid.RadialRange(midRadial, height, host.Grid.atmosphereStartRadius, radialBins, out int y0, out int y1);
            float hotT = 0f;
            float coldT = 0f;
            int hotCount = 0;
            int coldCount = 0;
            yield return ReadMaterialsAndState(host, (mats, states) =>
            {
                for (int y = y0; y < y1; y++)
                {
                    for (int x = hotStart; x < hotEnd; x++)
                    {
                        if (mats[y * width + x] != MaterialIds.Mantle) continue;
                        hotT += states[y * width + x].x;
                        hotCount++;
                    }
                    for (int x = coldStart; x < coldEnd; x++)
                    {
                        if (mats[y * width + x] != MaterialIds.Mantle) continue;
                        coldT += states[y * width + x].x;
                        coldCount++;
                    }
                }
            });
            Assert.That(hotCount, Is.GreaterThan(0));
            Assert.That(coldCount, Is.GreaterThan(0));
            Assert.That(hotT / hotCount, Is.GreaterThan(coldT / coldCount + 4f));
        }

        [UnityTest]
        public IEnumerator GeothermalInjectorCreatesSurfaceTemperatureContrastWithSolarOff()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 4402;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.mantleLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.slowPassInterval = 1;
            host.Config.solarIntensity = 0f;
            host.Config.thermalRate = 0f;
            host.Config.coreHeatRate = 0f;
            host.Config.climateLayerEnable = false;
            host.Config.geothermalSurfaceGain = 2f;
            host.Config.mantlePlumeHeat = 2f;
            host.Config.enableRockChunks = false;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 48);

            int angularBins = host.Config.geodynamicsAngularBins;
            int radialBins = host.Config.geodynamicsRadialBins;
            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            Vector4[] lattice = null;
            yield return ReadGeodynamics(host, state => lattice = state);
            int hotBin = 0;
            int coldBin = 0;
            float hotFlux = float.NegativeInfinity;
            float coldFlux = float.PositiveInfinity;
            for (int a = 0; a < angularBins; a++)
            {
                float flux = 0f;
                for (int r = 0; r < radialBins; r++)
                    flux += lattice[GeodynamicsGrid.StateIndex(a, r, GeodynamicsGrid.SlotMantle, angularBins, radialBins)].w;
                flux /= Mathf.Max(1, radialBins);
                if (flux > hotFlux) { hotFlux = flux; hotBin = a; }
                if (flux < coldFlux) { coldFlux = flux; coldBin = a; }
            }
            Assert.That(hotFlux, Is.GreaterThan(coldFlux + 0.02f));

            GeodynamicsGrid.ThetaRange(hotBin, width, angularBins, out int hotStart, out int hotEnd);
            GeodynamicsGrid.ThetaRange(coldBin, width, angularBins, out int coldStart, out int coldEnd);
            float hotT = 0f;
            float coldT = 0f;
            int hotCount = 0;
            int coldCount = 0;
            yield return ReadMaterialsAndState(host, (mats, states) =>
            {
                for (int x = hotStart; x < hotEnd; x++)
                {
                    int y = FindExposedY(mats, width, height, x);
                    hotT += states[y * width + x].x;
                    hotCount++;
                }
                for (int x = coldStart; x < coldEnd; x++)
                {
                    int y = FindExposedY(mats, width, height, x);
                    coldT += states[y * width + x].x;
                    coldCount++;
                }
            });
            Assert.That(hotCount, Is.GreaterThan(0));
            Assert.That(coldCount, Is.GreaterThan(0));
            Assert.That(hotT / hotCount, Is.GreaterThan(coldT / coldCount + 0.15f));
        }

        [UnityTest]
        public IEnumerator ClimateSlabPicksUpGeothermalWithZeroWaterDelta()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 4403;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.mantleLayerEnable = true;
            host.Config.climateLayerEnable = true;
            host.Config.climateCouplePeriod = 1;
            host.Config.climateSlabRadiativeCooling = 0f;
            host.Config.geothermalClimateGain = 2f;
            host.Config.geothermalSurfaceGain = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.enableRockChunks = false;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            host.RebuildClimate();
            for (int i = 0; i < 4; i++) yield return null;
            yield return Step(host, 16);

            float waterOn = 0f;
            yield return ReadMaterialsStateAux(host, (_, states, aux) => waterOn = SumWater(states, aux));
            float meanSlab = 0f;
            int bins = ClimateGrid.ClampBinCount(host.Config.climateBinCount);
            yield return ReadClimate(host, state =>
            {
                for (int bin = 0; bin < bins; bin++)
                    meanSlab += state[ClimateGrid.StateIndex(bin, ClimateGrid.StateMemory)].x;
                meanSlab /= bins;
            });

            host.Config.geothermalClimateGain = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            host.RebuildClimate();
            for (int i = 0; i < 4; i++) yield return null;
            yield return Step(host, 16);
            float waterOff = 0f;
            yield return ReadMaterialsStateAux(host, (_, states, aux) => waterOff = SumWater(states, aux));
            float meanSlabOff = 0f;
            yield return ReadClimate(host, state =>
            {
                for (int bin = 0; bin < bins; bin++)
                    meanSlabOff += state[ClimateGrid.StateIndex(bin, ClimateGrid.StateMemory)].x;
                meanSlabOff /= bins;
            });
            Assert.That(waterOn, Is.EqualTo(waterOff).Within(1e-3f));
            Assert.That(meanSlab, Is.GreaterThan(meanSlabOff + 0.05f));
        }

        [UnityTest]
        public IEnumerator ConduitMaturitySurvivesEventExhaustionAndReusesColumn()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 4404;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.mantleLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.slowPassInterval = 1;
            host.Config.mantleConduitMemory = 0.01f;
            host.Config.mantleConduitReuse = 1f;
            host.Config.volcanicReleaseThreshold = 2f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.atmosphereStartRadius * height * 0.55f), 6, height - 8);
            for (int dy = 0; dy < 6; dy++)
            {
                Paint(host, x, y + dy, MaterialIds.Magma);
                PaintHeat(host, x, y + dy, 1100f);
            }
            yield return Step(host, 8);

            float maturity = 0f;
            yield return ReadMantleField(host, field =>
            {
                int count = field != null ? field.Length : 0;
                for (int dy = 0; dy < 6; dy++)
                {
                    int yy = y + dy;
                    int index = yy * width + x;
                    if (yy < 0 || yy >= height || index < 0 || index >= count) continue;
                    maturity = Mathf.Max(maturity, field[index].x);
                }
            });
            if (maturity < 0.05f)
            {
                int angularGuess = GeodynamicsGrid.AngularBinOf(x, width, host.Config.geodynamicsAngularBins);
                int radialGuess = GeodynamicsGrid.RadialBinOf(
                    (y + 0.5f) / Mathf.Max(1, height - 1), host.Grid.atmosphereStartRadius, host.Config.geodynamicsRadialBins);
                yield return ReadGeodynamics(host, lattice =>
                {
                    maturity = Mathf.Max(maturity, lattice[GeodynamicsGrid.StateIndex(
                        angularGuess, radialGuess, GeodynamicsGrid.SlotMantle,
                        host.Config.geodynamicsAngularBins, host.Config.geodynamicsRadialBins)].z);
                });
            }
            Assert.That(maturity, Is.GreaterThan(0.05f));

            var events = new Vector4[GeodynamicsGrid.EventBufferCount()];
            host.Resources.GeodynamicsEvents.SetData(events);
            yield return Step(host, 12);

            float later = 0f;
            yield return ReadMantleField(host, field =>
            {
                int count = field != null ? field.Length : 0;
                for (int dy = 0; dy < 6; dy++)
                {
                    int yy = y + dy;
                    int index = yy * width + x;
                    if (yy < 0 || yy >= height || index < 0 || index >= count) continue;
                    later = Mathf.Max(later, field[index].x);
                }
            });
            if (later < 0.02f)
            {
                int angularGuess = GeodynamicsGrid.AngularBinOf(x, width, host.Config.geodynamicsAngularBins);
                int radialGuess = GeodynamicsGrid.RadialBinOf(
                    (y + 0.5f) / Mathf.Max(1, height - 1), host.Grid.atmosphereStartRadius, host.Config.geodynamicsRadialBins);
                yield return ReadGeodynamics(host, lattice =>
                {
                    later = Mathf.Max(later, lattice[GeodynamicsGrid.StateIndex(
                        angularGuess, radialGuess, GeodynamicsGrid.SlotMantle,
                        host.Config.geodynamicsAngularBins, host.Config.geodynamicsRadialBins)].z);
                });
            }
            Assert.That(later, Is.GreaterThan(0.02f));

            int angularBin = GeodynamicsGrid.AngularBinOf(x, width, host.Config.geodynamicsAngularBins);
            int radialBin = GeodynamicsGrid.RadialBinOf(
                (y + 0.5f) / Mathf.Max(1, height - 1), host.Grid.atmosphereStartRadius, host.Config.geodynamicsRadialBins);
            host.Config.volcanicMeltRate = 2f;
            host.Config.eruptionDriveScale = 0.55f;
            ForceVolcanicRelease(host, angularBin, radialBin, 1f, 0.35f);
            yield return Step(host, 16);

            int magma = 0;
            yield return ReadMaterialsAndState(host, (mats, _) =>
            {
                for (int yy = 0; yy < height; yy++)
                    if (mats[yy * width + x] == MaterialIds.Magma) magma++;
            });
            Assert.That(magma, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator ExplosiveSurfaceEruptionYieldsTephraCone()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1;
            host.Config.transportPassInterval = 1;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.climateLayerEnable = false;
            host.Config.volcanicCooling = 0.05f;
            host.Config.gravityStrength = 1f;
            host.Config.enableMaterialTransport = true;
            host.Config.magmaViscosity = 1.8f;
            host.Config.magmaEruption = 1f;
            host.Config.eruptionPressureStrength = 2f;
            host.Config.eruptionFlowStrength = 2f;
            host.Config.eruptionBurdenDepth = 3;
            host.Config.eruptionBlastThreshold = 0.2f;
            host.Config.eruptionTephraFraction = 1f;
            host.Config.tectonicSurfaceCoupling = 1f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.pressureRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.enableRockChunks = false;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int magmaY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.58f), 10, host.Grid.radialResolution - 10);
            for (int dx = -6; dx <= 6; dx++)
            {
                Paint(host, x + dx, magmaY - 1, MaterialIds.Rock);
                Paint(host, x + dx, magmaY, MaterialIds.Sediment);
                Paint(host, x + dx, magmaY + 1, MaterialIds.Sediment);
                Paint(host, x + dx, magmaY + 2, MaterialIds.Air);
                Paint(host, x + dx, magmaY + 3, MaterialIds.Air);
            }
            Paint(host, x, magmaY, MaterialIds.Magma);
            PaintHeat(host, x, magmaY, 760f);
            PaintPressure(host, x, magmaY, 14f);
            PaintGroundwater(host, x, magmaY + 1, 0.7f);
            yield return Step(host, 24);

            int tephra = 0;
            int ventHeight = 0;
            float neighborHeight = 0f;
            yield return ReadMaterialsAndState(host, (mats, _) =>
            {
                tephra = Count(mats, MaterialIds.Tephra);
                int height = host.Grid.radialResolution;
                ventHeight = FindExposedY(mats, width, height, x);
                int n = 0;
                for (int dx = 3; dx <= 6; dx++)
                {
                    neighborHeight += FindExposedY(mats, width, height, x + dx);
                    neighborHeight += FindExposedY(mats, width, height, x - dx);
                    n += 2;
                }
                neighborHeight /= Mathf.Max(1, n);
            });
            Assert.That(tephra, Is.GreaterThan(0));
            Assert.That(ventHeight, Is.GreaterThan(neighborHeight - 0.1f));
        }

        [UnityTest]
        public IEnumerator ExtremeVolcanismWithTubesStaysAtOrBelowSafetyLimit()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.ApplyPreset(SimulationPreset.Validation);
            host.Config.seed = 4405;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.mantleLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.slowPassInterval = 1;
            host.Config.geodynamicsPressureBuildRate = 4f;
            host.Config.geodynamicsHeatCoupling = 3f;
            host.Config.volcanicReleaseThreshold = 0.1f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            host.Config.tectonicMaxConcurrentEvents = 8;
            host.Config.tectonicCooldownTicks = 16;
            host.Config.volcanicMeltRate = 4f;
            host.Config.volcanicMagmaFractionLimit = 0.12f;
            host.Config.mantleConduitReuse = 1f;
            host.Config.mantlePlumeHeat = 0.8f;
            host.Config.enableRockChunks = false;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 96);

            WorldGeodynamicsMetrics geo = default;
            WorldGeologyMetrics geology = default;
            yield return MeasureGeodynamics(host, result => geo = result);
            yield return MeasureGeology(host, result => geology = result);
            Assert.That(geo.HasNonFinite, Is.False);
            Assert.That(geology.HasNonFinite, Is.False);
            Assert.That(geology.MagmaFraction, Is.LessThanOrEqualTo(host.Config.volcanicMagmaFractionLimit + 0.01f));
            Assert.That(geo.ActiveVolcanicCount, Is.LessThanOrEqualTo(host.Config.tectonicMaxConcurrentEvents));
        }

        [UnityTest]
        public IEnumerator SameSeedMantleEvolutionIsDeterministic()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 4406;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.mantleLayerEnable = true;
            host.Config.validationIntervalTicks = 100000;
            host.Config.enableRockChunks = false;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 24);

            uint[] materialsA = null;
            Vector4[] latticeA = null;
            Vector4[] fieldA = null;
            yield return ReadMaterialsAndState(host, (mats, _) => materialsA = (uint[])mats.Clone());
            yield return ReadGeodynamics(host, state => latticeA = (Vector4[])state.Clone());
            yield return ReadMantleField(host, field => fieldA = (Vector4[])field.Clone());

            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 24);

            yield return ReadMaterialsAndState(host, (mats, _) => Assert.That(mats, Is.EqualTo(materialsA)));
            yield return ReadGeodynamics(host, state => Assert.That(state, Is.EqualTo(latticeA)));
            yield return ReadMantleField(host, field => Assert.That(field, Is.EqualTo(fieldA)));
        }

        private sealed class SimulationConfigSnapshot
        {
            private readonly FieldInfo[] fields;
            private readonly object[] values;

            private SimulationConfigSnapshot(FieldInfo[] fields, object[] values)
            {
                this.fields = fields;
                this.values = values;
            }

            public static SimulationConfigSnapshot Capture(SimulationConfig config)
            {
                FieldInfo[] fields = typeof(SimulationConfig).GetFields(BindingFlags.Instance | BindingFlags.Public);
                var values = new object[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                    values[i] = fields[i].GetValue(config);
                return new SimulationConfigSnapshot(fields, values);
            }

            public void Restore(SimulationConfig config)
            {
                for (int i = 0; i < fields.Length; i++)
                    fields[i].SetValue(config, values[i]);
            }
        }
    }
}
