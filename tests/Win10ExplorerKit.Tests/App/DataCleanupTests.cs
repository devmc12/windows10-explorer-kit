using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.App;

namespace Win10ExplorerKit.Tests.App
{
    [TestClass]
    public class DataCleanupTests
    {
        /// <summary>
        /// Verifies uninstall removes recognized data and preserves unrelated files
        /// </summary>
        [TestMethod]
        public void RemovesOnlyOwnedRuntimeFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "Win10ExplorerKit.Tests", Guid.NewGuid().ToString("N"), "Win10ExplorerKit");
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            File.WriteAllText(Path.Combine(root, "Config.ini"), "[General]\nLanguage=en");
            File.WriteAllText(Path.Combine(root, "logs", "application.log"), "sample");
            File.WriteAllText(Path.Combine(root, "future-setting.json"), "{}");
            File.WriteAllText(Path.Combine(root, "logs", "notes.log"), "keep");
            DataCleanup.Clean(root);
            Assert.IsFalse(File.Exists(Path.Combine(root, "Config.ini")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "logs", "application.log")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "future-setting.json")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "logs", "notes.log")));
        }

        /// <summary>
        /// Exercises byte-preserving migration and destination precedence
        /// </summary>
        [TestMethod]
        public void MigratesLegacyFilesWithoutOverwritingExistingData()
        {
            string root = Path.Combine(Path.GetTempPath(), "Win10ExplorerKit.Tests", Guid.NewGuid().ToString("N"));
            string source = Path.Combine(root, "legacy");
            string target = Path.Combine(root, "portable");
            Directory.CreateDirectory(Path.Combine(source, "logs"));
            byte[] ini = System.Text.Encoding.UTF8.GetBytes("; keep\r\n[Future]\r\nValue=中文\r\n");
            File.WriteAllBytes(Path.Combine(source, "Config.ini"), ini);
            File.WriteAllText(Path.Combine(source, "logs", "application.log"), "original log");
            LegacyStorage.Migrate(source, target);
            CollectionAssert.AreEqual(ini, File.ReadAllBytes(Path.Combine(target, "Config.ini")));
            Assert.AreEqual("original log", File.ReadAllText(Path.Combine(target, "logs", "application.log")));
            Assert.IsFalse(Directory.Exists(source));
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Config.ini"), "another config");
            LegacyStorage.Migrate(source, target);
            CollectionAssert.AreEqual(ini, File.ReadAllBytes(Path.Combine(target, "Config.ini")));
            Assert.IsTrue(File.Exists(Path.Combine(source, "Config.ini")));
        }
    }
}
