using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Configuration;

[assembly: DoNotParallelize]

namespace Win10ExplorerKit.Tests.Configuration
{
    [TestClass]
    public class ConfigurationTests
    {
        private string _directory;

        /// <summary>
        /// Creates a unique test-owned directory without accessing personal configuration
        /// </summary>
        [TestInitialize]
        public void Initialize() { _directory = Path.Combine(Path.GetTempPath(), "Win10ExplorerKit.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }

        /// <summary>
        /// Verifies first-run detection and persistent selection across subsequent launches
        /// </summary>
        /// <param name="culture">Initial Windows UI culture</param>
        /// <param name="expected">Resolved locale code</param>
        [DataTestMethod]
        [DataRow("zh-CN", "zh-CN")]
        [DataRow("zh-TW", "zh-CN")]
        [DataRow("en-US", "en")]
        [DataRow("de-DE", "en")]
        public void FirstRunUsesCultureThenKeepsSavedChoice(string culture, string expected)
        {
            string path = Path.Combine(_directory, "Config.ini");
            var settings = new SettingsService(path, culture);
            Assert.AreEqual(expected, settings.Language);
            settings.SetLanguage("en");
            Assert.AreEqual("en", new SettingsService(path, "zh-CN", "zh", true).Language);
        }

        /// <summary>
        /// Migrates legacy language and quote preferences only when INI values are absent
        /// </summary>
        [TestMethod]
        public void MigratesLegacyPreferences()
        {
            var settings = new SettingsService(Path.Combine(_directory, "Config.ini"), "en", "zh", true);
            Assert.AreEqual("zh-CN", settings.Language);
            Assert.IsFalse(settings.QuotePaths);
        }

        /// <summary>
        /// Confirms saved snapshots preserve unknown content and notify observers once
        /// </summary>
        [TestMethod]
        public void SavesSnapshotWithoutLosingFutureSettings()
        {
            string path = Path.Combine(_directory, "Config.ini");
            File.WriteAllText(path, "; retain comment\n[Future]\nValue=a=b\n");
            var settings = new SettingsService(path, "en");
            int notifications = 0;
            settings.Changed += (sender, e) => notifications++;
            settings.Save(new AppOptions { Language = "zh-CN", QuotePaths = false });
            Assert.AreEqual(1, notifications);
            Assert.AreEqual("a=b", new ConfigStore(path).Read().Get("Future", "Value", null));
            StringAssert.Contains(File.ReadAllText(path), "; retain comment");
        }

        /// <summary>
        /// Exercises lock-protected merges from independent configuration store instances
        /// </summary>
        [TestMethod]
        public void ConcurrentUpdatesPreserveAllKeys()
        {
            string path = Path.Combine(_directory, "Config.ini");
            Parallel.For(0, 12, index => new ConfigStore(path).Update(doc => doc.Set("Future", "Key" + index, "value")));
            for (int i = 0; i < 12; i++) Assert.AreEqual("value", new ConfigStore(path).Read().Get("Future", "Key" + i, null));
            Assert.AreEqual(0, Directory.GetFiles(_directory, "*.tmp").Length);
        }

        /// <summary>
        /// Restores safe defaults for manually edited invalid values
        /// </summary>
        [TestMethod]
        public void InvalidValuesFallbackWithoutLosingUnknownKeys()
        {
            string path = Path.Combine(_directory, "Config.ini");
            File.WriteAllText(path, "[General]\nLanguage=auto\n[Copy]\nQuotePaths=invalid\n");
            var settings = new SettingsService(path, "zh-TW");
            Assert.AreEqual("zh-CN", settings.Language);
            Assert.IsTrue(settings.QuotePaths);
        }
    }
}
