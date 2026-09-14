using System.IO;
using GeneSys.Rendering;
using GeneSys.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class GraphicsVisualsTests
    {
        [Test]
        public void SolarDirectionMatchesAngleConvention()
        {
            Vector2 zero = TerrariumVisualController.SolarDirectionFromAngle01(0f);
            Assert.That(zero.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(zero.y, Is.EqualTo(0f).Within(0.001f));

            Vector2 quarter = TerrariumVisualController.SolarDirectionFromAngle01(0.25f);
            Assert.That(quarter.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(quarter.y, Is.EqualTo(1f).Within(0.001f));

            Vector2 wrapped = TerrariumVisualController.SolarDirectionFromAngle01(1.25f);
            Assert.That(wrapped.x, Is.EqualTo(quarter.x).Within(0.001f));
            Assert.That(wrapped.y, Is.EqualTo(quarter.y).Within(0.001f));
        }

        [Test]
        public void SolarInsolationIsLinearFromSubsolarToTerminators()
        {
            const float sun = 0.1f;
            Assert.That(TerrariumVisualController.SolarInsolation(sun, sun), Is.EqualTo(1f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun + 0.125f, sun), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun - 0.125f, sun), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun + 0.25f, sun), Is.EqualTo(0f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun - 0.25f, sun), Is.EqualTo(0f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun + 0.5f, sun), Is.EqualTo(0f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun + 1.125f, sun), Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void SharedSolarInsolationHelperIsWiredThroughSimAndDisplay()
        {
            string helper = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SolarInsolation.hlsl");
            Assert.That(helper.Contains("float SolarInsolation(float theta01, float solarAngle01)"));
            Assert.That(helper.Contains("saturate(1.0 - dist * 4.0)"));

            string weather = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Weather.compute");
            Assert.That(weather.Contains("SolarInsolation.hlsl"));
            Assert.That(weather.Contains("SolarInsolation(theta01, _WeatherC.y)"));

            string flora = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Flora.compute");
            Assert.That(flora.Contains("SolarInsolation.hlsl"));
            Assert.That(flora.Contains("SolarInsolation(theta01, _WeatherC.y)"));

            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display.Contains("SolarInsolation.hlsl"));
            Assert.That(display.Contains("SolarInsolation(theta01, _SolarAngle01)"));
            Assert.That(display.Contains("float3(0.62, 0.70, 0.95)"));
        }

        [Test]
        public void MoltenCoreShaderExistsAndHasConvectionProperties()
        {
            string shader = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/MoltenCore.shader");
            Assert.That(shader.Contains("Shader \"GeneSys/Molten Core\""));
            Assert.That(shader.Contains("_CirculationSpeed"));
            Assert.That(shader.Contains("_HeatGlow"));
            Assert.That(shader.Contains("_Intensity"));
            Assert.That(shader.Contains("_CoreColor"));
            Assert.That(shader.Contains("_MagmaColor"));
            Assert.That(shader.Contains("_DeepColor"));
            Assert.That(shader.Contains("_SlagColor"));
        }

        [Test]
        public void MoltenCoreConfigDefaultsAreConfigured()
        {
            var config = ScriptableObject.CreateInstance<GeneSys.Configuration.SimulationConfig>();
            Assert.That(config.enableCoreVisual, Is.EqualTo(1));
            Assert.That(config.coreVisualStrength, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.coreCirculationSpeed, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.coreHeatGlow, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.coreVisualScale, Is.EqualTo(1f).Within(0.001f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void BackdropLayerScaleCoversViewAcrossZoomRange()
        {
            const float maxOrtho = PlanetoidDisplayRenderer.MaxOrthographicSize;
            const float starMargin = 2.75f;
            const float nebulaMargin = 0.55f;
            float starSpawn = maxOrtho * (1f + starMargin);
            float nebulaSpawn = maxOrtho * (1f + nebulaMargin);
            float[] orthos = { PlanetoidDisplayRenderer.MinOrthographicSize, 2.5f, 5.5f, maxOrtho };
            float[] follows =
            {
                TerrariumVisualController.DefaultStarfieldZoomFollow,
                TerrariumVisualController.DefaultNebulaZoomFollow
            };

            foreach (float follow in follows)
            {
                foreach (float ortho in orthos)
                {
                    float starCover = ortho / starSpawn;
                    float starScale = TerrariumVisualController.BackdropLayerScale(ortho, maxOrtho, follow, starCover);
                    Assert.That(
                        TerrariumVisualController.BackdropCoversView(starSpawn, starScale, ortho),
                        Is.True,
                        $"stars follow {follow} ortho {ortho}");

                    float nebulaCover = ortho / nebulaSpawn;
                    float nebulaScale = TerrariumVisualController.BackdropLayerScale(ortho, maxOrtho, follow, nebulaCover);
                    Assert.That(
                        TerrariumVisualController.BackdropCoversView(nebulaSpawn, nebulaScale, ortho),
                        Is.True,
                        $"nebula follow {follow} ortho {ortho}");
                }
            }
        }

        [Test]
        public void DistantBackdropFollowChangesApparentSizeLessThanNear()
        {
            const float maxOrtho = PlanetoidDisplayRenderer.MaxOrthographicSize;
            const float minOrtho = PlanetoidDisplayRenderer.MinOrthographicSize;
            float farMin = ApparentSize(minOrtho, maxOrtho, TerrariumVisualController.DefaultStarfieldZoomFollow);
            float farMax = ApparentSize(maxOrtho, maxOrtho, TerrariumVisualController.DefaultStarfieldZoomFollow);
            float nearMin = ApparentSize(minOrtho, maxOrtho, TerrariumVisualController.DefaultNebulaZoomFollow);
            float nearMax = ApparentSize(maxOrtho, maxOrtho, TerrariumVisualController.DefaultNebulaZoomFollow);
            float farChange = farMin / farMax;
            float nearChange = nearMin / nearMax;
            Assert.That(farChange, Is.LessThan(nearChange));

            float lockedMin = ApparentSize(minOrtho, maxOrtho, 1f);
            float lockedMax = ApparentSize(maxOrtho, maxOrtho, 1f);
            Assert.That(lockedMin, Is.EqualTo(lockedMax).Within(0.001f));
        }

        [Test]
        public void ProbeThrusterDefaultsSitOnTheSpriteRear()
        {
            var config = ScriptableObject.CreateInstance<GeneSys.Configuration.SimulationConfig>();
            Assert.That(config.enableProbeThruster, Is.EqualTo(1));
            Assert.That(config.probeThrusterStrength, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.starfieldZoomFollow, Is.EqualTo(TerrariumVisualController.DefaultStarfieldZoomFollow).Within(0.001f));
            Assert.That(config.nebulaZoomFollow, Is.EqualTo(TerrariumVisualController.DefaultNebulaZoomFollow).Within(0.001f));
            Assert.That(ProbeController.ThrusterLocalOffset.x, Is.LessThan(0f));
            Object.DestroyImmediate(config);
        }

        private static float ApparentSize(float ortho, float reference, float follow)
        {
            return TerrariumVisualController.BackdropLayerScale(ortho, reference, follow, 0f) / ortho;
        }

        [Test]
        public void PlanetoidDisplayAttenuatesDayNightAndGridAtCore()
        {
            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display.Contains("depthFade = saturate((simulationRadius - _VisualCoreRadius) / max(0.01, 1.0 - _VisualCoreRadius));"));
            Assert.That(display.Contains("coreGridFade"));
        }

        [Test]
        public void AtmosphereGlowShaderContainsSpeedPropertyAndFragIntegration()
        {
            string shader = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/AtmosphereGlow.shader");
            Assert.That(shader.Contains("_Speed (\"Speed\", Float) = 1.0"));
            Assert.That(shader.Contains("float _Speed;"));
            Assert.That(shader.Contains("max(0.001, _Speed)"));
        }

        [Test]
        public void UnifiedFloraAndFaunaOverlaysAreWiredThroughShader()
        {
            string shader = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(shader.Contains("Texture2DArray<float4> _FloraTex;"));
            Assert.That(shader.Contains("_OverlayMode == 20"));
            Assert.That(shader.Contains("_OverlayMode == 23"));
            Assert.That(shader.Contains("ALGAE: Vibrant Cyan / Emerald Teal"));
            Assert.That(shader.Contains("GRASS: Spring Green & Magenta Flower blooms"));
            Assert.That(shader.Contains("TREES: Warm Amber Wood & Deep Forest Leaf"));
            Assert.That(shader.Contains("WASP: Electric Crimson & Pollen Gold"));
            Assert.That(shader.Contains("CRICKET: Warm Golden Amber / Tawny Bronze"));
        }

        [Test]
        public void SpeciesColorPalettesAreDistinct()
        {
            // Flora species colors must be distinct
            Assert.That(FloraVisuals.Algae, Is.Not.EqualTo(FloraVisuals.Grass));
            Assert.That(FloraVisuals.Grass, Is.Not.EqualTo(FloraVisuals.TreeWood));
            Assert.That(FloraVisuals.Algae, Is.Not.EqualTo(FloraVisuals.TreeWood));

            Color algae = FloraVisuals.SpeciesColor(1);
            Color grass = FloraVisuals.SpeciesColor(2);
            Color tree = FloraVisuals.SpeciesColor(3, 4); // trunk
            Assert.That(algae, Is.EqualTo(FloraVisuals.Algae));
            Assert.That(grass, Is.EqualTo(FloraVisuals.Grass));
            Assert.That(tree, Is.EqualTo(FloraVisuals.TreeWood));

            // Fauna species colors must be distinct
            Assert.That(FaunaVisuals.CricketAdult, Is.Not.EqualTo(FaunaVisuals.WaspAdult));
            Color cricket = FaunaVisuals.SpeciesColor(1, 3);
            Color wasp = FaunaVisuals.SpeciesColor(2, 3);
            Assert.That(cricket, Is.EqualTo(FaunaVisuals.CricketAdult));
            Assert.That(wasp, Is.EqualTo(FaunaVisuals.WaspAdult));
        }
    }
}
