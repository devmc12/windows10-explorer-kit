using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Win10ExplorerKit.Windows.Interop
{
    internal static class NativeMethods
    {
        /// <summary>Queues a notification on the window's owning thread</summary>
        /// <param name="window">Recipient handle</param>
        /// <param name="message">Native message identifier</param>
        /// <param name="wParam">First message parameter</param>
        /// <param name="lParam">Second message parameter</param>
        /// <returns>Whether the message was queued</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        /// <summary>Obtains the broadcast identifier shared with the Windows taskbar</summary>
        /// <param name="name">Registered message name</param>
        /// <returns>System-wide message identifier</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string name);

        /// <summary>
        /// Activates a tray popup so clicks outside it dismiss the menu normally
        /// </summary>
        /// <param name="window">Popup window handle</param>
        /// <returns>True when Windows accepts foreground activation</returns>
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr window);

        /// <summary>
        /// Notifies Explorer that registered association labels have changed
        /// </summary>
        /// <param name="eventId">Shell change event identifier</param>
        /// <param name="flags">Flags describing the event data</param>
        /// <param name="item1">First optional event item</param>
        /// <param name="item2">Second optional event item</param>
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

        /// <summary>
        /// Registers a keyboard shortcut with the Windows message loop
        /// </summary>
        /// <param name="hWnd">Window receiving hotkey messages</param>
        /// <param name="id">Application-defined hotkey identifier</param>
        /// <param name="modifiers">Modifier key flags</param>
        /// <param name="virtualKey">Virtual key code</param>
        /// <returns>True if registration succeeded</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

        /// <summary>
        /// Releases a registered shortcut so other applications can receive it
        /// </summary>
        /// <param name="hWnd">Window owning the shortcut</param>
        /// <param name="id">Previously registered hotkey identifier</param>
        /// <returns>True if the shortcut was released</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        /// <summary>
        /// Reads the current foreground window from Windows
        /// </summary>
        /// <returns>The foreground window handle or zero</returns>
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// Reads a native window class into the supplied buffer
        /// </summary>
        /// <param name="hWnd">Window whose class is requested</param>
        /// <param name="className">Output buffer for the class name</param>
        /// <param name="maxCount">Output capacity in characters</param>
        /// <returns>The number of characters written, excluding the terminator</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        /// <summary>
        /// Looks up the process owning a native window
        /// </summary>
        /// <param name="hWnd">Window whose owner is requested</param>
        /// <param name="processId">Receives the owning process identifier</param>
        /// <returns>The owning thread identifier</returns>
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }

}
