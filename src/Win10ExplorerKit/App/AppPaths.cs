using System;
using System.IO;

namespace Win10ExplorerKit.App
{
    internal static class AppPaths
    {
        // Activation always targets the main executable
        internal static string ExecutablePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Win10ExplorerKit.exe");
        // Writable directory shared by program files and runtime data
        public static readonly string DataDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Original per-user directory used during migration
        public static readonly string LegacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win10ExplorerKit");
    }
}
