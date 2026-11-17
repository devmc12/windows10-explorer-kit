using System;
using System.IO;

namespace Win10ExplorerKit.App
{
    internal static class PortableSetup
    {
        /// <summary>
        /// Registers a portable location once, preserving later startup choices on normal launches
        /// </summary>
        /// <param name="directory">Application directory containing the portable marker</param>
        /// <param name="configPath">Per-user configuration file</param>
        /// <param name="executable">Executable path used for shell integration</param>
        /// <param name="register">Registration action completed before recording success</param>
        public static void EnsureRegistered(string directory, string configPath, string executable, Action register)
        {
            if (!File.Exists(Path.Combine(directory, "portable.flag"))) return;
            var store = new ConfigStore(configPath);
            string path = Path.GetFullPath(executable);
            if (string.Equals(store.Read().Get("Integration", "PortablePath", null), path, StringComparison.OrdinalIgnoreCase)) return;
            register();
            store.Update(document => document.Set("Integration", "PortablePath", path));
        }
    }
}
