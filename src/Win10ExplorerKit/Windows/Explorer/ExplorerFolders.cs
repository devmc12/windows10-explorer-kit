using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Win10ExplorerKit.Windows.Explorer
{
    internal static class ExplorerFolders
    {
        /// <summary>Acquires the current shell's window collection on the caller's STA</summary>
        /// <returns>Owned ShellWindows COM reference</returns>
        internal static object Connect()
        {
            // CLSID_ShellWindows exposes the collection directly without a Shell.Application wrapper
            return Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true));
        }

        /// <summary>Reads open filesystem folders once for an explicit tray operation</summary>
        /// <returns>Available folder paths</returns>
        internal static string[] ReadOpen()
        {
            object windows = Connect();
            try { return Read(windows, null); }
            finally { Release(windows); }
        }

        /// <summary>Enumerates folder windows and optionally registers a navigation callback</summary>
        /// <param name="collection">Owned shell window collection</param>
        /// <param name="visit">Returns true to retain the browser reference for a new subscription</param>
        /// <returns>Distinct paths from a complete enumeration</returns>
        internal static string[] Read(object collection, Func<IntPtr, object, bool> visit)
        {
            int count = (int)Member(collection, "Count");
            var paths = new List<string>();
            for (int i = 0; i < count; ++i)
            {
                object window = Member(collection, "Item", i);
                if (window == null) throw new COMException("Shell window collection changed");
                bool retained = false;
                object document = null, folder = null, item = null;
                try
                {
                    var handle = new IntPtr(Convert.ToInt64(Member(window, "HWND")));
                    if (!ExplorerSelection.IsFileExplorerWindow(handle)) continue;
                    retained = visit?.Invoke(handle, window) == true;
                    document = Member(window, "Document");
                    if (document == null) throw new COMException("Shell view is initializing");
                    folder = Member(document, "Folder");
                    item = Member(folder, "Self");
                    if ((bool)Member(item, "IsFileSystem")) paths.Add((string)Member(item, "Path"));
                }
                finally
                {
                    Release(item);
                    Release(folder);
                    Release(document);
                    if (!retained) Release(window);
                }
            }
            if (count != (int)Member(collection, "Count")) throw new COMException("Shell window collection changed");
            return FolderPaths.Distinct(paths);
        }

        /// <summary>Invokes shell automation directly without allocating dynamic call sites</summary>
        /// <param name="source">Automation object</param>
        /// <param name="name">Member name from the shell type library</param>
        /// <param name="arguments">Optional member arguments</param>
        /// <returns>Automation value or an owned COM reference</returns>
        private static object Member(object source, string name, params object[] arguments) =>
            source.GetType().InvokeMember(name, BindingFlags.GetProperty | BindingFlags.InvokeMethod,
                null, source, arguments);

        /// <summary>Releases one owned automation reference on its creating apartment</summary>
        /// <param name="value">Optional COM object</param>
        internal static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }
}
