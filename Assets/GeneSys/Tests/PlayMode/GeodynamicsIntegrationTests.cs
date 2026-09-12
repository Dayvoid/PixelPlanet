using System;
using System.Collections;
using GeneSys.Materials;
using GeneSys.Persistence;
using GeneSys.Simulation;
using GeneSys.Simulation.Geodynamics;
using GeneSys.Simulation.Gpu;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class GeodynamicsIntegrationTests
    {
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

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static IEnumerator ReadMaterials(SimulationHost host, Action<uint[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<uint>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator ReadMaterialsAndAux(SimulationHost host, Action<uint[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.AuxRead, 0, auxRequest =>
                {
                    if (auxRequest.hasError) { failed = true; done = true; return; }
                    consume(materials, auxRequest.GetData<Vector4>().ToArray());
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

        private static IEnumerator ReadGeodynamics(SimulationHost host, Action<Vector4[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.GeodynamicsStateRead, stateRequest =>
            {
                if (stateRequest.hasError) { failed = true; done = true; return; }
                Vector4[] state = stateRequest.GetData<Vector4>().ToArray();
                AsyncGPUReadback.Request(host.Resources.GeodynamicsEvents, eventRequest =>
                {
                    if (eventRequest.hasError) { failed = true; done = true; return; }
                    consume(state, eventRequest.GetData<Vector4>().ToArray());
                    done = true;
                });
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

        private static IEnumerator MeasureMobile(SimulationHost host, Action<WorldMobileMetrics> assign)
        {
            bool ready = false;
            WorldMobileMetrics metrics = default;
            SimulationMetrics.MeasureMobileMassAsync(host, result =>
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

        private static void PaintField(SimulationHost host, int x, int y, float channel, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(channel, amount, 0f, 0f)
            });
        }

        private static void RestoreDefaults(SimulationHost host)
        {
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsAngularBins = 64;
            host.Config.geodynamicsRadialBins = 16;
            host.Config.geodynamicsPeriodTicks = 4;
            host.Config.geodynamicsConvectionStrength = 0.12f;
            host.Config.geodynamicsPressureBuildRate = 0.35f;
            host.Config.geodynamicsPressureLeakage = 0.08f;
            host.Config.geodynamicsHeatCoupling = 0.15f;
            host.Config.tectonicStrainGain = 0.12f;
            host.Config.tectonicStrainTransfer = 0.18f;
            host.Config.tectonicFaultHealing = 0.015f;
            host.Config.tectonicEarthquakeThreshold = 0.82f;
            host.Config.tectonicReleaseFraction = 0.22f;
            host.Config.tectonicEventFootprint = 0.06f;
            host.Config.tectonicCooldownTicks = 240;
            host.Config.tectonicMaxConcurrentEvents = 2;
            host.Config.tectonicSurfaceCoupling = 0.18f;
            host.Config.extrusionRate = 0.4f;
            host.Config.volcanicReleaseThreshold = 0.78f;
            host.Config.volcanicReleaseFraction = 0.2f;
            host.Config.volcanicSurfaceCoupling = 0.22f;
            host.Config.eruptionDriveScale = 0.55f;
            host.Config.hydrothermalReleaseThreshold = 0.7f;
            host.Config.hydrothermalHeatTransferRate = 0.35f;
            host.Config.maceMagma = false;
            host.Config.maceAsh = false;
            host.Config.validationIntervalTicks = 1000;
            host.Config.slowPassInterval = 4;
            host.Config.thermalRate = 0.35f;
        }

        private static void QuietSurface(SimulationHost host)
        {
            host.Config.eruptionDriveScale = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.erosionRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.validationIntervalTicks = 100000;
        }

        private static int CountGeologyOwned(uint[] materials)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                uint id = materials[i];
                if (id == MaterialIds.Core || id == MaterialIds.Mantle || id == MaterialIds.Basalt
                    || id == MaterialIds.Magma || id == MaterialIds.Ash || id == MaterialIds.Rock)
                    count++;
            }
            return count;
        }

        private static float ReservoirAt(Vector4[] state, int angularBin, int radialBin, int angularBins, int radialBins, int component)
        {
            int index = GeodynamicsGrid.StateIndex(angularBin, radialBin, GeodynamicsGrid.SlotReservoir, angularBins, radialBins);
            return component switch
            {
                0 => state[index].x,
                1 => state[index].y,
                2 => state[index].z,
                _ => state[index].w
            };
        }

        [UnityTest]
        public IEnumerator SameSeedWorldgenAndEvolutionAreDeterministic()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 424242;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 24);

            uint[] materialsA = null;
            WorldGeodynamicsMetrics geoA = default;
            yield return ReadMaterials(host, mats => materialsA = (uint[])mats.Clone());
            yield return MeasureGeodynamics(host, result => geoA = result);

            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 24);

            uint[] materialsB = null;
            WorldGeodynamicsMetrics geoB = default;
            yield return ReadMaterials(host, mats => materialsB = mats);
            yield return MeasureGeodynamics(host, result => geoB = result);

            Assert.That(materialsB, Is.EqualTo(materialsA));
            Assert.That(geoB.HasNonFinite, Is.False);
            Assert.That(geoB.MeanStrain, Is.EqualTo(geoA.MeanStrain).Within(1e-5f));
            Assert.That(geoB.MaxOverpressure, Is.EqualTo(geoA.MaxOverpressure).Within(1e-5f));
            Assert.That(geoB.ActiveEventCount, Is.EqualTo(geoA.ActiveEventCount));
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator EnabledLatticeBuildsInteriorReservoirs()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 7771;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.geodynamicsPressureBuildRate = 2.5f;
            host.Config.tectonicStrainGain = 1.5f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.volcanicReleaseThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            WorldGeodynamicsMetrics before = default;
            yield return MeasureGeodynamics(host, result => before = result);
            yield return Step(host, 48);
            WorldGeodynamicsMetrics after = default;
            yield return MeasureGeodynamics(host, result => after = result);

            Assert.That(after.HasNonFinite, Is.False);
            Assert.That(after.MaxOverpressure + after.MaxStrain,
                Is.GreaterThan(before.MaxOverpressure + before.MaxStrain + 0.01f));
            Assert.That(after.ActiveEventCount, Is.EqualTo(0));
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator DisabledLayerDoesNotSelectEvents()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 7772;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.geodynamicsPressureBuildRate = 4f;
            host.Config.tectonicStrainGain = 4f;
            host.Config.tectonicEarthquakeThreshold = 0.1f;
            host.Config.volcanicReleaseThreshold = 0.1f;
            host.Config.hydrothermalReleaseThreshold = 0.1f;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 48);

            WorldGeodynamicsMetrics metrics = default;
            yield return MeasureGeodynamics(host, result => metrics = result);
            Assert.That(metrics.ActiveEventCount, Is.EqualTo(0));
            Assert.That(metrics.ReleasedEnergy, Is.EqualTo(0f));
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator EarthquakeReleaseIsLocalAndRespectsBudget()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 7773;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.geodynamicsConvectionStrength = 1.2f;
            host.Config.geodynamicsPressureBuildRate = 2f;
            host.Config.tectonicStrainGain = 4f;
            host.Config.tectonicStrainTransfer = 0.8f;
            host.Config.tectonicEarthquakeThreshold = 0.1f;
            host.Config.volcanicReleaseThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            host.Config.tectonicReleaseFraction = 0.2f;
            host.Config.tectonicEventFootprint = 0.06f;
            host.Config.tectonicMaxConcurrentEvents = 2;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 32);

            WorldGeodynamicsMetrics metrics = default;
            Vector4[] state = null;
            Vector4[] events = null;
            yield return MeasureGeodynamics(host, result => metrics = result);
            yield return ReadGeodynamics(host, (geo, ev) => { state = geo; events = ev; });

            Assert.That(metrics.HasNonFinite, Is.False);
            Assert.That(metrics.ActiveEventCount, Is.GreaterThan(0));
            Assert.That(metrics.ReleasedEnergy, Is.GreaterThan(0f));

            int quakes = 0;
            float maxReleased = 0f;
            int eventAngular = -1;
            int eventRadial = -1;
            int angularBins = host.Config.geodynamicsAngularBins;
            int radialBins = host.Config.geodynamicsRadialBins;
            var strongAngles = new bool[angularBins];
            for (int a = 0; a < angularBins; a++)
            {
                for (int r = 0; r < radialBins; r++)
                {
                    int index = GeodynamicsGrid.EventIndex(a, r, angularBins, radialBins);
                    if (index >= events.Length) continue;
                    if (events[index].y <= 0.05f) continue;
                    maxReleased = Mathf.Max(maxReleased, events[index].w);
                    if (events[index].y > 0.25f)
                        strongAngles[a] = true;
                    if (Mathf.RoundToInt(events[index].x) == GeodynamicsGrid.EventTypeEarthquake)
                    {
                        quakes++;
                        eventAngular = a;
                        eventRadial = r;
                    }
                }
            }

            int strongCount = 0;
            for (int i = 0; i < strongAngles.Length; i++)
                if (strongAngles[i]) strongCount++;
            Assert.That(strongCount / (float)Mathf.Max(1, angularBins), Is.LessThanOrEqualTo(0.25f));
            Assert.That(maxReleased, Is.LessThanOrEqualTo(0.25f));
            if (quakes > 0)
            {
                int kinIndex = GeodynamicsGrid.StateIndex(eventAngular, eventRadial, GeodynamicsGrid.SlotKinematics, angularBins, radialBins);
                Assert.That(state[kinIndex].w, Is.GreaterThan(0f));
                float neighbor = ReservoirAt(state, eventAngular + 1, eventRadial, angularBins, radialBins, 2);
                Assert.That(neighbor, Is.GreaterThan(0f));
            }
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator VolcanicAndHydrothermalReleasesCanFire()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 7774;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.geodynamicsPressureBuildRate = 4f;
            host.Config.geodynamicsHeatCoupling = 2f;
            host.Config.tectonicStrainGain = 0f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.volcanicReleaseThreshold = 0.1f;
            host.Config.hydrothermalReleaseThreshold = 0.1f;
            host.Config.volcanicReleaseFraction = 0.2f;
            host.Config.tectonicCooldownTicks = 16;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 24);

            Vector4[] events = null;
            yield return ReadGeodynamics(host, (_, ev) => events = ev);
            int volcanic = 0;
            int hydrothermal = 0;
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].y <= 0.05f) continue;
                int type = Mathf.RoundToInt(events[i].x);
                if (type == GeodynamicsGrid.EventTypeVolcanic) volcanic++;
                if (type == GeodynamicsGrid.EventTypeHydrothermal) hydrothermal++;
                Assert.That(events[i].w, Is.LessThanOrEqualTo(0.25f));
            }

            Assert.That(volcanic + hydrothermal, Is.GreaterThan(0),
                "Expected a volcanic or hydrothermal release under lowered thresholds.");
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator DistantSectorsStayIsolatedFromPaintedPlume()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 7775;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.geodynamicsConvectionStrength = 0.02f;
            host.Config.geodynamicsPressureBuildRate = 0f;
            host.Config.geodynamicsHeatCoupling = 3f;
            host.Config.tectonicStrainGain = 0f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.volcanicReleaseThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.28f), 4, host.Grid.radialResolution - 8);
            for (int dy = -2; dy <= 2; dy++)
            {
                Paint(host, x, y + dy, MaterialIds.Magma);
                PaintField(host, x, y + dy, 1f, 1200f);
            }
            yield return Step(host, 32);

            int angularBins = host.Config.geodynamicsAngularBins;
            int radialBins = host.Config.geodynamicsRadialBins;
            int nearBin = GeodynamicsGrid.AngularBinOf(x, width, angularBins);
            int farBin = GeodynamicsGrid.WrapBin(nearBin + angularBins / 2, angularBins);
            int radialBin = GeodynamicsGrid.RadialBinOf(
                (float)y / Mathf.Max(1, host.Grid.radialResolution - 1),
                host.Grid.atmosphereStartRadius,
                radialBins);

            float near = 0f;
            float far = 0f;
            yield return ReadGeodynamics(host, (state, _) =>
            {
                near = ReservoirAt(state, nearBin, radialBin, angularBins, radialBins, 0);
                far = ReservoirAt(state, farBin, radialBin, angularBins, radialBins, 0);
            });

            Assert.That(near, Is.GreaterThan(far + 0.02f));
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator HydrothermalBoilConservesWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.geodynamicsLayerEnable = false;
            host.Config.thermalRate = 0.4f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.springDischargeRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.hydrothermalHeatTransferRate = 1f;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Air);
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, y, 5f, -100f);
            PaintField(host, x, y, 5f, 0.45f);
            PaintField(host, x, y, 1f, 250f);
            yield return Step(host, 1);

            double massBefore = 0d;
            float vaporBefore = 0f;
            yield return ReadMaterialsStateAux(host, (_, states, aux) =>
            {
                vaporBefore = aux[y * width + x].x;
                massBefore = states[y * width + x].z + aux[y * width + x].x + aux[y * width + x].y;
            });
            yield return Step(host, 12);
            yield return ReadMaterialsStateAux(host, (_, states, aux) =>
            {
                double massAfter = states[y * width + x].z + aux[y * width + x].x + aux[y * width + x].y;
                Assert.That(aux[y * width + x].x, Is.GreaterThan(vaporBefore + 0.01f));
                Assert.That(massAfter, Is.EqualTo(massBefore).Within(0.04d));
            });
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator ExclusiveMaceMagmaDoesNotDoubleConsume()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.geodynamicsLayerEnable = false;
            host.Config.maceMagma = true;
            host.Config.maceAsh = false;
            host.Config.maceSedimentPilot = true;
            host.Config.eruptionDriveScale = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.55f), 8, host.Grid.radialResolution - 8);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Magma);
            Paint(host, x, y + 1, MaterialIds.Air);
            yield return Step(host, 1);

            WorldMobileMetrics before = default;
            int magmaBefore = 0;
            int ashBefore = 0;
            yield return MeasureMobile(host, result => before = result);
            yield return ReadMaterials(host, mats =>
            {
                magmaBefore = Count(mats, MaterialIds.Magma);
                ashBefore = Count(mats, MaterialIds.Ash);
            });
            yield return Step(host, 12);
            WorldMobileMetrics after = default;
            int magmaAfter = 0;
            int ashAfter = 0;
            yield return MeasureMobile(host, result => after = result);
            yield return ReadMaterials(host, mats =>
            {
                magmaAfter = Count(mats, MaterialIds.Magma);
                ashAfter = Count(mats, MaterialIds.Ash);
            });

            Assert.That(after.HasNonFinite, Is.False);
            Assert.That(after.HasNegative, Is.False);
            Assert.That(magmaAfter, Is.GreaterThan(0));
            Assert.That(ashAfter, Is.EqualTo(ashBefore));
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator SnapshotV15RoundTripsLattice()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 1515;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.geodynamicsPressureBuildRate = 1.5f;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 16);

            WorldGeodynamicsMetrics live = default;
            yield return MeasureGeodynamics(host, result => live = result);
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-geo-v15.snapshot");
            var snapshots = new WorldSnapshotService();
            bool saved = false;
            snapshots.Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++) yield return null;
            Assert.That(saved, Is.True);

            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(snapshots.Load(host, path), Is.True);
            WorldGeodynamicsMetrics loaded = default;
            yield return MeasureGeodynamics(host, result => loaded = result);
            Assert.That(loaded.HasNonFinite, Is.False);
            Assert.That(loaded.MeanStrain, Is.EqualTo(live.MeanStrain).Within(1e-4f));
            Assert.That(loaded.MaxOverpressure, Is.EqualTo(live.MaxOverpressure).Within(1e-4f));
            Assert.That(loaded.ActiveEventCount, Is.EqualTo(live.ActiveEventCount));
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator LegacyV14SnapshotRebuildsLattice()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 1414;
            host.Config.geodynamicsLayerEnable = true;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 8);

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-geo-v14.snapshot");
            var snapshots = new WorldSnapshotService();
            bool saved = false;
            snapshots.Save(host, path, 14, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++) yield return null;
            Assert.That(saved, Is.True);

            host.Regenerate();
            Assert.That(snapshots.Load(host, path), Is.True);
            WorldGeodynamicsMetrics migrated = default;
            yield return MeasureGeodynamics(host, result => migrated = result);
            Assert.That(migrated.HasNonFinite, Is.False);
            Assert.That(migrated.MeanFaultWeakness, Is.GreaterThan(0f));
            Assert.That(migrated.ActiveEventCount, Is.EqualTo(0));
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator CrustSurfaceStressStaysLowUnderGeodynamics()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.tectonicStrainGain = 2f;
            host.Config.geodynamicsPressureBuildRate = 2f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.volcanicReleaseThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            host.Config.erosionRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.6f), 10, host.Grid.radialResolution - 10);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Granite);
            Paint(host, x, y + 1, MaterialIds.Air);
            yield return Step(host, 20);

            float stress = -1f;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int index = y * host.Grid.angularResolution + x;
                Assert.That(mats[index], Is.EqualTo(MaterialIds.Granite));
                stress = aux[index].w;
            });
            Assert.That(stress, Is.LessThan(0.05f));
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator StandardSoakStaysCloseToDisabledControl()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 5000;
            RestoreDefaults(host);
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 80);

            uint[] enabled = null;
            WorldGeodynamicsMetrics metrics = default;
            yield return ReadMaterials(host, mats => enabled = (uint[])mats.Clone());
            yield return MeasureGeodynamics(host, result => metrics = result);

            host.Config.geodynamicsLayerEnable = false;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 80);
            uint[] disabled = null;
            yield return ReadMaterials(host, mats => disabled = mats);

            int changed = 0;
            int geologyCells = 0;
            for (int i = 0; i < enabled.Length; i++)
            {
                bool owned = enabled[i] == MaterialIds.Core || enabled[i] == MaterialIds.Mantle
                    || enabled[i] == MaterialIds.Basalt || enabled[i] == MaterialIds.Magma
                    || enabled[i] == MaterialIds.Ash || enabled[i] == MaterialIds.Rock
                    || disabled[i] == MaterialIds.Core || disabled[i] == MaterialIds.Mantle
                    || disabled[i] == MaterialIds.Basalt || disabled[i] == MaterialIds.Magma
                    || disabled[i] == MaterialIds.Ash || disabled[i] == MaterialIds.Rock;
                if (!owned) continue;
                geologyCells++;
                if (enabled[i] != disabled[i]) changed++;
            }

            Assert.That(metrics.HasNonFinite, Is.False);
            Assert.That(metrics.AffectedAngularFraction, Is.LessThanOrEqualTo(0.1f));
            Assert.That(changed / (float)Mathf.Max(1, geologyCells), Is.LessThan(0.02f));
            Assert.That(CountGeologyOwned(enabled), Is.EqualTo(CountGeologyOwned(disabled)).Within(Mathf.Max(8, geologyCells / 50)));
            RestoreDefaults(host);
        }

        private static int Count(uint[] materials, uint id)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == id) count++;
            return count;
        }
    }
}
