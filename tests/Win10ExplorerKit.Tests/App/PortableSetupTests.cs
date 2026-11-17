using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.App;

namespace Win10ExplorerKit.Tests.App
{
    [TestClass]
    public class PortableSetupTests
    {
        private string _directory;

        /// <summary>
        /// Creates isolated portable files without touching real shell registration
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            _directory = Path.Combine(Path.GetTempPath(), "Win10ExplorerKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        /// <summary>
        /// Verifies marker gating, repeat launches, relocation and re-registration after cleanup
        /// </summary>
        [TestMethod]
        public void RegistersEachPortableLocationOnce()
        {
            string config = Path.Combine(_directory, "Config.ini");
            string executable = Path.Combine(_directory, "Win10ExplorerKit.exe");
            int calls = 0;
            Action register = () => calls++;
            PortableSetup.EnsureRegistered(_directory, config, executable, register);
            Assert.AreEqual(0, calls);
            File.WriteAllText(Path.Combine(_directory, "portable.flag"), "portable");
            PortableSetup.EnsureRegistered(_directory, config, executable, register);
            PortableSetup.EnsureRegistered(_directory, config, executable, register);
            Assert.AreEqual(1, calls);
            PortableSetup.EnsureRegistered(_directory, config, Path.Combine(_directory, "moved", "Win10ExplorerKit.exe"), register);
            Assert.AreEqual(2, calls);
            File.Delete(config);
            PortableSetup.EnsureRegistered(_directory, config, executable, register);
            Assert.AreEqual(3, calls);
        }

        /// <summary>
        /// Leaves failed registration retryable without recording a successful first run
        /// </summary>
        [TestMethod]
        public void FailedRegistrationCanBeRetried()
        {
            string config = Path.Combine(_directory, "Config.ini");
            string executable = Path.Combine(_directory, "Win10ExplorerKit.exe");
            File.WriteAllText(Path.Combine(_directory, "portable.flag"), "portable");
            Assert.ThrowsException<IOException>(() => PortableSetup.EnsureRegistered(_directory, config, executable,
                () => { throw new IOException("simulated-registration-failure"); }));
            Assert.IsFalse(File.Exists(config));
            PortableSetup.EnsureRegistered(_directory, config, executable, () => { });
            Assert.IsTrue(File.Exists(config));
        }

        /// <summary>
        /// Removes only the unique temporary directory owned by this test
        /// </summary>
        [TestCleanup]
        public void Cleanup() { Directory.Delete(_directory, true); }
    }
}
