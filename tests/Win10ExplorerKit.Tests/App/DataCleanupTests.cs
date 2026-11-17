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
        /// Verifies uninstall removes preferences, logs and unknown application data
        /// </summary>
        [TestMethod]
        public void RemovesEntireApplicationDataDirectory()
        {
            string root = Path.Combine(Path.GetTempPath(), "Win10ExplorerKit.Tests", Guid.NewGuid().ToString("N"), "Win10ExplorerKit");
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            File.WriteAllText(Path.Combine(root, "Config.ini"), "[General]\nLanguage=en");
            File.WriteAllText(Path.Combine(root, "logs", "application.log"), "sample");
            File.WriteAllText(Path.Combine(root, "future-setting.json"), "{}");
            DataCleanup.Clean(root);
            Assert.IsFalse(Directory.Exists(root));
        }

        /// <summary>
        /// Prevents an unrelated folder from becoming a cleanup target
        /// </summary>
        [TestMethod]
        public void RejectsUnrelatedDirectories()
        {
            Assert.ThrowsException<InvalidOperationException>(() => DataCleanup.Clean(Path.GetTempPath()));
        }
    }
}
