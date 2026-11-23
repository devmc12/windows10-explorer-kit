using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Win10ExplorerKit.Windows.Explorer
{
    internal static class ExplorerSelection
    {
        /// <summary>
        /// Checks process identity and class without repeatedly calling Shell COM
        /// </summary>
        /// <param name="foreground">Candidate foreground window handle</param>
        /// <returns>True for supported File Explorer windows</returns>
        public static bool IsFileExplorerWindow(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero || !IsExplorerWindow(foreground))
            {
                return false;
            }

            // Read only the window class here; query COM selection only after a hotkey event
            var className = new StringBuilder(256);
            if (NativeMethods.GetClassName(foreground, className, className.Capacity) == 0)
            {
                return false;
            }

            return string.Equals(className.ToString(), "CabinetWClass", StringComparison.Ordinal) ||
                   string.Equals(className.ToString(), "ExploreWClass", StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads a captured Explorer selection and releases every COM reference on the calling STA
        /// </summary>
        /// <param name="foreground">Explorer window captured when the shortcut was pressed</param>
        /// <param name="cancellationToken">Stops enumeration between COM calls after timeout or shutdown</param>
        /// <returns>Selected paths, or an empty list when the window is unavailable</returns>
        public static List<string> GetSelectedPaths(IntPtr foreground, CancellationToken cancellationToken)
        {
            var result = new List<string>();
            if (!IsFileExplorerWindow(foreground)) return result;
            object windows = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                windows = ExplorerFolders.Connect();
                int windowCount = Convert.ToInt32(ExplorerFolders.Member(windows, "Count"));
                for (int windowIndex = 0; windowIndex < windowCount; windowIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    object window = null, document = null, selected = null;
                    try
                    {
                        window = ExplorerFolders.Member(windows, "Item", windowIndex);
                        if (window == null) continue;
                        long windowHandle = Convert.ToInt64(ExplorerFolders.Member(window, "HWND"));
                        if (new IntPtr(windowHandle) != foreground) continue;
                        document = ExplorerFolders.Member(window, "Document");
                        if (document == null) return result;
                        selected = ExplorerFolders.Member(document, "SelectedItems");
                        int selectedCount = Convert.ToInt32(ExplorerFolders.Member(selected, "Count"));
                        for (int itemIndex = 0; itemIndex < selectedCount; itemIndex++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            object item = ExplorerFolders.Member(selected, "Item", itemIndex);
                            try
                            {
                                string path = Convert.ToString(ExplorerFolders.Member(item, "Path"));
                                if (!string.IsNullOrWhiteSpace(path)) result.Add(path);
                            }
                            finally { ExplorerFolders.Release(item); }
                        }
                        break;
                    }
                    finally
                    {
                        ExplorerFolders.Release(selected);
                        ExplorerFolders.Release(document);
                        ExplorerFolders.Release(window);
                    }
                }
            }
            finally { ExplorerFolders.Release(windows); }
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        /// <summary>
        /// Checks whether a window belongs to the Explorer process
        /// </summary>
        /// <param name="window">Candidate window handle</param>
        /// <returns>True if the owning process is Explorer</returns>
        public static bool IsExplorerWindow(IntPtr window)
        {
            uint processId;
            NativeMethods.GetWindowThreadProcessId(window, out processId);
            if (processId == 0)
            {
                return false;
            }

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                    return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
