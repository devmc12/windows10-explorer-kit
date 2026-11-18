using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Win10ExplorerKit.Features.FolderRecovery
{
    internal sealed class FolderRecoveryService : IDisposable
    {
        private readonly SettingsService _settings;
        private FolderMonitor _monitor;
        private FolderSession _session;
        private bool _opening;

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
        private void Configure(object sender, EventArgs args)
        {
            if (_settings.RememberFolders && _monitor == null)
            {
                _session = new FolderSession(AppPaths.DataDirectory);
                _monitor = new FolderMonitor(_session);
            }
            else if (!_settings.RememberFolders)
            {
                _monitor?.Dispose();
                _monitor = null;
            }
        }

        /// <summary>Opens missing folders while keeping network access off the tray thread</summary>
        /// <param name="fixedList">Use the user's fixed list instead of the last session</param>
        /// <returns>Optional localized status for the tray</returns>
        internal async Task<string> OpenAsync(bool fixedList)
        {
            if (_opening) return null;
            _opening = true;
            try
            {
                if (!fixedList && _session == null) _session = new FolderSession(AppPaths.DataDirectory);
                string[] paths = fixedList ? _settings.FixedFolders : _session.RestorePaths;
                if (paths.Length == 0) return TranslationService.T(fixedList ? "folders.noFixed" : "folders.noSession");
                var existing = new HashSet<string>(ExplorerFolders.ReadOpen(), StringComparer.OrdinalIgnoreCase);
                int failed = await Task.Run(() => OpenMissing(paths, existing));
                if (failed != 0) return TranslationService.T("folders.skipped", failed);
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Write("Open folders failed", ex);
                return TranslationService.T("folders.openError");
            }
            finally { _opening = false; }
        }

        /// <summary>Opens each distinct available folder with the registered shell verb</summary>
        /// <param name="paths">Absolute directories to open</param>
        /// <param name="existing">Folders already displayed by Explorer</param>
        /// <returns>Number of unavailable or failed paths</returns>
        internal static int OpenMissing(IEnumerable<string> paths, HashSet<string> existing)
        {
            int failed = 0;
            foreach (string path in paths)
            {
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
            _monitor?.Dispose();
        }
    }
}
