using System;
using System.IO;

namespace Win10ExplorerKit.App
{
    internal static class DataCleanup
    {
        /// <summary>
        /// Removes the application data directory without traversing directory links
        /// </summary>
        /// <param name="directory">Explicit application data directory ending in Win10ExplorerKit</param>
        public static void Clean(string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetFileName(root), "Win10ExplorerKit", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to clean an unrelated data directory");
            if (!Directory.Exists(root)) return;
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to clean a linked data directory");
            DeleteOwnedTree(root);
        }

        /// <summary>
        /// Deletes a previously validated owned tree while skipping reparse-point directories
        /// </summary>
        /// <param name="directory">Owned directory within the validated application root</param>
        private static void DeleteOwnedTree(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            foreach (string child in Directory.GetDirectories(directory)) DeleteOwnedTree(child);
            // Leave linked or inaccessible remnants for manual inspection instead of following them
            if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
        }
    }
}
