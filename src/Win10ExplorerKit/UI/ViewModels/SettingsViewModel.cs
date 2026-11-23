using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Win10ExplorerKit.Windows.Startup;
namespace Win10ExplorerKit.UI.ViewModels
{
    internal sealed class SettingsViewModel : ObservableObject, IDisposable
    {
        private readonly SettingsService _settings;
        private readonly FolderRecoveryService _folders;
        private readonly Action<string> _notify;
        private readonly StartupRegistration _startup = new StartupRegistration(AppPaths.ExecutablePath);
        private string _fixedText;
        private Task _saving = Task.CompletedTask;
        private bool _startAtLogin;
        private int _startupRevision;
        private bool _disposed;
        private bool _stopping;
        private bool _openingLogs;
        private Task _logs = Task.CompletedTask;
        private readonly Task _startupRead;
        public string[] Languages => TranslationService.AvailableLanguages.ToArray();
        public string[] Themes => new[] { "System", "Light", "Dark" };
        public string Language { get => _settings.Language; set { if (value != null && (value != Language || !_saving.IsCompleted)) Save(() => _settings.SetLanguageAsync(value)); } }
        public string Theme { get => _settings.Theme; set { if (value != null && (value != Theme || !_saving.IsCompleted)) Save(() => _settings.SetThemeAsync(value)); } }
        public bool QuotePaths { get => _settings.QuotePaths; set { if (value != QuotePaths || !_saving.IsCompleted) Save(() => _settings.SetQuotePathsAsync(value)); } }
        public bool RememberFolders { get => _settings.RememberFolders; set { if (value != RememberFolders || !_saving.IsCompleted) Save(() => SaveFoldersAsync(value, _settings.FixedFolders)); } }
        public bool StartAtLogin
        {
            get => _startAtLogin;
            set
            {
                _startupRevision++;
                Save(async () => { await Task.Run(() => _startup.SetEnabled(value)); _startAtLogin = value; });
            }
        }
        public string FixedText { get => _fixedText; set { _fixedText = value; Changed(); } }
        public string Version => ProductInfo.Name + " " + typeof(SettingsViewModel).Assembly.GetName().Version.ToString(3);
        /// <summary>Shares immediate preferences and a separately committed folder editor</summary>
        /// <param name="settings">Persistent configuration</param><param name="folders">Folder commands</param><param name="notify">User feedback</param>
        internal SettingsViewModel(SettingsService settings, FolderRecoveryService folders, Action<string> notify)
        {
            _settings = settings; _folders = folders; _notify = notify;
            _fixedText = string.Join(Environment.NewLine, settings.FixedFolders);
            settings.Changed += Reload;
            _startupRead = LoadStartupAsync();
        }
        /// <summary>Loads startup registration once without registry access from repeated binding getters</summary>
        /// <returns>Completion of the initial preference read</returns>
        private async Task LoadStartupAsync()
        {
            try
            {
                bool enabled = await Task.Run(() => _startup.IsEnabled());
                if (!_disposed && !_stopping && _startupRevision == 0) { _startAtLogin = enabled; Changed(nameof(StartAtLogin)); }
            }
            catch (Exception ex) { AppLogger.Write("Startup preference read failed", ex); }
        }
        /// <summary>Refreshes committed controls while retaining unsaved folder input</summary>
        /// <param name="sender">Configuration service</param><param name="args">Change event</param>
        private void Reload(object sender, EventArgs args) { if (!_stopping && !_disposed) Changed(""); }
        /// <summary>Reports persistence errors and restores bindings to committed values</summary>
        /// <param name="action">Validated update</param>
        private void Save(Func<Task> action)
        {
            if (_disposed || _stopping) return;
            _saving = SaveAsync(_saving, action);
        }
        /// <summary>Serializes user edits while allowing the dispatcher to keep handling input</summary>
        /// <param name="previous">Previously queued save</param><param name="action">Next update</param>
        /// <returns>Completion after error reporting and binding refresh</returns>
        private async Task SaveAsync(Task previous, Func<Task> action)
        {
            try { await previous; }
            catch (Exception ex) { AppLogger.Write("Previous settings update failed", ex); }
            try { await action(); }
            catch (FormatException) { Report("folders.invalid"); }
            catch (Exception ex) { AppLogger.Write("Settings save failed", ex); Report("error.settings"); }
            if (!_disposed && !_stopping) Changed("");
        }
        /// <summary>Suppresses late UI feedback after application shutdown starts</summary>
        /// <param name="key">Localized feedback key</param>
        private void Report(string key) { if (!_disposed && !_stopping) _notify(TranslationService.T(key)); }
        /// <summary>Persists folder preferences without changing language or copy settings</summary>
        /// <param name="remember">Automatic recording flag</param><param name="paths">Fixed folders</param>
        /// <returns>Completion after persistence and notifications</returns>
        private Task SaveFoldersAsync(bool remember, string[] paths) => _settings.SaveAsync(new AppOptions
        { Language = _settings.Language, QuotePaths = _settings.QuotePaths, RememberFolders = remember, FixedFolders = paths });
        /// <summary>Validates and stores the explicit folder-editor draft</summary>
        internal void SaveFixedFolders()
        {
            string draft = FixedText ?? "";
            Save(async () =>
            {
                await SaveFoldersAsync(_settings.RememberFolders, draft.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
                if (FixedText == draft) FixedText = string.Join(Environment.NewLine, _settings.FixedFolders);
                Report("settings.saved");
            });
        }
        /// <summary>Opens the stored fixed list or recovery snapshot</summary>
        /// <param name="fixedList">Use fixed folders rather than the last snapshot</param>
        /// <returns>Completion after launching folders</returns>
        internal async Task OpenFoldersAsync(bool fixedList)
        {
            if (_stopping || _disposed) return;
            string status = await _folders.OpenAsync(fixedList);
            if (status != null && !_stopping && !_disposed) _notify(status);
        }
        /// <summary>Opens the existing daily-log directory</summary>
        internal void OpenLogs()
        {
            if (_disposed || _stopping || _openingLogs) return;
            _logs = OpenLogsAsync();
        }
        /// <summary>Tracks the shell launch so shutdown can observe its completion</summary>
        /// <returns>Completion after the log directory opens or the shell fails</returns>
        private async Task OpenLogsAsync()
        {
            _openingLogs = true;
            try
            {
                await Task.Run(() =>
                {
                    System.IO.Directory.CreateDirectory(AppLogger.LogDirectory);
                    using (Process.Start(new ProcessStartInfo(AppLogger.LogDirectory) { UseShellExecute = true })) { }
                });
            }
            catch (Exception ex) { AppLogger.Write("Open logs failed", ex); Report("error.operation"); }
            finally { _openingLogs = false; }
        }
        /// <summary>Finishes queued preference writes before a normal application exit</summary>
        /// <returns>Completion of pending saves</returns>
        internal Task FlushAsync() { _stopping = true; return Task.WhenAll(_saving, _logs, _startupRead); }
        /// <summary>Releases the shared settings subscription</summary>
        public void Dispose() { _disposed = true; _settings.Changed -= Reload; }
    }
}
