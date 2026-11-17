using System;
using Microsoft.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Windows.Startup;

namespace Win10ExplorerKit.Tests.Windows
{
    [TestClass]
    public class StartupRegistrationTests
    {
        /// <summary>
        /// Verifies quoted commands and reversible registration without touching actual login entries
        /// </summary>
        [TestMethod]
        public void TogglePreservesOtherEntriesAndPersistsAcrossInstances()
        {
            string path = @"Software\Win10ExplorerKit.Tests\" + Guid.NewGuid().ToString("N");
            // Synthetic path exercises quoting without referring to an installed application
            const string executable = @"C:\Example\App Folder\Win10ExplorerKit.exe";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path)) key.SetValue("OtherApp", "preserve");
                var startup = new StartupRegistration(executable, path);
                Assert.IsFalse(startup.IsEnabled());
                startup.SetEnabled(true);
                Assert.IsTrue(new StartupRegistration(executable, path).IsEnabled());
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
                    Assert.AreEqual("\"" + executable + "\" --hotkey", key.GetValue("Win10ExplorerKitHotkey"));
                startup.SetEnabled(false);
                startup.SetEnabled(false);
                Assert.IsFalse(startup.IsEnabled());
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path)) Assert.AreEqual("preserve", key.GetValue("OtherApp"));
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        }
    }
}
