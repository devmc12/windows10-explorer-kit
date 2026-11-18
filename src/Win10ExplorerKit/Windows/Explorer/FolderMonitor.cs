using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Win10ExplorerKit.Windows.Explorer
{
    internal sealed class FolderMonitor : NativeWindow, IDisposable
    {
        // Shell window registration and revocation dispatch identifiers from ExDisp
        private const int Registered = 200, Revoked = 201;
        // DWebBrowserEvents2 dispatch identifier for completed navigation
        private const int Navigated = 252;
        // Private WM_APP message that transfers COM callbacks to the owning tray thread
        private const int FoldersChanged = 0x8001;
        // Quiet interval in milliseconds for a burst of shell events
        private const int DebounceMilliseconds = 900;
        // Maximum retry delay while Explorer is unavailable, in milliseconds
        private const int MaxRetryMilliseconds = 30000;
        private static readonly Guid ShellEvents = new Guid("FE4106E0-399A-11D0-A48C-00A0C90A8F39");
        private static readonly Guid BrowserEvents = new Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D");
        private delegate void NavigationEvent(object dispatch, ref object url);
        private readonly Dictionary<IntPtr, (object Browser, int ProcessId)> _browsers =
            new Dictionary<IntPtr, (object, int)>();
        private readonly FolderSession _session;
        private readonly Timer _timer = new Timer();
        private readonly Action<int> _windowsChanged;
        private readonly NavigationEvent _navigated;
        private readonly int _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        private object _windows;
        private int _retry = DebounceMilliseconds;
        private bool _reconnect;
        private bool _ending;
        private bool _disposed;

        /// <summary>Subscribes once, then refreshes only after shell events or connection failures</summary>
        /// <param name="session">Snapshot store owned by the tray thread</param>
        internal FolderMonitor(FolderSession session)
        {
            _session = session;
            _windowsChanged = cookie => NativeMethods.PostMessage(Handle, FoldersChanged, IntPtr.Zero, IntPtr.Zero);
            _navigated = (object dispatch, ref object url) => NativeMethods.PostMessage(Handle, FoldersChanged, IntPtr.Zero, IntPtr.Zero);
            _timer.Tick += (sender, args) => Refresh();
            // An invisible top-level window receives Explorer restart and shutdown broadcasts
            CreateHandle(new CreateParams { ExStyle = 0x80 });
            Refresh();
        }

        /// <summary>Coalesces shell notifications into one enumeration on the UI apartment</summary>
        /// <param name="delay">Delay in milliseconds</param>
        private void Schedule(int delay)
        {
            if (_disposed || _ending) return;
            _timer.Stop();
            _timer.Interval = delay;
            _timer.Start();
        }

        /// <summary>Refreshes subscriptions and publishes only a complete enumeration</summary>
        private void Refresh()
        {
            _timer.Stop();
            if (_disposed || _ending) return;
            try
            {
                if (_reconnect)
                {
                    Disconnect();
                    _reconnect = false;
                }
                if (_windows == null)
                {
                    _windows = ExplorerFolders.Connect();
                    ComEventsHelper.Combine(_windows, ShellEvents, Registered, _windowsChanged);
                    ComEventsHelper.Combine(_windows, ShellEvents, Revoked, _windowsChanged);
                }
                var active = new HashSet<IntPtr>();
                IntPtr[] old = _browsers.Keys.ToArray();
                string[] paths = ExplorerFolders.Read(_windows, (handle, browser) =>
                {
                    active.Add(handle);
                    if (_browsers.ContainsKey(handle)) return false;
                    ComEventsHelper.Combine(browser, BrowserEvents, Navigated, _navigated);
                    NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
                    _browsers.Add(handle, (browser, (int)processId));
                    return true;
                });
                var lost = old.Where(h => !active.Contains(h)).ToArray();
                // A separate Explorer process may exit while windows from another process survive
                if (lost.Length > 0 && (lost.Length == old.Length ||
                    lost.Any(h => HasExited(_browsers[h].ProcessId)))) _session.Preserve();
                foreach (IntPtr handle in lost) RemoveBrowser(handle);
                _session.Observe(paths);
                _retry = DebounceMilliseconds;
            }
            catch (Exception ex)
            {
                if (_retry == DebounceMilliseconds)
                {
                    _session.Preserve();
                    AppLogger.Write("Folder monitoring interrupted", ex);
                }
                Disconnect();
                _retry = Math.Min(_retry * 2, MaxRetryMilliseconds);
                Schedule(_retry);
            }
        }

        /// <summary>Checks the owner only when a tracked window disappears</summary>
        /// <param name="id">Original Explorer process identifier</param>
        /// <returns>Whether the process terminated</returns>
        private static bool HasExited(int id)
        {
            try { using (var process = Process.GetProcessById(id)) return process.HasExited; }
            catch (ArgumentException) { return true; }
        }

        /// <summary>Removes a navigation sink before releasing its browser</summary>
        /// <param name="handle">Window that closed or lost its shell connection</param>
        private void RemoveBrowser(IntPtr handle)
        {
            object browser = _browsers[handle].Browser;
            Detach(browser, BrowserEvents, Navigated, _navigated);
            ExplorerFolders.Release(browser);
            _browsers.Remove(handle);
        }

        /// <summary>Unsubscribes from a possibly disconnected Explorer process</summary>
        /// <param name="source">Automation event source</param>
        /// <param name="iid">Outgoing interface identifier</param>
        /// <param name="id">Event dispatch identifier</param>
        /// <param name="handler">Previously registered handler</param>
        private static void Detach(object source, Guid iid, int id, Delegate handler)
        {
            try { ComEventsHelper.Remove(source, iid, id, handler); }
            catch (Exception ex) when (ex is COMException || ex is InvalidComObjectException) { }
        }

        /// <summary>Releases all subscriptions before reconnecting or disabling monitoring</summary>
        private void Disconnect()
        {
            foreach (IntPtr key in _browsers.Keys.ToArray()) RemoveBrowser(key);
            if (_windows == null) return;
            Detach(_windows, ShellEvents, Registered, _windowsChanged);
            Detach(_windows, ShellEvents, Revoked, _windowsChanged);
            ExplorerFolders.Release(_windows);
            _windows = null;
        }

        /// <summary>Freezes recovery during shutdown and reconnects when Explorer restarts</summary>
        /// <param name="message">Native Windows broadcast</param>
        protected override void WndProc(ref Message message)
        {
            const int QueryEndSession = 0x11, EndSession = 0x16;
            if (message.Msg == FoldersChanged) Schedule(DebounceMilliseconds);
            else if (message.Msg == QueryEndSession)
            {
                _ending = true;
                _timer.Stop();
                _session.Preserve();
            }
            else if (message.Msg == EndSession && message.WParam == IntPtr.Zero)
            {
                _ending = false;
                Schedule(DebounceMilliseconds);
            }
            else if (message.Msg == _taskbarCreated && !_disposed)
            {
                _session.Preserve();
                _reconnect = true;
                Schedule(DebounceMilliseconds);
            }
            base.WndProc(ref message);
        }

        /// <summary>Stops all callbacks and releases the hidden broadcast receiver</summary>
        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            Disconnect();
            DestroyHandle();
        }
    }
}
