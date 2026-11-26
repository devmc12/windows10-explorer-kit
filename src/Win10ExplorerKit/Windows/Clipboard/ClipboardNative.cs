// Declares native clipboard and memory ownership APIs
using System;
using System.Runtime.InteropServices;

namespace Win10ExplorerKit.Windows.Clipboard
{
    internal static class ClipboardNative
    {
        /// <summary>Acquires exclusive clipboard access for a non-null owner window</summary>
        /// <param name="owner">Window created on the calling thread</param><returns>Whether access was acquired</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenClipboard(IntPtr owner);

        /// <summary>Releases the clipboard lock acquired on this thread</summary>
        /// <returns>Whether the clipboard was closed</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseClipboard();

        /// <summary>Clears existing formats and assigns ownership to the window used to open the clipboard</summary>
        /// <returns>Whether the clipboard was emptied</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EmptyClipboard();

        /// <summary>Transfers a fully rendered global allocation to Windows on success</summary>
        /// <param name="format">Clipboard format identifier</param><param name="memory">Unlocked movable global allocation</param>
        /// <returns>The transferred handle, or zero on failure</returns>
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        /// <summary>Allocates a global memory block compatible with clipboard ownership</summary>
        /// <param name="flags">Allocation flags</param><param name="bytes">Number of bytes including the terminator</param>
        /// <returns>Owned allocation handle, or zero on failure</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        /// <summary>Locks a movable allocation to obtain its current address</summary>
        /// <param name="memory">Owned allocation handle</param><returns>Writable address, or zero on failure</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalLock(IntPtr memory);

        /// <summary>Releases the temporary address lock before clipboard ownership transfer</summary>
        /// <param name="memory">Locked allocation handle</param><returns>Whether the block remains locked</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(IntPtr memory);

        /// <summary>Frees an allocation only when ownership has not transferred to Windows</summary>
        /// <param name="memory">Owned allocation handle</param><returns>Zero on success, otherwise the original handle</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalFree(IntPtr memory);
    }
}
