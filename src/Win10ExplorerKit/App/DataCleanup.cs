using System;
using System.IO;
using System.Globalization;
using System.Linq;

namespace Win10ExplorerKit.App
{
    internal static class DataCleanup
    {
        /// <summary>Deletes only recognized application data beside the executable</summary>
        /// <param name="directory">Program directory</param>
        public static void Clean(string directory)
        {
            if (!CanEnter(directory)) return;
            File.Delete(Path.Combine(directory, "Config.ini"));
            string logs = Path.Combine(directory, "logs");
            if (!CanEnter(logs)) return;
            foreach (string file in Directory.EnumerateFiles(logs, "*.log"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.Equals("application", StringComparison.OrdinalIgnoreCase) ||
                    DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _)) File.Delete(file);
            }
            RemoveIfEmpty(logs);
        }

        /// <summary>Checks whether the directory exists and is not a link</summary>
        /// <param name="path">Directory to inspect</param>
        /// <returns>Whether ordinary children can be accessed</returns>
        internal static bool CanEnter(string path) => Directory.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

        /// <summary>Retires a directory once its owned files have been removed</summary>
        /// <param name="path">Directory to inspect</param>
        internal static void RemoveIfEmpty(string path)
        {
            if (CanEnter(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
    }
}
