using System;
using System.Windows.Forms;

namespace Win10ExplorerKit.App
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly TrayController _tray;
        private readonly ExplorerHotkeyService _hotkey;
        private readonly Timer _shutdownTimer;
        private bool _disposed;

        /// <summary>
        /// Owns the tray and background services without attaching lifetime to a form
        /// </summary>
        /// <param name="settings">Shared user settings service</param>
        /// <param name="instance">Current session ownership and shutdown signal</param>
        /// <param name="showSettings">Whether to open settings on this launch</param>
        /// <param name="forcePlain">Whether this launch overrides the saved quoting preference</param>
        public TrayApplicationContext(SettingsService settings, SingleInstance instance, bool showSettings = false, bool forcePlain = false)
        {
            _tray = new TrayController(settings, ExitThread);
            if (showSettings) _tray.ShowSettings();
            _hotkey = new ExplorerHotkeyService(
                () => CopyPathService.PutOnClipboard(ExplorerSelection.GetSelectedPaths(), forcePlain || !settings.QuotePaths),
                _tray.ShowHotkeyConflict);
            _shutdownTimer = new Timer { Interval = 150 };
            _shutdownTimer.Tick += (sender, e) => { if (instance.StopRequested()) ExitThread(); };
            _shutdownTimer.Start();
            ShellIntegration.RefreshLabels();
            AppLogger.Write("Host started " + typeof(TrayApplicationContext).Assembly.GetName().Version, null);
        }

        /// <summary>
        /// Releases services in reverse order while leaving persisted preferences intact
        /// </summary>
        /// <param name="disposing">Whether managed resources should be released</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                // Both the message loop and the owner may dispose the application context
                _disposed = true;
                _shutdownTimer.Dispose();
                _hotkey.Dispose();
                _tray.Dispose();
                AppLogger.Write("Host stopped", null);
            }
            base.Dispose(disposing);
        }
    }
}
