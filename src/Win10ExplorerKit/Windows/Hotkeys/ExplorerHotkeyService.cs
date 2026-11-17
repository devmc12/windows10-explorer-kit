using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Win10ExplorerKit.Windows.Hotkeys
{
    internal sealed class ExplorerHotkeyService : NativeWindow, IDisposable
    {
        // Application-defined identifier for the Explorer copy-path shortcut
        private const int HotkeyId = 0x4C50;
        // Windows message identifying a registered hotkey press
        private const int WmHotkey = 0x0312;
        // Control, Shift and no-repeat modifier flags for RegisterHotKey
        private const uint Modifiers = 0x0002 | 0x0004 | 0x4000;
        private readonly Timer _timer = new Timer { Interval = 80 };
        private readonly Action _copy;
        private readonly Action _conflict;
        private bool _registered;
        private IntPtr _previous;
        private DateTime _retry;
        private int _error;

        /// <summary>
        /// Creates a message-only window so shortcut lifetime is independent of GUI forms
        /// </summary>
        /// <param name="copy">Action invoked for a valid Explorer shortcut</param>
        /// <param name="conflict">Notification invoked on a new registration error</param>
        public ExplorerHotkeyService(Action copy, Action conflict)
        {
            _copy = copy;
            _conflict = conflict;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
            _timer.Tick += (sender, e) => Refresh();
            _timer.Start();
            Refresh();
        }

        /// <summary>
        /// Registers only while Explorer is foreground and bounds retry frequency
        /// </summary>
        private void Refresh()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == _previous && (_registered || DateTime.UtcNow < _retry)) return;
            _previous = foreground;
            bool explorer = ExplorerSelection.IsFileExplorerWindow(foreground);
            if (explorer && !_registered)
            {
                _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, Modifiers, (uint)Keys.C);
                if (!_registered)
                {
                    int error = Marshal.GetLastWin32Error();
                    _retry = DateTime.UtcNow.AddSeconds(1);
                    if (error != _error) { _error = error; AppLogger.Write("RegisterHotKey failed: " + error, null); _conflict(); }
                }
                else _error = 0;
            }
            else if (!explorer && _registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }
            if (!explorer) _retry = DateTime.MaxValue;
        }

        /// <summary>
        /// Validates foreground ownership again before executing the copy action
        /// </summary>
        /// <param name="message">Native message being dispatched</param>
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId &&
                ExplorerSelection.IsFileExplorerWindow(NativeMethods.GetForegroundWindow())) _copy();
            base.WndProc(ref message);
        }

        /// <summary>
        /// Stops polling and releases native registrations before destroying the handle
        /// </summary>
        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            if (_registered) NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }
    }
}
