using System;
using System.IO;
using GeneSys.Persistence;
using NUnit.Framework;

namespace GeneSys.Tests
{
    public sealed class WorldSnapshotCatalogTests
    {
        private string directory;
        private WorldSnapshotService service;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "genesys-world-catalog-tests", Guid.NewGuid().ToString("N"));
            service = new WorldSnapshotService(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void TryNormalizeFileNameStripsSnapshotExtension()
        {
            Assert.That(WorldSnapshotService.TryNormalizeFileName("coast.snapshot", out string name, out string error), Is.True, error);
            Assert.That(name, Is.EqualTo("coast"));
            Assert.That(WorldSnapshotService.TryNormalizeFileName("coast", out string withoutExtension, out _), Is.True);
            Assert.That(withoutExtension, Is.EqualTo("coast"));
        }

        [Test]
        public void RejectsInvalidFilenames()
        {
            Assert.That(WorldSnapshotService.TryNormalizeFileName("", out _, out string emptyError), Is.False);
            Assert.That(emptyError, Is.Not.Empty);
            Assert.That(WorldSnapshotService.TryNormalizeFileName("bad:name", out _, out string colonError), Is.False);
            Assert.That(colonError, Is.EqualTo(WorldSnapshotService.InvalidCharactersMessage));
            Assert.That(WorldSnapshotService.TryNormalizeFileName("bad*name", out _, out string starError), Is.False);
            Assert.That(starError, Is.EqualTo(WorldSnapshotService.InvalidCharactersMessage));
            Assert.That(WorldSnapshotService.TryNormalizeFileName("..", out _, out _), Is.False);
        }

        [Test]
        public void GetPathResolvesUnderWorldsDirectory()
        {
            Assert.That(service.GetPath("coast-01"), Is.EqualTo(Path.Combine(directory, "coast-01.snapshot")));
        }

        [Test]
        public void ListWorldsReturnsSortedSnapshotNames()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "zeta.snapshot"), "a");
            File.WriteAllText(Path.Combine(directory, "alpha.snapshot"), "b");
            File.WriteAllText(Path.Combine(directory, "ignore.json"), "nope");
            File.WriteAllText(Path.Combine(directory, "also.txt"), "nope");
            var names = service.ListWorlds();
            Assert.That(names, Is.EqualTo(new[] { "alpha", "zeta" }));
        }
    }
}
