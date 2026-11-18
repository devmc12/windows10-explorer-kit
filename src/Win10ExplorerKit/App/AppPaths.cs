using System;
using System.IO;

namespace Win10ExplorerKit.App
{
    internal static class AppPaths
    {
        // Writable directory shared by program files and runtime data
        public static readonly string DataDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Original per-user directory used during migration
        public static readonly string LegacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win10ExplorerKit");
    }
}
