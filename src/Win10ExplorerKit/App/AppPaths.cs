using System;
using System.IO;

namespace Win10ExplorerKit.App
{
    internal static class AppPaths
    {
        // Current-user storage is separate from the installation and source directories
        public static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win10ExplorerKit");
    }
}
