using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

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
        /// Reads the selection from the foreground Explorer window on demand
        /// </summary>
        /// <returns>Selected paths, or an empty list when the window is unavailable</returns>
        public static List<string> GetSelectedPaths()
        {
            var result = new List<string>();
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero || !IsExplorerWindow(foreground))
            {
                return result;
            }

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    return result;
                }

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();
                int windowCount = Convert.ToInt32(windows.Count);
                for (int windowIndex = 0; windowIndex < windowCount; windowIndex++)
                {
                    dynamic window = windows.Item(windowIndex);
                    long windowHandle = Convert.ToInt64(window.HWND);
                    if (new IntPtr(windowHandle) != foreground)
                    {
                        continue;
                    }

                    dynamic selected = window.Document.SelectedItems();
                    int selectedCount = Convert.ToInt32(selected.Count);
                    for (int itemIndex = 0; itemIndex < selectedCount; itemIndex++)
                    {
                        dynamic item = selected.Item(itemIndex);
                        string path = Convert.ToString(item.Path);
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            result.Add(path);
                        }
                    }

                    break;
                }
            }
            catch
            {
                // Explorer may be closing or showing a virtual folder. In that case there
                // is no usable filesystem selection to copy
            }

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
