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
        public string[] Languages => TranslationService.AvailableLanguages.ToArray();
        public string[] Themes => new[] { "System", "Light", "Dark" };
        public string Language { get => _settings.Language; set { if (value != null && value != Language) Save(() => _settings.SetLanguage(value)); } }
        public string Theme { get => _settings.Theme; set { if (value != null && value != Theme) Save(() => _settings.SetTheme(value)); } }
        public bool QuotePaths { get => _settings.QuotePaths; set { if (value != QuotePaths) Save(() => _settings.SetQuotePaths(value)); } }
        public bool RememberFolders { get => _settings.RememberFolders; set { if (value != RememberFolders) Save(() => SaveFolders(value, _settings.FixedFolders)); } }
        public bool StartAtLogin { get => _startup.IsEnabled(); set { Save(() => _startup.SetEnabled(value)); Changed(); } }
        public string FixedText { get => _fixedText; set { _fixedText = value; Changed(); } }
        public string Version => ProductInfo.Name + " " + typeof(SettingsViewModel).Assembly.GetName().Version.ToString(3);
        /// <summary>Shares immediate preferences and a separately committed folder editor</summary>
        /// <param name="settings">Persistent configuration</param><param name="folders">Folder commands</param><param name="notify">User feedback</param>
        internal SettingsViewModel(SettingsService settings, FolderRecoveryService folders, Action<string> notify)
        {
            _settings = settings; _folders = folders; _notify = notify;
            _fixedText = string.Join(Environment.NewLine, settings.FixedFolders);
            settings.Changed += Reload;
        }
        /// <summary>Refreshes committed controls while retaining unsaved folder input</summary>
        /// <param name="sender">Configuration service</param><param name="args">Change event</param>
        private void Reload(object sender, EventArgs args) => Changed("");
        /// <summary>Reports persistence errors and restores bindings to committed values</summary>
        /// <param name="action">Validated update</param>
        private void Save(Action action)
        {
            try { action(); }
            catch (FormatException) { _notify(TranslationService.T("folders.invalid")); }
            catch (Exception ex) { AppLogger.Write("Settings save failed", ex); _notify(TranslationService.T("error.settings")); }
            Changed("");
        }
        /// <summary>Persists folder preferences without changing language or copy settings</summary>
        /// <param name="remember">Automatic recording flag</param><param name="paths">Fixed folders</param>
        private void SaveFolders(bool remember, string[] paths) => _settings.Save(new AppOptions
        { Language = _settings.Language, QuotePaths = _settings.QuotePaths, RememberFolders = remember, FixedFolders = paths });
        /// <summary>Validates and stores the explicit folder-editor draft</summary>
        internal void SaveFixedFolders() => Save(() =>
        {
            SaveFolders(_settings.RememberFolders, (FixedText ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
            FixedText = string.Join(Environment.NewLine, _settings.FixedFolders);
            _notify(TranslationService.T("settings.saved"));
        });
        /// <summary>Opens the stored fixed list or recovery snapshot</summary>
        /// <param name="fixedList">Use fixed folders rather than the last snapshot</param>
        /// <returns>Completion after launching folders</returns>
        internal async Task OpenFoldersAsync(bool fixedList)
        {
            string status = await _folders.OpenAsync(fixedList);
            if (status != null) _notify(status);
        }
        /// <summary>Opens the existing daily-log directory</summary>
        internal void OpenLogs() => Save(() =>
        {
            System.IO.Directory.CreateDirectory(AppLogger.LogDirectory);
            Process.Start(new ProcessStartInfo(AppLogger.LogDirectory) { UseShellExecute = true });
        });
        /// <summary>Releases the shared settings subscription</summary>
        public void Dispose() => _settings.Changed -= Reload;
    }
}
