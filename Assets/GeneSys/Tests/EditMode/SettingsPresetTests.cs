using System;
using System.IO;
using GeneSys.Configuration;
using GeneSys.Persistence;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class SettingsPresetTests
    {
        private string directory;
        private SettingsPresetService service;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "genesys-preset-tests", Guid.NewGuid().ToString("N"));
            service = new SettingsPresetService(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void SaveAndLoadRoundTripsSimulationSettings()
        {
            SimulationConfig source = ScriptableObject.CreateInstance<SimulationConfig>();
            source.seed = 4242;
            source.mantlePressure = 0.37f;
            source.enableStarfield = 0;
            source.grid = PolarGridDefinition.Standard;
            Assert.That(service.Save(source, "wet-world", out string saveError), Is.True, saveError);
            Assert.That(File.Exists(Path.Combine(directory, "wet-world.preset")), Is.True);

            SimulationConfig loaded = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(service.Load(loaded, "wet-world", out string loadError), Is.True, loadError);
            Assert.That(loaded.seed, Is.EqualTo(4242));
            Assert.That(loaded.mantlePressure, Is.EqualTo(0.37f).Within(0.0001f));
            Assert.That(loaded.enableStarfield, Is.EqualTo(0));
            Assert.That(loaded.grid.angularResolution, Is.EqualTo(PolarGridDefinition.Standard.angularResolution));
            Assert.That(loaded.grid.radialResolution, Is.EqualTo(PolarGridDefinition.Standard.radialResolution));
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(loaded);
        }

        [Test]
        public void SaveStripsPresetExtensionAndOverwrites()
        {
            SimulationConfig config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.seed = 1;
            Assert.That(service.Save(config, "coast.preset", out _), Is.True);
            config.seed = 2;
            Assert.That(service.Save(config, "coast", out _), Is.True);
            Assert.That(File.Exists(Path.Combine(directory, "coast.preset")), Is.True);
            Assert.That(File.Exists(Path.Combine(directory, "coast.preset.preset")), Is.False);

            SimulationConfig loaded = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(service.Load(loaded, "coast.preset", out _), Is.True);
            Assert.That(loaded.seed, Is.EqualTo(2));
            UnityEngine.Object.DestroyImmediate(config);
            UnityEngine.Object.DestroyImmediate(loaded);
        }

        [Test]
        public void RejectsInvalidFilenames()
        {
            Assert.That(SettingsPresetService.TryNormalizeFileName("", out _, out string emptyError), Is.False);
            Assert.That(emptyError, Is.Not.Empty);
            Assert.That(SettingsPresetService.TryNormalizeFileName("bad:name", out _, out string colonError), Is.False);
            Assert.That(colonError, Is.EqualTo(SettingsPresetService.InvalidCharactersMessage));
            Assert.That(SettingsPresetService.TryNormalizeFileName("bad*name", out _, out string starError), Is.False);
            Assert.That(starError, Is.EqualTo(SettingsPresetService.InvalidCharactersMessage));
            Assert.That(SettingsPresetService.TryNormalizeFileName("..", out _, out _), Is.False);
        }

        [Test]
        public void ListPresetsReturnsOnlyPresetFiles()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "keep.preset"), "{}");
            File.WriteAllText(Path.Combine(directory, "ignore.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "also.txt"), "nope");
            var names = service.ListPresets();
            Assert.That(names, Is.EqualTo(new[] { "keep" }));
        }
    }
}
