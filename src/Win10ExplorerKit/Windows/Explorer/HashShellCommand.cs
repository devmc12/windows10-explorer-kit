using System;
using System.Runtime.InteropServices;
using Win10ExplorerKit.App.Hashing;

namespace Win10ExplorerKit.Windows.Explorer
{
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class HashShellCommand : IHashExecuteCommand, IHashObjectWithSelection
    {
        private readonly HashApplicationContext _host;
        private IShellItemArray _selection;
        private bool _hidden;
        private readonly string _operation;

        /// <summary>Creates a lightweight shell invocation owned by the local server</summary>
        /// <param name="host">Shared hash window host</param>
        /// <param name="operation">Optional fixed permission action selected by the COM class</param>
        internal HashShellCommand(HashApplicationContext host, string operation = null) { _host = host; _operation = operation; }

        /// <summary>Stores the selection until Execute transfers it to the UI queue</summary>
        /// <param name="selection">Shell-owned selection array</param>
        public void SetSelection(IShellItemArray selection) { _selection = selection; }

        /// <summary>Queries the stored selection without assuming the requested interface</summary>
        /// <param name="iid">Requested interface</param>
        /// <param name="selection">Owned result pointer</param>
        public void GetSelection(ref Guid iid, out IntPtr selection)
        {
            selection = IntPtr.Zero;
            if (_selection == null) return;
            IntPtr unknown = Marshal.GetIUnknownForObject(_selection);
            try { Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, ref iid, out selection)); }
            finally { Marshal.Release(unknown); }
        }

        /// <summary>Queues work after the synchronous Explorer call returns</summary>
        public void Execute()
        {
            if (_hidden || _selection == null) return;
            _host.Accept(_selection, _operation);
            _selection = null;
        }

        /// <summary>Receives modifier keys</summary><param name="keys">Unused key state</param>
        public void SetKeyState(uint keys) { }
        /// <summary>Receives command parameters</summary><param name="parameters">Unused parameters</param>
        public void SetParameters(string parameters) { }
        /// <summary>Receives invocation coordinates</summary><param name="point">Unused screen point</param>
        public void SetPosition(System.Drawing.Point point) { }
        /// <summary>Receives show state</summary><param name="show">Unused show state</param>
        public void SetShowWindow(int show) { }
        /// <summary>Honors callers that prohibit interactive UI</summary><param name="hidden">Whether UI is prohibited</param>
        public void SetNoShowUI(bool hidden) { _hidden = hidden; }
        /// <summary>Receives caller context without changing the process directory</summary><param name="directory">Unused directory</param>
        public void SetDirectory(string directory) { }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class HashClassFactory : IHashClassFactory
    {
        private readonly HashApplicationContext _host;
        private readonly string _operation;

        /// <summary>Creates a factory for one hash-window host</summary><param name="host">UI host</param>
        /// <param name="operation">Optional permission action bound to this factory</param>
        internal HashClassFactory(HashApplicationContext host, string operation = null) { _host = host; _operation = operation; }

        /// <summary>Creates a command through the requested COM interface</summary>
        /// <param name="outer">Unsupported aggregation owner</param><param name="iid">Requested interface</param><param name="instance">AddRef-owned result</param><returns>COM status</returns>
        public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance)
        {
            instance = IntPtr.Zero;
            // CLASS_E_NOAGGREGATION rejects unsupported COM aggregation
            if (outer != IntPtr.Zero) return unchecked((int)0x80040110);
            IntPtr unknown = Marshal.GetIUnknownForObject(new HashShellCommand(_host, _operation));
            try { return Marshal.QueryInterface(unknown, ref iid, out instance); }
            finally { Marshal.Release(unknown); }
        }

        /// <summary>Retains a host lease while Explorer holds a server lock</summary>
        /// <param name="locked">Whether to acquire or release a lease</param><returns>COM success</returns>
        public int LockServer(bool locked) { _host.ServerLock(locked); return 0; }
    }
}
