using System;
using System.IO;
using System.Threading;

namespace Win10ExplorerKit.App
{
    internal static class LegacyStorage
    {
        /// <summary>Imports existing runtime files before initialization</summary>
        /// <param name="source">Legacy data directory</param>
        /// <param name="target">Program directory</param>
        public static void Migrate(string source, string target)
        {
            if (!DataCleanup.CanEnter(source) || string.Equals(Path.GetFullPath(source).TrimEnd('\\'),
                Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            // Concurrent shell commands can arrive during the first launch
            using (var mutex = new Mutex(false, @"Local\Win10ExplorerKit.StorageMigration"))
            {
                try { if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) throw new IOException("Storage migration is busy"); }
                catch (AbandonedMutexException) { }
                try
                {
                    ImportFile(source, target, "Config.ini");
                    string logs = Path.Combine(source, "logs");
                    if (DataCleanup.CanEnter(logs))
                    {
                        ImportFile(logs, Path.Combine(target, "logs"), "application.log");
                        DataCleanup.RemoveIfEmpty(logs);
                    }
                    DataCleanup.RemoveIfEmpty(source);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        /// <summary>Publishes a complete copy before retiring the original, including across volumes</summary>
        /// <param name="source">Legacy parent directory</param>
        /// <param name="target">Destination parent directory</param>
        /// <param name="name">Owned filename</param>
        private static void ImportFile(string source, string target, string name)
        {
            string original = Path.Combine(source, name);
            string destination = Path.Combine(target, name);
            if (!File.Exists(original) || File.Exists(destination) ||
                (File.GetAttributes(original) & FileAttributes.ReparsePoint) != 0) return;
            Directory.CreateDirectory(target);
            if (!DataCleanup.CanEnter(target)) throw new IOException("Linked migration destination");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(original, temporary);
                File.Move(temporary, destination);
                File.Delete(original);
            }
            finally { File.Delete(temporary); }
        }
    }
}
