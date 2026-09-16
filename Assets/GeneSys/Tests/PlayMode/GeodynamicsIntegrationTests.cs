using System;
using System.Collections;
using System.Collections.Generic;
using GeneSys.Configuration;
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
            host.Config.tectonicKinematicCoupling = 0.15f;
            host.Config.tectonicUpliftScale = 1f;
            host.Config.tectonicConvergenceScale = 1f;
            host.Config.tectonicDisplacementScale = 0.5f;
            host.Config.tectonicCoseismicScale = 0.35f;
            host.Config.tectonicIsostasyScale = 2f;
            host.Config.extrusionRate = 0.4f;
            host.Config.volcanicReleaseThreshold = 0.78f;
            host.Config.volcanicReleaseFraction = 0.2f;
            host.Config.volcanicMeltRate = 0.28f;
            host.Config.volcanicMagmaFractionLimit = 0.2f;
            host.Config.eruptionDriveScale = 0.55f;
            host.Config.hydrothermalReleaseThreshold = 0.7f;
            host.Config.hydrothermalNutrientRate = 0.35f;
            host.Config.validationIntervalTicks = 1000;
            host.Config.slowPassInterval = 4;
            host.Config.thermalRate = 0.35f;
            host.Config.coreHeatRate = 0.15f;
            host.Config.volcanicCoolingRate = 0.15f;
            host.Config.enableRockChunks = false;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.hydrothermalNutrientRate = 1f;
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

        private static int FindMaterialXNear(uint[] materials, uint id, int markerX, int width, int yMin, int yMax)
        {
            int yLo = Mathf.Max(0, yMin);
            int yHi = Mathf.Min(materials.Length / Mathf.Max(1, width) - 1, yMax);
            for (int y = yHi; y >= yLo; y--)
            {
                for (int dx = -16; dx <= 16; dx++)
                {
                    int x = markerX + dx;
                    if (x < 0) x += width;
                    else if (x >= width) x -= width;
                    int dist = Mathf.Abs(dx);
                    if (dist <= 16 && materials[y * width + x] == id)
                        return x;
                }
            }
            return -1;
        }

        private static int CountInColumn(uint[] materials, uint id, int x, int width, int height)
        {
            int count = 0;
            for (int y = 0; y < height; y++)
                if (materials[y * width + x] == id)
                    count++;
            return count;
        }

        private static int LidSurfaceY(uint[] materials, int x, int width, int height)
        {
            for (int y = height - 1; y >= 0; y--)
            {
                uint id = materials[y * width + x];
                if (id == MaterialIds.Rock || id == MaterialIds.Basalt || id == MaterialIds.Soil
                    || id == MaterialIds.Sediment || id == MaterialIds.Ice || id == MaterialIds.Metal
                    || id == MaterialIds.Limestone || id == MaterialIds.Clay)
                    return y;
            }
            return -1;
        }

        private static void RestoreTransport(SimulationHost host, bool transport)
        {
            host.Config.enableMaterialTransport = transport;
        }

        private static void ConfigureKinematics(SimulationHost host)
        {
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.geodynamicsConvectionStrength = 0f;
            host.Config.geodynamicsHeatCoupling = 0f;
            host.Config.geodynamicsPressureBuildRate = 0f;
            host.Config.tectonicStrainGain = 0f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.volcanicReleaseThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            host.Config.tectonicKinematicCoupling = 1f;
            host.Config.tectonicUpliftScale = 4f;
            host.Config.tectonicConvergenceScale = 4f;
            host.Config.tectonicDisplacementScale = 4f;
            host.Config.tectonicCoseismicScale = 0f;
            host.Config.tectonicIsostasyScale = 2f;
            host.Config.enableMaterialTransport = false;
            host.Config.volcanicCoolingRate = 0f;
            host.Config.slowPassInterval = 1000;
            host.Config.coreHeatRate = 0f;
            host.Config.thermalRate = 0f;
            QuietSurface(host);
            host.Config.tectonicKinematicCoupling = 1f;
        }

        private static int PaintedLidThickness(SimulationHost host, int y0)
        {
            int height = host.Grid.radialResolution;
            int atmosphereY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.atmosphereStartRadius * (height - 1)), 1, height - 1);
            int refCells = Mathf.Max(6, Mathf.RoundToInt(host.Config.crustRatio * (height - 1)));
            return Mathf.Clamp(refCells, 6, Mathf.Max(6, atmosphereY - y0 - 4));
        }

        private static void ForceKinematics(SimulationHost host, float flowX, float flowY, float overpressure = 0f, int centerX = -1, int sectorBins = 3)
        {
            int angularBins = host.Config.geodynamicsAngularBins;
            int radialBins = host.Config.geodynamicsRadialBins;
            int width = host.Grid.angularResolution;
            int centerBin = centerX >= 0
                ? GeodynamicsGrid.AngularBinOf(centerX, width, angularBins)
                : -1;
            var state = new Vector4[GeodynamicsGrid.StateBufferCount()];
            host.Resources.GeodynamicsStateRead.GetData(state);
            for (int a = 0; a < angularBins; a++)
            {
                int delta = centerBin < 0 ? 0 : Mathf.Min(Mathf.Abs(a - centerBin), angularBins - Mathf.Abs(a - centerBin));
                bool inSector = centerBin < 0 || delta <= sectorBins;
                for (int r = 0; r < radialBins; r++)
                {
                    int res = GeodynamicsGrid.StateIndex(a, r, GeodynamicsGrid.SlotReservoir, angularBins, radialBins);
                    int kin = GeodynamicsGrid.StateIndex(a, r, GeodynamicsGrid.SlotKinematics, angularBins, radialBins);
                    Vector4 reservoir = state[res];
                    reservoir.x = 0f;
                    reservoir.y = inSector ? overpressure : 0f;
                    reservoir.z = 0f;
                    reservoir.w = 0f;
                    state[res] = reservoir;
                    Vector4 kinematics = state[kin];
                    kinematics.x = inSector ? flowX : 0f;
                    kinematics.y = inSector ? flowY : 0f;
                    state[kin] = kinematics;
                }
            }
            host.Resources.GeodynamicsStateRead.SetData(state);
            host.Resources.GeodynamicsStateWrite.SetData(state);
        }

        private static void PaintLidColumn(SimulationHost host, int x, int y0)
        {
            int height = host.Grid.radialResolution;
            int thickness = PaintedLidThickness(host, y0);
            for (int y = Mathf.Max(1, y0 - 8); y < y0; y++)
                Paint(host, x, y, MaterialIds.Mantle);
            for (int y = y0; y < y0 + thickness && y < height; y++)
                Paint(host, x, y, MaterialIds.Rock);
            for (int y = y0 + thickness; y < height; y++)
                Paint(host, x, y, MaterialIds.Air);
        }

        [UnityTest]
        public IEnumerator ForcedUpliftRaisesSolidEdgeAndKeepsCoreCount()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 8801;
            bool transport = host.Config.enableMaterialTransport;
            ConfigureKinematics(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int y0 = Mathf.Clamp(Mathf.RoundToInt(height * 0.58f), 12, height - 12);
            PaintLidColumn(host, x, y0);
            yield return Step(host, 1);

            int surfaceBefore = 0;
            int coreBefore = 0;
            yield return ReadMaterials(host, mats =>
            {
                surfaceBefore = LidSurfaceY(mats, x, width, height);
                coreBefore = Count(mats, MaterialIds.Core);
            });
            Assert.That(surfaceBefore, Is.GreaterThan(0));

            for (int i = 0; i < 10; i++)
            {
                ForceKinematics(host, 0f, 2f, 0f, x);
                yield return Step(host, 1);
            }

            yield return ReadMaterials(host, mats =>
            {
                int surfaceAfter = LidSurfaceY(mats, x, width, height);
                Assert.That(surfaceAfter, Is.GreaterThan(surfaceBefore));
                Assert.That(Count(mats, MaterialIds.Core), Is.EqualTo(coreBefore));
            });
            RestoreDefaults(host);
            RestoreTransport(host, transport);
        }

        [UnityTest]
        public IEnumerator ForcedSubsidenceLowersSolidEdge()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 8802;
            bool transport = host.Config.enableMaterialTransport;
            ConfigureKinematics(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int y0 = Mathf.Clamp(Mathf.RoundToInt(height * 0.58f), 12, height - 12);
            int thickness = PaintedLidThickness(host, y0);
            PaintLidColumn(host, x, y0);
            yield return Step(host, 1);

            int surfaceBefore = 0;
            yield return ReadMaterials(host, mats =>
            {
                Assert.That(mats[(y0 - 1) * width + x], Is.EqualTo(MaterialIds.Mantle));
                Assert.That(mats[(y0 + thickness - 1) * width + x], Is.EqualTo(MaterialIds.Rock));
                Assert.That(mats[(y0 + thickness) * width + x], Is.EqualTo(MaterialIds.Air));
                surfaceBefore = LidSurfaceY(mats, x, width, height);
            });
            Assert.That(surfaceBefore, Is.EqualTo(y0 + thickness - 1));

            for (int i = 0; i < 8; i++)
            {
                ForceKinematics(host, 0f, -2f, 0f, x);
                yield return Step(host, 1);
            }

            yield return ReadMaterials(host, mats =>
            {
                int surfaceAfter = LidSurfaceY(mats, x, width, height);
                Assert.That(mats[surfaceBefore * width + x], Is.Not.EqualTo(MaterialIds.Rock),
                    "Subsidence should vacate the old surface cell");
                Assert.That(surfaceAfter, Is.LessThan(surfaceBefore));
            });
            RestoreDefaults(host);
            RestoreTransport(host, transport);
        }

        [UnityTest]
        public IEnumerator ForcedAngularFlowShiftsLidMarkerAndWrapsSeam()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return AssertAngularMarkerShift(host, 8803, host.Grid.angularResolution - 2, 2f);
        }

        [UnityTest]
        public IEnumerator ForcedNegativeAngularFlowShiftsOddMarker()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return AssertAngularMarkerShift(host, 8807, 51, -2f);
        }

        private static IEnumerator AssertAngularMarkerShift(SimulationHost host, int seed, int markerX, float flowX)
        {
            host.Clock.SetRunning(false);
            host.Config.seed = seed;
            bool transport = host.Config.enableMaterialTransport;
            ConfigureKinematics(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int y0 = Mathf.Clamp(Mathf.RoundToInt(height * 0.58f), 12, height - 12);
            int thickness = PaintedLidThickness(host, y0);
            for (int dx = -4; dx <= 4; dx++)
            {
                int px = (markerX + dx + width) % width;
                Paint(host, px, y0 - 1, MaterialIds.Mantle);
                for (int y = y0; y < y0 + thickness; y++)
                    Paint(host, px, y, MaterialIds.Rock);
                for (int y = y0 + thickness; y < height; y++)
                    Paint(host, px, y, MaterialIds.Air);
            }
            Paint(host, markerX, y0 + thickness - 1, MaterialIds.Metal);
            yield return Step(host, 1);

            int markerBefore = -1;
            int yLo = Mathf.Max(0, y0);
            int yHi = Mathf.Min(height - 1, y0 + thickness + 24);
            yield return ReadMaterials(host, mats => markerBefore = FindMaterialXNear(mats, MaterialIds.Metal, markerX, width, yLo, yHi));
            Assert.That(markerBefore, Is.EqualTo(markerX));

            for (int i = 0; i < 12; i++)
            {
                ForceKinematics(host, flowX, 0f);
                yield return Step(host, 1);
            }

            yield return ReadMaterials(host, mats =>
            {
                int markerAfter = FindMaterialXNear(mats, MaterialIds.Metal, markerX, width, yLo, yHi);
                Assert.That(markerAfter, Is.GreaterThanOrEqualTo(0));
                int signed = flowX > 0f
                    ? (markerAfter - markerBefore + width) % width
                    : (markerBefore - markerAfter + width) % width;
                Assert.That(signed, Is.GreaterThan(0));
                Assert.That(signed, Is.LessThan(width / 2));
            });
            RestoreDefaults(host);
            RestoreTransport(host, transport);
        }

        [UnityTest]
        public IEnumerator KinematicUpliftDoesNotStealWaterOrMagma()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 8804;
            bool transport = host.Config.enableMaterialTransport;
            ConfigureKinematics(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int waterX = width / 3;
            int magmaX = (2 * width) / 3;
            int y0 = Mathf.Clamp(Mathf.RoundToInt(height * 0.58f), 12, height - 12);
            int thickness = PaintedLidThickness(host, y0);
            PaintLidColumn(host, waterX, y0);
            PaintLidColumn(host, magmaX, y0);
            yield return Step(host, 1);
            Paint(host, waterX - 1, y0 + thickness, MaterialIds.Rock);
            Paint(host, waterX + 1, y0 + thickness, MaterialIds.Rock);
            Paint(host, waterX, y0 + thickness, MaterialIds.Water);
            PaintField(host, waterX, y0 + thickness, 2f, 1f);
            Paint(host, magmaX, y0 + 2, MaterialIds.Magma);
            PaintField(host, magmaX, y0 + 2, 1f, 1200f);
            yield return Step(host, 1);

            int waterCountBefore = 0;
            int magmaSurfaceBefore = 0;
            yield return ReadMaterials(host, mats =>
            {
                waterCountBefore = CountInColumn(mats, MaterialIds.Water, waterX, width, height);
                magmaSurfaceBefore = LidSurfaceY(mats, magmaX, width, height);
                Assert.That(mats[(y0 + thickness) * width + waterX], Is.EqualTo(MaterialIds.Water));
                Assert.That(mats[(y0 + 2) * width + magmaX], Is.EqualTo(MaterialIds.Magma));
                Assert.That(waterCountBefore, Is.GreaterThan(0));
            });

            for (int i = 0; i < 8; i++)
            {
                ForceKinematics(host, 0f, 2f, 0f, waterX);
                yield return Step(host, 1);
            }

            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int lid = LidSurfaceY(mats, waterX, width, height);
                Assert.That(CountInColumn(mats, MaterialIds.Water, waterX, width, height), Is.EqualTo(waterCountBefore));
                Assert.That(lid, Is.GreaterThanOrEqualTo(0));
                Assert.That(mats[(lid + 1) * width + waterX], Is.EqualTo(MaterialIds.Water));
                Assert.That(mats[(y0 + 2) * width + magmaX], Is.EqualTo(MaterialIds.Magma));
                Assert.That(LidSurfaceY(mats, magmaX, width, height), Is.EqualTo(magmaSurfaceBefore));
                Assert.That(aux[lid * width + waterX].w, Is.LessThan(0.05f));
            });
            RestoreDefaults(host);
            RestoreTransport(host, transport);
        }

        [UnityTest]
        public IEnumerator PersistentVerticalDriveDoesNotRunAwayToLimits()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 8805;
            bool transport = host.Config.enableMaterialTransport;
            ConfigureKinematics(host);
            host.Config.tectonicKinematicCoupling = 1f;
            host.Config.tectonicUpliftScale = 1f;
            host.Config.tectonicConvergenceScale = 1f;
            host.Config.tectonicIsostasyScale = 4f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int y0 = Mathf.Clamp(Mathf.RoundToInt(height * 0.58f), 12, height - 12);
            int atmosphereY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.atmosphereStartRadius * (height - 1)), 1, height - 1);
            PaintLidColumn(host, x, y0);
            yield return Step(host, 1);

            int surfaceBefore = 0;
            yield return ReadMaterials(host, mats => surfaceBefore = LidSurfaceY(mats, x, width, height));
            Assert.That(surfaceBefore, Is.GreaterThan(0));

            int surfaceMid = 0;
            for (int i = 0; i < 80; i++)
            {
                ForceKinematics(host, 0f, 2f, 0f, x, 2);
                yield return Step(host, 1);
                if (i == 59)
                    yield return ReadMaterials(host, mats => surfaceMid = LidSurfaceY(mats, x, width, height));
            }

            yield return ReadMaterials(host, mats =>
            {
                int surfaceAfter = LidSurfaceY(mats, x, width, height);
                int refCells = Mathf.Max(6, Mathf.RoundToInt(host.Config.crustRatio * (height - 1)));
                float isostasy = Mathf.Max(0.01f, host.Config.tectonicIsostasyScale);
                Assert.That(surfaceAfter, Is.LessThan(atmosphereY - 2));
                Assert.That(surfaceAfter, Is.EqualTo(surfaceMid).Within(3));
                Assert.That(surfaceAfter - surfaceBefore, Is.LessThan(Mathf.RoundToInt(refCells * 2f / isostasy) + 6));
            });
            RestoreDefaults(host);
            RestoreTransport(host, transport);
        }

        [UnityTest]
        public IEnumerator DefaultCouplingPlumeMovesLidWithoutRunaway()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 8806;
            bool transport = host.Config.enableMaterialTransport;
            RestoreDefaults(host);
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.eruptionDriveScale = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.enableMaterialTransport = false;
            host.Config.volcanicCoolingRate = 0f;
            host.Config.slowPassInterval = 1000;
            host.Config.coreHeatRate = 0f;
            host.Config.thermalRate = 0f;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int y0 = Mathf.Clamp(Mathf.RoundToInt(height * 0.58f), 12, height - 12);
            PaintLidColumn(host, x, y0);
            for (int dx = -2; dx <= 2; dx++)
            {
                int px = (x + dx + width) % width;
                if (px != x)
                    PaintLidColumn(host, px, y0);
                for (int dy = -6; dy <= -2; dy++)
                {
                    Paint(host, px, y0 + dy, MaterialIds.Magma);
                    PaintField(host, px, y0 + dy, 1f, 1200f);
                }
            }
            yield return Step(host, 1);

            var surfaceBefore = new int[width];
            yield return ReadMaterials(host, mats =>
            {
                for (int px = 0; px < width; px++)
                    surfaceBefore[px] = LidSurfaceY(mats, px, width, height);
            });
            Assert.That(surfaceBefore[x], Is.GreaterThan(0));

            yield return Step(host, 400);

            int refCells = Mathf.Max(6, Mathf.RoundToInt(host.Config.crustRatio * (height - 1)));
            float isostasy = Mathf.Max(0.01f, host.Config.tectonicIsostasyScale);
            int maxRelief = Mathf.RoundToInt(refCells * 2f / isostasy) + 2;
            yield return ReadMaterials(host, mats =>
            {
                bool sectorMoved = false;
                int maxDelta = 0;
                for (int px = 0; px < width; px++)
                {
                    int after = LidSurfaceY(mats, px, width, height);
                    int delta = Mathf.Abs(after - surfaceBefore[px]);
                    maxDelta = Mathf.Max(maxDelta, delta);
                    int dist = Mathf.Min(Mathf.Abs(px - x), width - Mathf.Abs(px - x));
                    if (dist <= 8 && after != surfaceBefore[px])
                        sectorMoved = true;
                }
                Assert.That(sectorMoved, Is.True, "Default coupling should shift the lid over a painted plume sector.");
                Assert.That(maxDelta, Is.LessThanOrEqualTo(maxRelief));
            });
            RestoreDefaults(host);
            RestoreTransport(host, transport);
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

        private static void ForceVolcanicRelease(SimulationHost host, int angularBin, int radialBin, float intensity = 0.9f, float budget = 0.2f)
        {
            var events = new Vector4[GeodynamicsGrid.EventBufferCount()];
            host.Resources.GeodynamicsEvents.GetData(events);
            int index = GeodynamicsGrid.EventIndex(
                angularBin, radialBin, host.Config.geodynamicsAngularBins, host.Config.geodynamicsRadialBins);
            events[index] = new Vector4(GeodynamicsGrid.EventTypeVolcanic, intensity, budget, budget);
            host.Resources.GeodynamicsEvents.SetData(events);
        }

        [UnityTest]
        public IEnumerator VolcanicReleaseBudgetExhaustsAndSetsRefractory()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 9101;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.volcanicReleaseThreshold = 2f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            host.Config.tectonicCooldownTicks = 80;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int angular = host.Config.geodynamicsAngularBins / 2;
            int radial = Mathf.Max(2, host.Config.geodynamicsRadialBins / 3);
            ForceVolcanicRelease(host, angular, radial, 0.95f, 0.18f);
            yield return Step(host, 24);

            Vector4[] state = null;
            Vector4[] events = null;
            yield return ReadGeodynamics(host, (s, e) => { state = s; events = e; });
            int eventIndex = GeodynamicsGrid.EventIndex(angular, radial, host.Config.geodynamicsAngularBins, host.Config.geodynamicsRadialBins);
            int kinIndex = GeodynamicsGrid.StateIndex(angular, radial, GeodynamicsGrid.SlotKinematics,
                host.Config.geodynamicsAngularBins, host.Config.geodynamicsRadialBins);
            Assert.That(events[eventIndex].y, Is.LessThan(0.2f));
            Assert.That(events[eventIndex].w, Is.LessThan(0.05f));
            Assert.That(state[kinIndex].w, Is.GreaterThan(0f));
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator ConcurrentEventsStayWithinCapacity()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 9102;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.geodynamicsPressureBuildRate = 4f;
            host.Config.geodynamicsHeatCoupling = 3f;
            host.Config.tectonicStrainGain = 0f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.volcanicReleaseThreshold = 0.08f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            host.Config.tectonicMaxConcurrentEvents = 2;
            host.Config.tectonicCooldownTicks = 16;
            QuietSurface(host);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 48);

            Vector4[] events = null;
            yield return ReadGeodynamics(host, (_, ev) => events = ev);
            int angularBins = host.Config.geodynamicsAngularBins;
            int radialBins = host.Config.geodynamicsRadialBins;
            int volcanic = 0;
            var angles = new List<int>();
            for (int a = 0; a < angularBins; a++)
            {
                for (int r = 0; r < radialBins; r++)
                {
                    Vector4 ev = events[GeodynamicsGrid.EventIndex(a, r, angularBins, radialBins)];
                    if (ev.y <= 0.05f) continue;
                    if (Mathf.RoundToInt(ev.x) != GeodynamicsGrid.EventTypeVolcanic) continue;
                    volcanic++;
                    angles.Add(a);
                }
            }
            Assert.That(volcanic, Is.LessThanOrEqualTo(host.Config.tectonicMaxConcurrentEvents));
            if (angles.Count >= 2)
            {
                int minSep = Mathf.Max(1, Mathf.CeilToInt(host.Config.tectonicEventFootprint * angularBins));
                int da = Mathf.Min(Mathf.Abs(angles[0] - angles[1]), angularBins - Mathf.Abs(angles[0] - angles[1]));
                Assert.That(da, Is.GreaterThanOrEqualTo(minSep));
            }
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator RateLimitedMeltDoesNotFillALatticeTile()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 9103;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 1;
            host.Config.slowPassInterval = 1;
            host.Config.volcanicMeltRate = 4f;
            host.Config.extrusionRate = 2f;
            host.Config.volcanicReleaseThreshold = 0.08f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            host.Config.geodynamicsPressureBuildRate = 4f;
            host.Config.thermalRate = 0f;
            host.Config.coreHeatRate = 0f;
            host.Config.enableRockChunks = false;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 8);

            uint[] materials = null;
            yield return ReadMaterials(host, mats => materials = mats);
            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int angularBins = host.Config.geodynamicsAngularBins;
            int radialBins = host.Config.geodynamicsRadialBins;
            float maxFill = 0f;
            for (int a = 0; a < angularBins; a++)
            {
                GeodynamicsGrid.ThetaRange(a, width, angularBins, out int x0, out int x1);
                for (int r = 0; r < radialBins; r++)
                {
                    GeodynamicsGrid.RadialRange(r, height, host.Grid.atmosphereStartRadius, radialBins, out int y0, out int y1);
                    int cells = 0;
                    int magma = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        for (int x = x0; x < x1; x++)
                        {
                            cells++;
                            if (materials[y * width + x] == MaterialIds.Magma)
                                magma++;
                        }
                    }
                    if (cells > 0)
                        maxFill = Mathf.Max(maxFill, magma / (float)cells);
                }
            }
            Assert.That(maxFill, Is.LessThan(0.55f), "A single lattice tile must not convert wholesale in a few slow passes.");
            RestoreDefaults(host);
        }

        [UnityTest]
        public IEnumerator ForcedReleaseMeltsAConnectedFilamentNotARectangle()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.ApplyPreset(SimulationPreset.Validation);
            RestoreDefaults(host);
            host.Config.seed = 9104;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.geodynamicsPeriodTicks = 10000;
            host.Config.slowPassInterval = 1;
            host.Config.volcanicMeltRate = 4f;
            host.Config.extrusionRate = 2f;
            host.Config.volcanicReleaseThreshold = 2f;
            host.Config.tectonicEarthquakeThreshold = 2f;
            host.Config.hydrothermalReleaseThreshold = 2f;
            host.Config.thermalRate = 0f;
            host.Config.coreHeatRate = 0f;
            host.Config.eruptionDriveScale = 0f;
            host.Config.thermalRate = 0f;
            host.Config.enableRockChunks = false;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(height * 0.32f), 6, height - 8);
            int angular = GeodynamicsGrid.AngularBinOf(x, width, host.Config.geodynamicsAngularBins);
            int radial = GeodynamicsGrid.RadialBinOf((float)y / Mathf.Max(1, height - 1),
                host.Grid.atmosphereStartRadius, host.Config.geodynamicsRadialBins);
            GeodynamicsGrid.ThetaRange(angular, width, host.Config.geodynamicsAngularBins, out int x0, out int x1);
            GeodynamicsGrid.RadialRange(radial, height, host.Grid.atmosphereStartRadius, host.Config.geodynamicsRadialBins, out int y0, out int y1);

            var geoState = new Vector4[GeodynamicsGrid.StateBufferCount()];
            host.Resources.GeodynamicsStateRead.GetData(geoState);
            int res = GeodynamicsGrid.StateIndex(angular, radial, GeodynamicsGrid.SlotReservoir,
                host.Config.geodynamicsAngularBins, host.Config.geodynamicsRadialBins);
            int kin = GeodynamicsGrid.StateIndex(angular, radial, GeodynamicsGrid.SlotKinematics,
                host.Config.geodynamicsAngularBins, host.Config.geodynamicsRadialBins);
            Vector4 reservoir = geoState[res];
            reservoir.y = 0.9f;
            geoState[res] = reservoir;
            Vector4 kinematics = geoState[kin];
            kinematics.z = 0.85f;
            geoState[kin] = kinematics;
            host.Resources.GeodynamicsStateRead.SetData(geoState);
            host.Resources.GeodynamicsStateWrite.SetData(geoState);
            ForceVolcanicRelease(host, angular, radial, 1f, 0.2f);

            for (int yy = y0; yy < y1; yy++)
            {
                for (int xx = x0; xx < x1; xx++)
                {
                    Paint(host, xx, yy, MaterialIds.Mantle);
                    PaintField(host, xx, yy, 1f, 400f);
                }
            }
            yield return Step(host, 40);

            uint[] materials = null;
            yield return ReadMaterials(host, mats => materials = mats);
            int magma = 0;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            var magmaCells = new List<Vector2Int>();
            for (int yy = y0; yy < y1; yy++)
            {
                for (int xx = x0; xx < x1; xx++)
                {
                    if (materials[yy * width + xx] != MaterialIds.Magma) continue;
                    magma++;
                    minX = Mathf.Min(minX, xx);
                    maxX = Mathf.Max(maxX, xx);
                    magmaCells.Add(new Vector2Int(xx, yy));
                }
            }
            int binWidth = Mathf.Max(1, x1 - x0);
            int span = magma > 0 ? maxX - minX + 1 : 0;
            Assert.That(magma, Is.GreaterThan(0), "A funded release should melt a dike filament.");
            Assert.That(span, Is.LessThan(binWidth), "Melt must not paint the whole lattice tile width.");
            Assert.That(magma / (float)Mathf.Max(1, binWidth * Mathf.Max(1, y1 - y0)), Is.LessThan(0.55f));

            int largest = LargestFourConnected(magmaCells, width);
            Assert.That(largest, Is.GreaterThanOrEqualTo(Mathf.Max(1, Mathf.CeilToInt(magma * 0.6f))),
                "Dike melt should stay connected rather than forming random pockets.");
            RestoreDefaults(host);
            host.ApplyPreset(SimulationPreset.Standard);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExtremeVolcanismStaysAtOrBelowSafetyLimit()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.ApplyPreset(SimulationPreset.Validation);
            RestoreDefaults(host);
            host.Config.seed = 9105;
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
            RestoreDefaults(host);
            host.ApplyPreset(SimulationPreset.Standard);
            yield return null;
        }

        [UnityTest]
        [Category("Validation")]
        public IEnumerator DefaultSoakKeepsMagmaSparseAndFinite()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.ApplyPreset(SimulationPreset.Validation);
            RestoreDefaults(host);
            host.Config.seed = 12345;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 400);

            WorldGeodynamicsMetrics geo = default;
            WorldGeologyMetrics geology = default;
            yield return MeasureGeodynamics(host, result => geo = result);
            yield return MeasureGeology(host, result => geology = result);
            Assert.That(geo.HasNonFinite, Is.False);
            Assert.That(geology.HasNonFinite, Is.False);
            Assert.That(geo.SafetyHardCap, Is.False);
            Assert.That(geology.MagmaFraction, Is.LessThan(host.Config.volcanicMagmaFractionLimit));
            Assert.That(geo.ActiveVolcanicCount, Is.LessThanOrEqualTo(host.Config.tectonicMaxConcurrentEvents));
            RestoreDefaults(host);
            host.ApplyPreset(SimulationPreset.Standard);
            yield return null;
        }

        private static int LargestFourConnected(List<Vector2Int> cells, int width)
        {
            if (cells == null || cells.Count == 0) return 0;
            var remaining = new HashSet<long>();
            for (int i = 0; i < cells.Count; i++)
                remaining.Add(((long)cells[i].y << 32) ^ (uint)cells[i].x);
            int best = 0;
            var queue = new Queue<Vector2Int>();
            while (remaining.Count > 0)
            {
                long seed = 0;
                foreach (long key in remaining)
                {
                    seed = key;
                    break;
                }
                remaining.Remove(seed);
                queue.Enqueue(new Vector2Int((int)(seed & 0xffffffff), (int)(seed >> 32)));
                int size = 0;
                while (queue.Count > 0)
                {
                    Vector2Int cell = queue.Dequeue();
                    size++;
                    Vector2Int[] n = { new Vector2Int(cell.x - 1, cell.y), new Vector2Int(cell.x + 1, cell.y), new Vector2Int(cell.x, cell.y - 1), new Vector2Int(cell.x, cell.y + 1) };
                    for (int i = 0; i < n.Length; i++)
                    {
                        int nx = ((n[i].x % width) + width) % width;
                        long key = ((long)n[i].y << 32) ^ (uint)nx;
                        if (!remaining.Remove(key)) continue;
                        queue.Enqueue(new Vector2Int(nx, n[i].y));
                    }
                }
                best = Mathf.Max(best, size);
            }
            return best;
        }
    }
}
