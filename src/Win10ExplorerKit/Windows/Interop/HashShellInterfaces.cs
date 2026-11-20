using System;
using System.Runtime.InteropServices;

namespace Win10ExplorerKit.Windows.Interop
{
    // Interface layouts follow the Windows SDK vtable order
    [ComVisible(true), Guid("7F9185B0-CB92-43C5-80A9-92277A4F7B54"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IHashExecuteCommand
    {
        /// <summary>Receives modifier keys</summary><param name="keys">Shell key state</param>
        void SetKeyState(uint keys);
        /// <summary>Receives optional command parameters</summary><param name="parameters">Shell parameters</param>
        void SetParameters([MarshalAs(UnmanagedType.LPWStr)] string parameters);
        /// <summary>Receives the invocation position</summary><param name="point">Screen point</param>
        void SetPosition(System.Drawing.Point point);
        /// <summary>Receives the window show mode</summary><param name="show">Show mode</param>
        void SetShowWindow(int show);
        /// <summary>Receives the caller's UI preference</summary><param name="hidden">Whether UI is prohibited</param>
        void SetNoShowUI([MarshalAs(UnmanagedType.Bool)] bool hidden);
        /// <summary>Receives the working directory</summary><param name="directory">Caller directory</param>
        void SetDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        /// <summary>Queues the selected items for the hash window</summary>
        void Execute();
    }

    [ComVisible(true), Guid("1C9CD5BB-98E9-4491-A60F-31AACC72B83C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IHashObjectWithSelection
    {
        /// <summary>Receives a complete shell selection</summary><param name="selection">Marshaled item array</param>
        void SetSelection(IShellItemArray selection);
        /// <summary>Returns the selection interface requested by Explorer</summary>
        /// <param name="iid">Requested interface</param><param name="selection">AddRef-owned output pointer</param>
        void GetSelection(ref Guid iid, out IntPtr selection);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemArray
    {
        /// <summary>Binds an array handler</summary><param name="context">Bind context</param><param name="handler">Handler ID</param><param name="iid">Interface ID</param><param name="value">Result</param>
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
        /// <summary>Reads array properties</summary><param name="flags">Store flags</param><param name="iid">Interface ID</param><param name="value">Result</param>
        void GetPropertyStore(int flags, ref Guid iid, out IntPtr value);
        /// <summary>Reads a property description list</summary><param name="key">Property key pointer</param><param name="iid">Interface ID</param><param name="value">Result</param>
        void GetPropertyDescriptionList(IntPtr key, ref Guid iid, out IntPtr value);
        /// <summary>Reads combined item attributes</summary><param name="flags">Combination flags</param><param name="mask">Attribute mask</param><param name="attributes">Result</param>
        void GetAttributes(uint flags, uint mask, out uint attributes);
        /// <summary>Counts selected items</summary><param name="count">Item count</param>
        void GetCount(out uint count);
        /// <summary>Reads one selected item</summary><param name="index">Zero-based index</param><param name="item">Owned item reference</param>
        void GetItemAt(uint index, out IShellItem item);
        /// <summary>Creates a shell enumerator</summary><param name="items">Enumerator pointer</param>
        void EnumItems(out IntPtr items);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        /// <summary>Binds an item handler</summary><param name="context">Bind context</param><param name="handler">Handler ID</param><param name="iid">Interface ID</param><param name="value">Result</param>
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
        /// <summary>Gets the parent item</summary><param name="parent">Owned parent reference</param>
        void GetParent(out IShellItem parent);
        /// <summary>Reads an item name</summary><param name="kind">SIGDN format</param><param name="name">CoTaskMem string</param>
        void GetDisplayName(uint kind, out IntPtr name);
        /// <summary>Reads item attributes</summary><param name="mask">Attribute mask</param><param name="attributes">Result</param>
        void GetAttributes(uint mask, out uint attributes);
        /// <summary>Compares shell items</summary><param name="other">Other item</param><param name="hint">Comparison hint</param><param name="order">Result</param>
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IHashClassFactory
    {
        /// <summary>Creates a command object</summary><param name="outer">Aggregation owner</param><param name="iid">Requested interface</param><param name="instance">Result pointer</param><returns>COM status</returns>
        [PreserveSig] int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance);
        /// <summary>Updates COM server lifetime</summary><param name="locked">Whether to hold a server lock</param><returns>COM status</returns>
        [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
    }

    internal static class HashComNative
    {
        /// <summary>Initializes the STA for incoming Shell calls</summary><param name="reserved">Zero</param><returns>COM status</returns>
        [DllImport("ole32.dll")] internal static extern int OleInitialize(IntPtr reserved);
        /// <summary>Releases the STA initialization</summary>
        [DllImport("ole32.dll")] internal static extern void OleUninitialize();
        /// <summary>Publishes a local-server class factory</summary><param name="clsid">Class ID</param><param name="factory">Factory pointer</param><param name="context">Activation context</param><param name="flags">Registration flags</param><param name="cookie">Registration token</param><returns>COM status</returns>
        [DllImport("ole32.dll")] internal static extern int CoRegisterClassObject(ref Guid clsid, IntPtr factory, uint context, uint flags, out uint cookie);
        /// <summary>Stops accepting new activations</summary><param name="cookie">Registration token</param><returns>COM status</returns>
        [DllImport("ole32.dll")] internal static extern int CoRevokeClassObject(uint cookie);
    }
}
