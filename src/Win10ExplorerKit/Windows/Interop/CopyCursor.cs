using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Win10ExplorerKit.Windows.Interop
{
    /// <summary>Owns a native OLE arrow-and-plus cursor for copyable results</summary>
    internal sealed class CopyCursor : IDisposable
    {
        // OLE's arrow-and-plus copy resource on supported Windows versions
        private const int CopyResource = 6;
        // Load the DLL as resources without executing its initialization code
        private const uint LoadAsDataFile = 0x00000002;
        // LoadImage resource type for a cursor
        private const uint CursorImage = 2;
        // Match the system cursor size without sharing ownership of the handle
        private const uint DefaultSize = 0x00000040;
        private IntPtr _handle;
        internal Cursor Value { get; private set; }

        /// <summary>Loads the Windows copy cursor, falling back if its resource is unavailable</summary>
        internal CopyCursor()
        {
            Value = Cursors.Default;
            // An absolute system path prevents DLL lookup in the installation directory
            IntPtr module = LoadLibraryEx(Path.Combine(Environment.SystemDirectory, "ole32.dll"), IntPtr.Zero, LoadAsDataFile);
            if (module == IntPtr.Zero) return;
            try
            {
                _handle = LoadImage(module, new IntPtr(CopyResource), CursorImage, 0, 0, DefaultSize);
                if (_handle != IntPtr.Zero) Value = new Cursor(_handle);
            }
            finally { FreeLibrary(module); }
        }

        /// <summary>Releases the privately loaded cursor after the control stops using it</summary>
        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            // Cursor(IntPtr) does not own its native handle
            Value.Dispose();
            DestroyCursor(_handle);
            _handle = IntPtr.Zero;
            Value = Cursors.Default;
        }

        /// <summary>Opens a system module for resource loading</summary>
        /// <param name="path">Absolute module path</param>
        /// <param name="file">Reserved null handle</param>
        /// <param name="flags">Module loading flags</param>
        /// <returns>Module handle or zero on failure</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW")]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

        /// <summary>Loads a privately owned cursor image from a module</summary>
        /// <param name="module">Resource module</param>
        /// <param name="name">Integer resource identifier</param>
        /// <param name="type">Image resource type</param>
        /// <param name="width">Requested width, or zero for the default</param>
        /// <param name="height">Requested height, or zero for the default</param>
        /// <param name="flags">Sizing and ownership flags</param>
        /// <returns>Cursor handle or zero on failure</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
        private static extern IntPtr LoadImage(IntPtr module, IntPtr name, uint type, int width, int height, uint flags);

        /// <summary>Releases the resource module</summary>
        /// <param name="module">Module handle</param>
        /// <returns>Whether the module was released</returns>
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);

        /// <summary>Releases a non-shared cursor image</summary>
        /// <param name="cursor">Privately owned cursor handle</param>
        /// <returns>Whether the cursor was released</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyCursor(IntPtr cursor);
    }
}
