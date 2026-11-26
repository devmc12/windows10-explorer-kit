// Selects filesystem folders through the native shell dialog
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Win10ExplorerKit.Windows.Interop;

namespace Win10ExplorerKit.UI.Services
{
    internal static class FolderPicker
    {
        /// <summary>Shows the native multi-folder picker without a WinForms window</summary>
        /// <param name="owner">Owning WPF window</param><returns>Selected filesystem paths</returns>
        internal static string[] Show(Window owner)
        {
            object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), true));
            var picker = (IFileOpenDialog)instance;
            IShellItemArray items = null;
            try
            {
                picker.GetOptions(out uint options);
                // Pick folders, require filesystem paths, permit multi-selection and require existing paths
                picker.SetOptions(options | 0x20 | 0x40 | 0x200 | 0x800);
                int result = picker.Show(new WindowInteropHelper(owner).Handle);
                // HRESULT_FROM_WIN32(ERROR_CANCELLED) is a normal dismissal
                if (result == unchecked((int)0x800704C7)) return Array.Empty<string>();
                Marshal.ThrowExceptionForHR(result);
                picker.GetResults(out items); items.GetCount(out uint count);
                var paths = new List<string>();
                for (uint index = 0; index < count; index++)
                {
                    items.GetItemAt(index, out IShellItem item);
                    IntPtr name = IntPtr.Zero;
                    try
                    {
                        // SIGDN_FILESYSPATH returns a plain absolute path
                        item.GetDisplayName(0x80058000, out name); paths.Add(Marshal.PtrToStringUni(name));
                    }
                    finally { if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name); Marshal.ReleaseComObject(item); }
                }
                return paths.ToArray();
            }
            finally { if (items != null) Marshal.ReleaseComObject(items); Marshal.ReleaseComObject(instance); }
        }

        // Preserve the complete native vtable order, including methods unused by this picker
        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            /// <summary>Shows the picker</summary><param name="owner">Parent window</param><returns>HRESULT</returns>
            [PreserveSig] int Show(IntPtr owner);
            /// <summary>Reserved native slot for filters</summary>
            void SetFileTypes();
            /// <summary>Reserved native slot for selected filter</summary>
            void SetFileTypeIndex();
            /// <summary>Reserved native slot for reading selected filter</summary>
            void GetFileTypeIndex();
            /// <summary>Reserved native slot for event subscription</summary>
            void Advise();
            /// <summary>Reserved native slot for event removal</summary>
            void Unadvise();
            /// <summary>Sets native picker options</summary><param name="options">FOS mask</param>
            void SetOptions(uint options);
            /// <summary>Reads native picker options</summary><param name="options">FOS mask</param>
            void GetOptions(out uint options);
            /// <summary>Reserved default-folder slot</summary>
            void SetDefaultFolder();
            /// <summary>Reserved current-folder slot</summary>
            void SetFolder();
            /// <summary>Reserved current-folder getter slot</summary>
            void GetFolder();
            /// <summary>Reserved current-selection slot</summary>
            void GetCurrentSelection();
            /// <summary>Reserved filename slot</summary>
            void SetFileName();
            /// <summary>Reserved filename getter slot</summary>
            void GetFileName();
            /// <summary>Reserved title slot</summary>
            void SetTitle();
            /// <summary>Reserved confirmation label slot</summary>
            void SetOkButtonLabel();
            /// <summary>Reserved filename label slot</summary>
            void SetFileNameLabel();
            /// <summary>Reserved single-result slot</summary>
            void GetResult();
            /// <summary>Reserved places slot</summary>
            void AddPlace();
            /// <summary>Reserved extension slot</summary>
            void SetDefaultExtension();
            /// <summary>Reserved close slot</summary>
            void Close();
            /// <summary>Reserved client identity slot</summary>
            void SetClientGuid();
            /// <summary>Reserved client data slot</summary>
            void ClearClientData();
            /// <summary>Reserved filter slot</summary>
            void SetFilter();
            /// <summary>Retrieves all chosen folder objects</summary><param name="items">Owned result collection</param>
            void GetResults(out IShellItemArray items);
            /// <summary>Reserved selected-items slot</summary>
            void GetSelectedItems();
        }
    }
}
