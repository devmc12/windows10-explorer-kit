using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Win10ExplorerKit.Features.FolderRecovery
{
    internal sealed class FolderRecoveryService : IDisposable
    {
        private readonly SettingsService _settings;
        // Bound interactive shell enumeration waits to five seconds without interrupting COM in place
        private const int OpenTimeoutMilliseconds = 5000;
        private BackgroundSta _worker;
        private FolderMonitor _monitor;
        private FolderSession _session;
        private Task<string> _opening;
        private Task<(string[] Paths, string[] Existing)> _folderRead;
        private volatile bool _disposed;
        private volatile bool _remember;
        private bool _configuring;
        internal Task Completion => Task.WhenAll(_worker?.Completion ?? Task.CompletedTask, (Task)_opening ?? Task.CompletedTask);

        /// <summary>Activates monitoring only while the user preference is enabled</summary>
        /// <param name="settings">Shared application preferences</param>
        internal FolderRecoveryService(SettingsService settings)
        {
            _settings = settings;
            _settings.Changed += Configure;
            Configure(this, EventArgs.Empty);
        }

        /// <summary>Applies monitoring changes without disturbing an existing subscription</summary>
        /// <param name="sender">Settings service</param>
        /// <param name="args">Change notification</param>
        private async void Configure(object sender, EventArgs args)
        {
            _remember = _settings.RememberFolders;
            if (_disposed || _configuring || (!_remember && _worker == null)) return;
            _configuring = true;
            bool applied = _remember;
            try
            {
                await Worker.InvokeAsync(() =>
                {
                    if (_disposed) return false;
                    applied = _remember;
                    if (applied && _monitor == null)
                    {
                        if (_session == null) _session = new FolderSession(AppPaths.DataDirectory);
                        if (!_disposed) _monitor = new FolderMonitor(_session);
                    }
                    else if (!applied) { _monitor?.Dispose(); _monitor = null; }
                    return true;
                });
            }
            catch (Exception ex) { if (!_disposed) AppLogger.Write("Folder monitoring setup failed", ex); }
            finally
            {
                _configuring = false;
                if (!_disposed && applied != _remember) Configure(this, EventArgs.Empty);
            }
        }

        private BackgroundSta Worker => _worker ?? (_worker = new BackgroundSta("Folder recovery", () => _monitor?.Dispose()));

        /// <summary>Opens missing folders while keeping network access off the tray thread</summary>
        /// <param name="fixedList">Use the user's fixed list instead of the last session</param>
        /// <returns>Optional localized status for the tray</returns>
        internal async Task<string> OpenAsync(bool fixedList)
        {
            if (_disposed || (_opening != null && !_opening.IsCompleted)) return null;
            if (_folderRead != null && !_folderRead.IsCompleted) return TranslationService.T("folders.openError");
            // Snapshot user preferences before entering the worker apartment
            string[] fixedPaths = (string[])_settings.FixedFolders.Clone();
            _opening = OpenCoreAsync(fixedList, fixedPaths);
            return await _opening;
        }

        /// <summary>Reads session data and Explorer state on the worker before launching missing folders</summary>
        /// <param name="fixedList">Whether to use fixed paths</param><param name="fixedPaths">Immutable preference snapshot</param>
        /// <returns>Optional localized feedback</returns>
        private async Task<string> OpenCoreAsync(bool fixedList, string[] fixedPaths)
        {
            try
            {
                using (var deadline = new CancellationTokenSource(OpenTimeoutMilliseconds))
                {
                    CancellationToken token = deadline.Token;
                    Task<(string[] Paths, string[] Existing)> read = Worker.InvokeAsync(() =>
                    {
                        if (!fixedList && _session == null) _session = new FolderSession(AppPaths.DataDirectory);
                        string[] paths = fixedList ? fixedPaths : (string[])_session.RestorePaths.Clone();
                        token.ThrowIfCancellationRequested();
                        return (paths, paths.Length == 0 ? Array.Empty<string>() : ExplorerFolders.ReadOpen());
                    }, token);
                    _folderRead = read;
                    if (await Task.WhenAny(read, Task.Delay(Timeout.Infinite, token)) != read)
                    {
                        // Observe late failures without launching folders from an expired request
                        _ = ObserveReadAsync(read);
                        return _disposed ? null : TranslationService.T("folders.openError");
                    }
                    var snapshot = await read;
                    token.ThrowIfCancellationRequested();
                    deadline.Cancel();
                    if (_disposed) return null;
                    if (snapshot.Paths.Length == 0) return TranslationService.T(fixedList ? "folders.noFixed" : "folders.noSession");
                    var existing = new HashSet<string>(snapshot.Existing, StringComparer.OrdinalIgnoreCase);
                    int failed = await Task.Run(() => OpenMissing(snapshot.Paths, existing, () => _disposed));
                    return _disposed || failed == 0 ? null : TranslationService.T("folders.skipped", failed);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Write("Open folders failed", ex);
                return _disposed ? null : TranslationService.T("folders.openError");
            }
        }

        /// <summary>Observes a timed-out read without blocking shutdown or emitting private shell details</summary>
        /// <param name="read">In-flight shell read</param><returns>Completion after the native call returns</returns>
        private static async Task ObserveReadAsync(Task read)
        {
            try { await read.ConfigureAwait(false); }
            catch (Exception ex) { AppLogger.Write("Folder read expired", ex); }
        }

        /// <summary>Opens each distinct available folder with the registered shell verb</summary>
        /// <param name="paths">Absolute directories to open</param>
        /// <param name="existing">Folders already displayed by Explorer</param>
        /// <param name="stopping">Optional shutdown check between folder launches</param>
        /// <returns>Number of unavailable or failed paths</returns>
        internal static int OpenMissing(IEnumerable<string> paths, HashSet<string> existing, Func<bool> stopping = null)
        {
            int failed = 0;
            foreach (string path in paths)
            {
                if (stopping?.Invoke() == true) break;
                if (!existing.Add(path)) continue;
                try
                {
                    if (!Directory.Exists(path)) { failed++; continue; }
                    using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" })) { }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception ||
                    ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                { failed++; }
            }
            return failed;
        }

        /// <summary>Releases Explorer subscriptions and settings notifications</summary>
        public void Dispose()
        {
            _settings.Changed -= Configure;
            _disposed = true;
            _worker?.Dispose();
        }
    }
}
