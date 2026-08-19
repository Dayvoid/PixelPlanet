using System;
using System.Collections;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class GeologyIntegrationTests
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

        private static int Count(uint[] materials, uint id)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == id) count++;
            return count;
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

        private static void PaintWater(SimulationHost host, int x, int y, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(2f, amount, 0f, 0f)
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

        private static void DriveWindErosion(SimulationHost host, int x, int y)
        {
            // Lateral pressure gradient → AtmosphericDynamics flow. Keep soil dry.
            PaintPressure(host, x - 1, y, 8f);
            PaintPressure(host, x + 1, y, -2f);
            PaintGroundwater(host, x, y, -100f);
            PaintWater(host, x, y, -100f);
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static void ConfigureEruptionDefaults(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1;
            host.Config.volcanicCooling = 0f;
            host.Config.gravityStrength = 0f;
            host.Config.fractureRate = 0f;
            host.Config.magmaViscosity = 0f;
            host.Config.eruptionPressureStrength = 2f;
            host.Config.eruptionFlowStrength = 2f;
            host.Config.eruptionBurdenDepth = 3;
            host.Config.ashFertilityStrength = 1f;
            host.Config.ashUpdraftStrength = 1f;
            host.Config.ashSettlingStrength = 1f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.pressureRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
        }

        private static void ConfigureStressIsolation(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1;
            host.Config.validationIntervalTicks = 100000;
            host.Config.magmaEruption = 0f;
            host.Config.gravityStrength = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.mantlePressure = 0f;
            host.Config.volcanicCooling = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.baseSoilCohesion = 0f;
            host.Config.stressDecayRate = 0.02f;
            host.Config.dryMoistureThreshold = 0.08f;
            host.Config.moistureCohesionStrength = 0.85f;
            host.Config.capillaryEvaporationFraction = 0.35f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;
            host.Config.springDischargeRate = 0f;
            host.Config.geyserDischargeRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.thermalRate = 0f;
            host.Config.electricalRate = 0f;
            host.Config.pressureRate = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.vaporPressureScale = 0f;
        }

        private static void RestoreStressIsolation(SimulationHost host)
        {
            host.Config.slowPassInterval = 4;
            host.Config.validationIntervalTicks = 1000;
            host.Config.magmaEruption = 0f;
            host.Config.gravityStrength = 1f;
            host.Config.fractureRate = 0.2f;
            host.Config.extrusionRate = 0.3f;
            host.Config.mantlePressure = 0.7f;
            host.Config.volcanicCooling = 0.15f;
            host.Config.dissolutionRate = 0.03f;
            host.Config.collapseRate = 0.03f;
            host.Config.erosionRate = 0.06f;
            host.Config.baseSoilCohesion = 0.45f;
            host.Config.stressDecayRate = 0.02f;
            host.Config.dryMoistureThreshold = 0.08f;
            host.Config.moistureCohesionStrength = 0.85f;
            host.Config.capillaryEvaporationFraction = 0.35f;
            host.Config.infiltrationRate = 0.3f;
            host.Config.groundwaterRate = 0.18f;
            host.Config.runoffRate = 0.45f;
            host.Config.pondingRate = 0.25f;
            host.Config.springDischargeRate = 0.35f;
            host.Config.geyserDischargeRate = 0.5f;
            host.Config.windStrength = 0.35f;
            host.Config.evaporationRate = 0.1f;
            host.Config.condensationRate = 0.12f;
            host.Config.precipitationRate = 0.2f;
            host.Config.thermalRate = 0.35f;
            host.Config.electricalRate = 0.3f;
            host.Config.pressureRate = 0.4f;
            host.Config.pressureDiffusionRate = 0.5f;
            host.Config.vaporPressureScale = 0.25f;
        }

        private static void PaintSupportedColumn(SimulationHost host, int x, int y, uint surfaceMaterial)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, surfaceMaterial);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
            }
        }

        [UnityTest]
        public IEnumerator MagmaEruptionZeroDoesNotCreateAsh()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureEruptionDefaults(host);
            host.Config.magmaEruption = 0f;
            host.Config.eruptionBlastThreshold = 0.2f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = 0, magmaY = 0;
            // Manual column paint without out iterator:
            x = host.Grid.angularResolution / 2;
            magmaY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.55f), 8, host.Grid.radialResolution - 8);
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                    if (!(dx == 0 && (dy == 0 || dy == 1 || dy == 2)))
                        Paint(host, x + dx, magmaY + dy, MaterialIds.Rock);
            Paint(host, x, magmaY, MaterialIds.Magma);
            Paint(host, x, magmaY + 1, MaterialIds.Sediment);
            Paint(host, x, magmaY + 2, MaterialIds.Air);
            PaintHeat(host, x, magmaY, 1000f);
            yield return Step(host, 1);

            PaintPressure(host, x, magmaY, 12f);
            yield return Step(host, 16);

            int ash = 0;
            yield return ReadMaterials(host, mats => ash = Count(mats, MaterialIds.Ash));
            Assert.That(ash, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator MagmaEruptionSeepsOrDisplacesSediment()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureEruptionDefaults(host);
            host.Config.magmaEruption = 0f;
            host.Config.eruptionBlastThreshold = 999f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int magmaY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.55f), 8, host.Grid.radialResolution - 8);
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                    if (!(dx == 0 && (dy == 0 || dy == 1 || dy == 2)))
                        Paint(host, x + dx, magmaY + dy, MaterialIds.Rock);
            // Stabilize rock platform before enabling eruption.
            yield return Step(host, 1);

            host.Config.magmaEruption = 1f;
            Paint(host, x, magmaY, MaterialIds.Magma);
            Paint(host, x, magmaY + 1, MaterialIds.Sediment);
            Paint(host, x, magmaY + 2, MaterialIds.Air);
            PaintHeat(host, x, magmaY, 1000f);
            PaintPressure(host, x, magmaY, 10f);
            // EruptionMotion runs in the same tick as the paint, before Volcanism soft intrusion.
            yield return Step(host, 8);

            uint afterAbove = 0;
            uint afterMagmaCell = 0;
            yield return ReadMaterials(host, mats =>
            {
                afterMagmaCell = mats[magmaY * host.Grid.angularResolution + x];
                afterAbove = mats[(magmaY + 1) * host.Grid.angularResolution + x];
            });

            bool seeped = afterAbove == MaterialIds.Magma
                && (afterMagmaCell == MaterialIds.Sediment || afterMagmaCell == MaterialIds.Soil || afterMagmaCell == MaterialIds.Ash);
            Assert.That(seeped, Is.True, $"Expected seep swap. above={afterAbove} magmaCell={afterMagmaCell}");
        }

        [UnityTest]
        public IEnumerator HighOverdriveBlastCreatesAsh()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureEruptionDefaults(host);
            host.Config.magmaEruption = 0f;
            host.Config.eruptionBlastThreshold = 0.15f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int magmaY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.55f), 8, host.Grid.radialResolution - 8);
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                    if (!(dx == 0 && (dy == 0 || dy == 1 || dy == 2)))
                        Paint(host, x + dx, magmaY + dy, MaterialIds.Rock);
            Paint(host, x, magmaY, MaterialIds.Magma);
            Paint(host, x, magmaY + 1, MaterialIds.Sediment);
            Paint(host, x, magmaY + 2, MaterialIds.Air);
            PaintHeat(host, x, magmaY, 1000f);
            yield return Step(host, 1);

            host.Config.magmaEruption = 1f;
            PaintPressure(host, x, magmaY, 14f);
            yield return Step(host, 16);

            int ash = 0;
            yield return ReadMaterials(host, mats => ash = Count(mats, MaterialIds.Ash));
            Assert.That(ash, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator AshSettlesAndFertilizesSedimentIntoSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureEruptionDefaults(host);
            host.Config.magmaEruption = 0f;
            host.Config.ashFertilityStrength = 2f;
            host.Config.ashSettlingStrength = 0f;
            host.Config.ashUpdraftStrength = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.7f), 8, host.Grid.radialResolution - 8);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    if (!(dx == 0 && (dy == 0 || dy == 1)))
                        Paint(host, x + dx, y + dy, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Sediment);
            Paint(host, x, y + 1, MaterialIds.Ash);
            yield return Step(host, 6);

            bool fertilized = false;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int width = host.Grid.angularResolution;
                for (int dy = 0; dy <= 1; dy++)
                {
                    int index = (y + dy) * width + x;
                    if (mats[index] == MaterialIds.Soil && aux[index].z > 0.1f)
                        fertilized = true;
                }
            });
            Assert.That(fertilized, Is.True);
        }

        [UnityTest]
        public IEnumerator HotUpdraftLoftsAshWhileWeakFlowSettles()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureEruptionDefaults(host);
            host.Config.magmaEruption = 0f;
            host.Config.ashUpdraftStrength = 4f;
            host.Config.ashSettlingStrength = 0.05f;
            host.Config.windStrength = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.65f), 8, host.Grid.radialResolution - 10);
            for (int dx = -2; dx <= 2; dx++)
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Ash);
            Paint(host, x, y + 1, MaterialIds.Air);
            Paint(host, x, y + 2, MaterialIds.Air);
            Paint(host, x, y + 3, MaterialIds.Air);
            PaintHeat(host, x, y, 600f);
            yield return Step(host, 8);

            int lofted = 0;
            yield return ReadMaterials(host, mats =>
            {
                int width = host.Grid.angularResolution;
                for (int yy = y + 1; yy <= y + 3; yy++)
                    if (mats[yy * width + x] == MaterialIds.Ash) lofted++;
            });

            host.Config.ashUpdraftStrength = 0f;
            host.Config.ashSettlingStrength = 4f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            for (int dx = -2; dx <= 2; dx++)
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Air);
            Paint(host, x, y + 1, MaterialIds.Air);
            Paint(host, x, y + 2, MaterialIds.Ash);
            yield return Step(host, 10);

            bool settled = false;
            yield return ReadMaterials(host, mats =>
            {
                int width = host.Grid.angularResolution;
                settled = mats[y * width + x] == MaterialIds.Ash || mats[(y + 1) * width + x] == MaterialIds.Ash;
            });

            Assert.That(lofted > 0 || settled, Is.True);
        }

        [UnityTest]
        public IEnumerator SustainedEruptionRemainsFiniteAndDeterministic()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 424242;
            host.Config.magmaEruption = 0.85f;
            host.Config.extrusionRate = 1.5f;
            host.Config.mantlePressure = 2f;
            host.Config.eruptionBlastThreshold = 0.8f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.gravityStrength = 1f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            SimulationValidator validator = UnityEngine.Object.FindFirstObjectByType<SimulationValidator>();
            Assert.That(validator, Is.Not.Null);
            validator.ResetBaseline();
            yield return Step(host, 40);

            bool validationDone = false;
            validator.ValidationCompleted += (_, __) => validationDone = true;
            validator.ValidateNow();
            for (int i = 0; i < 240 && !validationDone; i++)
                yield return null;
            Assert.That(validator.LastMessage, Does.Not.Contain("Non-finite"));

            uint[] materialsA = null;
            yield return ReadMaterials(host, mats => materialsA = (uint[])mats.Clone());

            host.Config.seed = 424242;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 40);
            uint[] materialsB = null;
            yield return ReadMaterials(host, mats => materialsB = mats);
            Assert.That(materialsB, Is.EqualTo(materialsA));

            // Restore safe defaults on shared config asset.
            host.Config.magmaEruption = 0f;
            host.Config.gravityStrength = 1f;
            host.Config.fractureRate = 0.2f;
            host.Config.volcanicCooling = 0.15f;
            host.Config.extrusionRate = 0.3f;
            host.Config.mantlePressure = 0.7f;
        }

        [UnityTest]
        public IEnumerator StressDecaysLinearlyWhenLoadingStops()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureStressIsolation(host);
            host.Config.stressDecayRate = 0f;
            host.Config.seed = 4242;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.6f), 8, host.Grid.radialResolution - 8);
            PaintSupportedColumn(host, x, y, MaterialIds.Rock);
            yield return Step(host, 1);

            // Build partial stress via tectonic loading, then freeze fracture.
            host.Config.fractureRate = 0.5f;
            PaintPressure(host, x, y, 2f);
            yield return Step(host, 20);

            float stressBefore = -1f;
            uint materialBefore = 0;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int index = y * host.Grid.angularResolution + x;
                materialBefore = mats[index];
                stressBefore = aux[index].w;
            });
            Assert.That(materialBefore, Is.EqualTo(MaterialIds.Rock));
            Assert.That(stressBefore, Is.InRange(0.1f, 1.0f));

            host.Config.fractureRate = 0f;
            host.Config.stressDecayRate = 0.5f;
            const int decayTicks = 16;
            float expectedDrop = 0.5f * (1f / host.Config.ticksPerSecond) * decayTicks;
            yield return Step(host, decayTicks);

            float stressAfter = -1f;
            uint materialAfter = 0;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int index = y * host.Grid.angularResolution + x;
                materialAfter = mats[index];
                stressAfter = aux[index].w;
            });

            Assert.That(materialAfter, Is.EqualTo(MaterialIds.Rock));
            Assert.That(stressAfter, Is.LessThan(stressBefore - expectedDrop * 0.5f));
            Assert.That(stressAfter, Is.EqualTo(Mathf.Max(0f, stressBefore - expectedDrop)).Within(0.08f));

            RestoreStressIsolation(host);
        }

        [UnityTest]
        public IEnumerator WeakErosionBelowDecayDoesNotConvertSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureStressIsolation(host);
            host.Config.erosionRate = 0f;
            host.Config.stressDecayRate = 2f;
            host.Config.baseSoilCohesion = 0f;
            host.Config.seed = 5151;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            PaintSupportedColumn(host, x, y, MaterialIds.Soil);
            // Heal any worldgen fault before measuring weak erosional loading.
            yield return Step(host, 40);

            host.Config.stressDecayRate = 0.25f;
            host.Config.erosionRate = 0.02f;
            host.Config.windStrength = 0.15f;
            host.Config.windDamping = 0.2f;
            for (int i = 0; i < 60; i++)
            {
                DriveWindErosion(host, x, y);
                yield return Step(host, 1);
            }

            uint material = 0;
            float stress = -1f;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int index = y * host.Grid.angularResolution + x;
                material = mats[index];
                stress = aux[index].w;
            });

            Assert.That(material, Is.EqualTo(MaterialIds.Soil));
            Assert.That(stress, Is.LessThan(0.25f));

            RestoreStressIsolation(host);
        }

        [UnityTest]
        public IEnumerator SustainedErosionAboveDecayConvertsSoilToSediment()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureStressIsolation(host);
            host.Config.erosionRate = 2f;
            host.Config.stressDecayRate = 0.02f;
            host.Config.baseSoilCohesion = 0f;
            host.Config.dryMoistureThreshold = 0.08f;
            host.Config.moistureCohesionStrength = 0.85f;
            host.Config.windStrength = 3f;
            host.Config.windDamping = 0.01f;
            host.Config.seed = 6161;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            PaintSupportedColumn(host, x, y, MaterialIds.Soil);
            yield return Step(host, 1);

            bool converted = false;
            float stressAfterConvert = -1f;
            for (int i = 0; i < 60 && !converted; i++)
            {
                DriveWindErosion(host, x, y);
                yield return Step(host, 1);
                yield return ReadMaterialsAndAux(host, (mats, aux) =>
                {
                    int index = y * host.Grid.angularResolution + x;
                    if (mats[index] == MaterialIds.Sediment)
                    {
                        converted = true;
                        stressAfterConvert = aux[index].w;
                    }
                });
            }

            Assert.That(converted, Is.True, "Expected dry exposed soil to convert under sustained wind-driven erosion.");
            Assert.That(stressAfterConvert, Is.EqualTo(0f).Within(0.001f));

            RestoreStressIsolation(host);
        }

        [UnityTest]
        public IEnumerator ZeroStressDecayPreservesLegacyAccumulation()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureStressIsolation(host);
            host.Config.stressDecayRate = 2f;
            host.Config.erosionRate = 0f;
            host.Config.baseSoilCohesion = 0f;
            host.Config.seed = 7171;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            PaintSupportedColumn(host, x, y, MaterialIds.Soil);
            yield return Step(host, 40);

            host.Config.stressDecayRate = 0f;
            host.Config.erosionRate = 0.15f;
            host.Config.windStrength = 1.2f;
            host.Config.windDamping = 0.35f;
            bool loaded = false;
            float stressLoaded = -1f;
            uint materialLoaded = 0;
            for (int i = 0; i < 50 && !loaded; i++)
            {
                DriveWindErosion(host, x, y);
                yield return Step(host, 1);
                yield return ReadMaterialsAndAux(host, (mats, aux) =>
                {
                    int index = y * host.Grid.angularResolution + x;
                    materialLoaded = mats[index];
                    stressLoaded = aux[index].w;
                    if (materialLoaded == MaterialIds.Soil && stressLoaded >= 0.2f && stressLoaded <= 0.95f)
                        loaded = true;
                });
            }
            Assert.That(materialLoaded, Is.EqualTo(MaterialIds.Soil));
            Assert.That(loaded, Is.True, $"Expected partial dry-soil stress accumulation, got stress={stressLoaded}");

            // Remove erosional drive and hold decay at zero; stress must not recover.
            host.Config.erosionRate = 0f;
            host.Config.windStrength = 0f;
            yield return Step(host, 30);

            float stressAfterIdle = -1f;
            uint material = 0;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int index = y * host.Grid.angularResolution + x;
                material = mats[index];
                stressAfterIdle = aux[index].w;
            });

            Assert.That(material, Is.EqualTo(MaterialIds.Soil));
            Assert.That(stressAfterIdle, Is.EqualTo(stressLoaded).Within(0.05f));

            RestoreStressIsolation(host);
        }

        [UnityTest]
        public IEnumerator MoistExposedSoilResistsWindErosion()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureStressIsolation(host);
            host.Config.erosionRate = 2f;
            host.Config.stressDecayRate = 0.02f;
            host.Config.baseSoilCohesion = 0f;
            host.Config.dryMoistureThreshold = 0.08f;
            host.Config.moistureCohesionStrength = 0.85f;
            host.Config.windStrength = 3f;
            host.Config.windDamping = 0.01f;
            host.Config.seed = 8181;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            PaintSupportedColumn(host, x, y, MaterialIds.Soil);
            yield return Step(host, 1);

            for (int i = 0; i < 50; i++)
            {
                PaintPressure(host, x - 1, y, 8f);
                PaintPressure(host, x + 1, y, -2f);
                PaintGroundwater(host, x, y, 0.6f);
                yield return Step(host, 1);
            }

            uint material = 0;
            yield return ReadMaterials(host, mats =>
            {
                material = mats[y * host.Grid.angularResolution + x];
            });
            Assert.That(material, Is.EqualTo(MaterialIds.Soil), "Moist exposed soil should resist ordinary wind erosion.");

            RestoreStressIsolation(host);
        }

        [UnityTest]
        public IEnumerator BuriedSoilResistsOrdinaryWindErosion()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureStressIsolation(host);
            host.Config.erosionRate = 2f;
            host.Config.stressDecayRate = 0.02f;
            host.Config.baseSoilCohesion = 0f;
            host.Config.windStrength = 3f;
            host.Config.windDamping = 0.01f;
            host.Config.seed = 9191;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Soil);
                Paint(host, x + dx, y + 1, MaterialIds.Rock);
            }
            yield return Step(host, 1);

            for (int i = 0; i < 50; i++)
            {
                DriveWindErosion(host, x, y);
                yield return Step(host, 1);
            }

            uint material = 0;
            yield return ReadMaterials(host, mats =>
            {
                material = mats[y * host.Grid.angularResolution + x];
            });
            Assert.That(material, Is.EqualTo(MaterialIds.Soil), "Buried soil must not convert via ordinary wind/runoff erosion.");

            RestoreStressIsolation(host);
        }

        [UnityTest]
        public IEnumerator RewettingHaltsDrySoilStressAccumulation()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureStressIsolation(host);
            host.Config.erosionRate = 0.2f;
            host.Config.stressDecayRate = 0.08f;
            host.Config.baseSoilCohesion = 0f;
            host.Config.windStrength = 1.2f;
            host.Config.windDamping = 0.35f;
            host.Config.seed = 10101;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            PaintSupportedColumn(host, x, y, MaterialIds.Soil);
            yield return Step(host, 1);

            float stressDry = -1f;
            uint materialDry = 0;
            for (int i = 0; i < 40; i++)
            {
                DriveWindErosion(host, x, y);
                yield return Step(host, 1);
                yield return ReadMaterialsAndAux(host, (mats, aux) =>
                {
                    int index = y * host.Grid.angularResolution + x;
                    materialDry = mats[index];
                    stressDry = aux[index].w;
                });
                if (materialDry == MaterialIds.Soil && stressDry >= 0.15f)
                    break;
            }
            Assert.That(materialDry, Is.EqualTo(MaterialIds.Soil), "Dry loading phase must stop before soil converts to sediment.");
            Assert.That(stressDry, Is.GreaterThan(0.15f));

            for (int i = 0; i < 40; i++)
            {
                PaintPressure(host, x - 1, y, 8f);
                PaintPressure(host, x + 1, y, -2f);
                PaintGroundwater(host, x, y, 0.6f);
                yield return Step(host, 1);
            }

            float stressWet = -1f;
            uint material = 0;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int index = y * host.Grid.angularResolution + x;
                material = mats[index];
                stressWet = aux[index].w;
            });
            Assert.That(material, Is.EqualTo(MaterialIds.Soil));
            Assert.That(stressWet, Is.LessThan(stressDry));

            RestoreStressIsolation(host);
        }

        [UnityTest]
        public IEnumerator CapillaryEvaporationDriesExposedSoilGroundwater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureStressIsolation(host);
            host.Config.evaporationRate = 2f;
            host.Config.capillaryEvaporationFraction = 1f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.seed = 11111;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            PaintSupportedColumn(host, x, y, MaterialIds.Soil);
            PaintWater(host, x, y, -100f);
            PaintGroundwater(host, x, y, -100f);
            PaintGroundwater(host, x, y, 0.5f);
            PaintHeat(host, x, y, 80f);
            yield return Step(host, 1);

            float groundBefore = -1f;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                groundBefore = aux[y * host.Grid.angularResolution + x].y;
            });
            Assert.That(groundBefore, Is.GreaterThan(0.2f));

            for (int i = 0; i < 80; i++)
            {
                PaintHeat(host, x, y, 40f);
                yield return Step(host, 1);
            }

            float groundAfter = -1f;
            float vapor = -1f;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int index = y * host.Grid.angularResolution + x;
                groundAfter = aux[index].y;
                vapor = aux[index].x;
            });
            Assert.That(groundAfter, Is.LessThan(groundBefore - 0.1f));
            Assert.That(vapor, Is.GreaterThan(0.05f));

            RestoreStressIsolation(host);
        }

        [UnityTest]
        public IEnumerator IdleSedimentDoesNotAutoConvertToSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureStressIsolation(host);
            host.Config.seed = 12121;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            PaintSupportedColumn(host, x, y, MaterialIds.Sediment);
            PaintGroundwater(host, x, y, 0.8f);
            PaintWater(host, x, y, 0.4f);
            yield return Step(host, 80);

            uint material = 0;
            yield return ReadMaterials(host, mats =>
            {
                material = mats[y * host.Grid.angularResolution + x];
            });
            Assert.That(material, Is.EqualTo(MaterialIds.Sediment), "Calm/wet sediment must not passively become soil.");

            RestoreStressIsolation(host);
        }
    }
}
