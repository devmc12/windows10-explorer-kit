using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.App;
using Win10ExplorerKit.Configuration;
using Win10ExplorerKit.Features.FolderRecovery;

namespace Win10ExplorerKit.Tests.Features
{
    [TestClass]
    public class FolderRecoveryTests
    {
        private string _directory;

        /// <summary>Creates a private fixture directory for each session</summary>
        [TestInitialize]
        public void Initialize()
        {
            _directory = Path.Combine(Path.GetTempPath(), "Win10ExplorerKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        /// <summary>Removes files created by the fixture</summary>
        [TestCleanup]
        public void Cleanup() => Directory.Delete(_directory, true);

        /// <summary>A new window after shell or application restart must leave the lost group available</summary>
        [TestMethod]
        public void PreservesRecoveryUntilRestored()
        {
            string[] original = { @"C:\Example\One", @"C:\Example\Two" };
            var session = new FolderSession(_directory);
            session.Observe(original);
            session = new FolderSession(_directory);
            session.Observe(Array.Empty<string>());
            session.Observe(new[] { @"C:\Example\Three" });
            CollectionAssert.AreEqual(original, session.RestorePaths);
            session = new FolderSession(_directory);
            CollectionAssert.AreEqual(original, session.RestorePaths);
            session.Observe(new[] { original[0], original[1], @"C:\Example\Three" });
            Assert.AreEqual(3, session.RestorePaths.Length);
            DataCleanup.Clean(_directory);
            Assert.IsFalse(File.Exists(Path.Combine(_directory, FolderSession.FileName)));
        }

        /// <summary>An older recovery offer must not mask the latest group on a subsequent shell loss</summary>
        [TestMethod]
        public void NextLossPinsTheMostRecentGroup()
        {
            var session = new FolderSession(_directory);
            session.Observe(new[] { @"C:\One" });
            session.Preserve();
            session.Observe(new[] { @"C:\Two", @"C:\Three" });
            session.Preserve();
            session.Observe(new[] { @"C:\Four" });
            CollectionAssert.AreEqual(new[] { @"C:\Two", @"C:\Three" }, session.RestorePaths);
        }

        /// <summary>Reordered windows, duplicate paths and an empty desktop require no additional writes</summary>
        [TestMethod]
        public void IgnoresEquivalentSetsWithoutRewriting()
        {
            var session = new FolderSession(_directory);
            session.Observe(new[] { @"C:\Alpha", @"D:\Beta" });
            string file = Path.Combine(_directory, FolderSession.FileName);
            using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                session.Observe(new[] { @"D:\Beta", @"C:\alpha\", @"C:\Alpha" });
                session.Observe(Array.Empty<string>());
            }
            CollectionAssert.AreEquivalent(new[] { @"C:\Alpha", @"D:\Beta" }, session.RestorePaths);
        }

        /// <summary>Fixed folders survive language changes, preserve Unicode and reject commands</summary>
        [TestMethod]
        public void PersistsValidatedFixedFoldersIndependentlyOfLanguage()
        {
            string file = Path.Combine(_directory, "Config.ini");
            var settings = new SettingsService(file, "en");
            Assert.IsFalse(settings.RememberFolders);
            settings.Save(new AppOptions { Language = "en", FixedFolders = new[] {
                @"C:\Sample\中文", @"c:\sample\中文\", @"\\example.invalid\share", @"\\example.invalid\share\" } });
            settings.SetLanguage("zh-CN");
            CollectionAssert.AreEqual(new[] { @"C:\Sample\中文", @"\\example.invalid\share\" },
                new SettingsService(file, "en").FixedFolders);
            Assert.ThrowsException<FormatException>(() => settings.Save(new AppOptions {
                Language = "en", FixedFolders = new[] { "shell:Downloads" } }));
            Assert.IsNull(FolderPaths.Normalize("C:relative"));
            Assert.IsNull(FolderPaths.Normalize(@"\\.\pipe\Example"));
        }

        /// <summary>An unavailable folder leaves later entries eligible for processing</summary>
        [TestMethod]
        public void SkipsOpenAndMissingFolders()
        {
            string absent = Path.Combine(_directory, "absent");
            var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _directory };
            Assert.AreEqual(1, FolderRecoveryService.OpenMissing(new[] { absent, _directory, absent }, open));
        }
    }
}
