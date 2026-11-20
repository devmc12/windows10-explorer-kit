using System;
using System.Runtime.InteropServices;
using Win10ExplorerKit.Windows.Interop;

internal static class MenuClient
{
    /// <summary>Invokes the same native menu path used for an Explorer selection</summary>
    /// <param name="selection">Flat array of siblings, including mixed files and folders</param>
    /// <param name="verb">Temporary registered verb</param>
    internal static void Invoke(IShellItemArray selection, string verb)
    {
        // BHID_SFUIObject binds the selection to its native context menu
        Guid handler = new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5");
        Guid iid = typeof(IContextMenu).GUID;
        IntPtr pointer;
        selection.BindToHandler(IntPtr.Zero, ref handler, ref iid, out pointer);
        var menu = (IContextMenu)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        IntPtr popup = CreatePopupMenu();
        IntPtr name = Marshal.StringToCoTaskMemAnsi(verb);
        try
        {
            Marshal.ThrowExceptionForHR(menu.QueryContextMenu(popup, 0, 1, 0x7fff, 0));
            var invoke = new InvokeInfo { Size = Marshal.SizeOf(typeof(InvokeInfo)), Verb = name, Show = 1 };
            menu.InvokeCommand(ref invoke);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
            DestroyMenu(popup);
            Marshal.ReleaseComObject(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeInfo
    {
        internal int Size;
        internal uint Mask;
        internal IntPtr Window, Verb, Parameters, Directory;
        internal int Show;
        internal uint HotKey;
        internal IntPtr Icon;
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        /// <summary>Populates the context menu</summary>
        /// <param name="menu">Popup</param><param name="index">Insert index</param>
        /// <param name="first">First ID</param><param name="last">Last ID</param>
        /// <param name="flags">Menu flags</param><returns>HRESULT containing command count</returns>
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        /// <summary>Executes a registered verb</summary><param name="info">Invocation data</param>
        void InvokeCommand(ref InvokeInfo info);
        /// <summary>Retrieves a canonical command string</summary>
        /// <param name="id">Command offset</param><param name="type">String kind</param>
        /// <param name="reserved">Reserved</param><param name="name">Buffer</param><param name="count">Capacity</param>
        void GetCommandString(UIntPtr id, uint type, IntPtr reserved, IntPtr name, uint count);
    }

    /// <summary>Allocates an empty native popup</summary><returns>Menu handle</returns>
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    /// <summary>Releases a native menu</summary><param name="menu">Menu handle</param><returns>Success</returns>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);
}
