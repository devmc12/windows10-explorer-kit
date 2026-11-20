using System;
using System.IO;

namespace Win10ExplorerKit.App
{
    internal static class PortableSetup
    {
        // Revision 2 replaces AnyCPU COM activation with native host executables
        private const string HashRegistrationVersion = "2";
        /// <summary>
        /// Registers a portable location once, preserving later startup choices on normal launches
        /// </summary>
        /// <param name="directory">Application directory containing the portable marker</param>
        /// <param name="configPath">Per-user configuration file</param>
        /// <param name="executable">Executable path used for shell integration</param>
        /// <param name="register">Registration action completed before recording success</param>
        /// <param name="upgrade">Optional registration upgrade that preserves startup choices</param>
        public static void EnsureRegistered(string directory, string configPath, string executable, Action register, Action upgrade = null)
        {
            if (!File.Exists(Path.Combine(directory, "portable.flag"))) return;
            var store = new ConfigStore(configPath);
            string path = Path.GetFullPath(executable);
            IniDocument document = store.Read();
            bool samePath = string.Equals(document.Get("Integration", "PortablePath", null), path, StringComparison.OrdinalIgnoreCase);
            bool needsUpgrade = upgrade != null && document.Get("Integration", "HashRegistration", null) != HashRegistrationVersion;
            if (samePath && !needsUpgrade) return;
            if (!samePath) register();
            if (needsUpgrade) upgrade();
            store.Update(updated =>
            {
                updated.Set("Integration", "PortablePath", path);
                if (upgrade != null) updated.Set("Integration", "HashRegistration", HashRegistrationVersion);
            });
        }
    }
}
