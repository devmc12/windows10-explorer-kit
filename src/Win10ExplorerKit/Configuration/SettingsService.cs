using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using Win10ExplorerKit.Features.FileHash;

namespace Win10ExplorerKit.Configuration
{
    // 1. Scope: typed application preferences backed by an extensible INI store
    // 2. Flow: migrate old preferences once, resolve language, expose typed settings
    // 3. Rules: preserve unknown keys; keep installation registry entries separate
    // 4. Usage: Load at process startup, then use SetLanguage or SetQuotePaths to persist
    internal sealed class SettingsService
    {
        // Legacy registry location read only during migration to Config.ini
        private const string LegacyRegistryPath = @"Software\Win10ExplorerKit";
        // Preferences reside beside the executable
        public static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "Config.ini");
        // Active store can point to an isolated file during configuration tests
        private readonly ConfigStore _store;
        // Canonical locale code resolved from saved settings or first-run system language
        public string Language { get; private set; }
        public string Theme { get; private set; }
        // Persistent output format shared by hotkey and shell commands
        public bool QuotePaths { get; private set; }
        public bool RememberFolders { get; private set; }
        public string[] FixedFolders { get; private set; }
        public string HashAlgorithm { get; private set; }
        public int HashParallelism { get; private set; }
        public int HashFolderDepth { get; private set; }
        public event EventHandler Changed;

        /// <summary>
        /// Loads per-user INI settings and migrates legacy language and output preferences
        /// </summary>
        /// <returns>The settings service for the current Windows user</returns>
        /// <param name="initialLanguage">Installer locale for a new configuration</param>
        public static SettingsService Load(string initialLanguage = null)
        {
            LegacyStorage.Migrate(AppPaths.LegacyDirectory, AppPaths.DataDirectory);
            string legacyLanguage = null, legacyStartup = null;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LegacyRegistryPath))
                if (key != null) legacyLanguage = key.GetValue("Language") as string;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                if (key != null) legacyStartup = key.GetValue("Win10ExplorerKitHotkey") as string;
            var settings = new SettingsService(FilePath, initialLanguage ?? CultureInfo.CurrentUICulture.Name, legacyLanguage,
                legacyStartup != null && legacyStartup.IndexOf(" --plain", StringComparison.OrdinalIgnoreCase) >= 0);
            // Retire the old preference only after the INI file has been saved successfully
            if (legacyLanguage != null)
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LegacyRegistryPath, true))
                    if (key != null) key.DeleteValue("Language", false);
            return settings;
        }

        /// <summary>
        /// Resolves settings from a supplied location without accessing registry or clipboard
        /// </summary>
        /// <param name="path">INI file location, including isolated test locations</param>
        /// <param name="cultureName">System UI culture used on first run</param>
        /// <param name="legacyLanguage">Optional saved language from the previous storage</param>
        /// <param name="legacyPlain">Whether previous startup arguments requested plain output</param>
        public SettingsService(string path, string cultureName, string legacyLanguage = null, bool legacyPlain = false)
        {
            _store = new ConfigStore(path);
            IniDocument current = _store.Read();
            string language = current.Get("General", "Language", null);
            bool quote;
            if (language == null || language.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                language != TranslationService.Resolve(language) || !bool.TryParse(current.Get("Copy", "QuotePaths", null), out quote))
            {
                _store.Update(delegate (IniDocument document)
                {
                    // Resolve under the lock so simultaneous first launches cannot overwrite a newer choice
                    string saved = document.Get("General", "Language", null);
                    string chosen = saved == null ? (legacyLanguage ?? cultureName) :
                        (saved.Equals("auto", StringComparison.OrdinalIgnoreCase) ? cultureName : saved);
                    document.Set("General", "Language", TranslationService.Resolve(chosen));
                    document.Set("Copy", "QuotePaths", document.GetBoolean("Copy", "QuotePaths", !legacyPlain) ? "true" : "false");
                });
                current = _store.Read();
            }
            Language = TranslationService.Resolve(current.Get("General", "Language", "en"));
            string theme = current.Get("General", "Theme", "System");
            Theme = theme == "Light" || theme == "Dark" ? theme : "System";
            QuotePaths = current.GetBoolean("Copy", "QuotePaths", true);
            RememberFolders = current.GetBoolean("Folders", "Remember", false);
            try { FixedFolders = FolderPaths.Distinct(new JavaScriptSerializer().Deserialize<string[]>(
                current.Get("Folders", "Fixed", "[]"))); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            { FixedFolders = Array.Empty<string>(); }
            string hash = current.Get("Hash", "Algorithm", "SHA-256");
            HashAlgorithm = Array.IndexOf(Features.FileHash.FileHasher.Algorithms, hash) >= 0 ? hash : "SHA-256";
            int.TryParse(current.Get("Hash", "Parallelism", FileHasher.DefaultParallelism.ToString(CultureInfo.InvariantCulture)), out int parallelism);
            HashParallelism = Array.IndexOf(FileHasher.ParallelOptions, parallelism) >= 0 ? parallelism : FileHasher.DefaultParallelism;
            bool validDepth = int.TryParse(current.Get("Hash", "FolderDepth",
                FileHasher.DefaultFolderDepth.ToString(CultureInfo.InvariantCulture)), out int folderDepth);
            HashFolderDepth = validDepth && Array.IndexOf(FileHasher.FolderDepthOptions, folderDepth) >= 0
                ? folderDepth : FileHasher.DefaultFolderDepth;
            TranslationService.UseLanguage(Language);
        }

        /// <summary>
        /// Saves a complete preferences snapshot before notifying all open interfaces
        /// </summary>
        /// <param name="options">Validated UI or command-line preferences</param>
        public void Save(AppOptions options) => SaveAsync(options, false).GetAwaiter().GetResult();

        /// <summary>Persists preferences off the UI thread before publishing committed values on the caller's context</summary>
        /// <param name="options">Validated preference snapshot</param><param name="background">Use background persistence for interactive callers</param>
        /// <returns>Completion after persistence and change notifications</returns>
        internal async Task SaveAsync(AppOptions options, bool background = true)
        {
            string[] folders = FolderPaths.Distinct(options.FixedFolders, true);
            string canonical = TranslationService.Resolve(options.Language);
            if (!string.Equals(canonical, options.Language, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unsupported language", "options");
            await UpdateAsync(delegate (IniDocument document)
            {
                document.Set("General", "Language", canonical);
                document.Set("Copy", "QuotePaths", options.QuotePaths ? "true" : "false");
                document.Set("Folders", "Remember", options.RememberFolders ? "true" : "false");
                document.Set("Folders", "Fixed", new JavaScriptSerializer().Serialize(folders));
            }, background);
            RememberFolders = options.RememberFolders;
            FixedFolders = folders;
            Language = canonical;
            QuotePaths = options.QuotePaths;
            TranslationService.UseLanguage(canonical);
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        /// <summary>Persists the folder discovery limit while preserving independent hash preferences</summary>
        /// <param name="depth">One to three levels, or zero for all levels</param>
        public void SetHashFolderDepth(int depth)
        {
            if (Array.IndexOf(FileHasher.FolderDepthOptions, depth) < 0) throw new ArgumentOutOfRangeException(nameof(depth));
            _store.Update(document => document.Set("Hash", "FolderDepth", depth.ToString(CultureInfo.InvariantCulture)));
            HashFolderDepth = depth;
        }

        /// <summary>Persists hash preferences without overwriting other windows' settings</summary>
        /// <param name="algorithm">Supported hash algorithm</param>
        /// <param name="parallelism">Maximum concurrent readers</param>
        public void SetHashOptions(string algorithm, int parallelism)
        {
            if (Array.IndexOf(Features.FileHash.FileHasher.Algorithms, algorithm) < 0 ||
                (Array.IndexOf(FileHasher.ParallelOptions, parallelism) < 0)) throw new ArgumentException("Invalid hash options");
            _store.Update(document =>
            {
                document.Set("Hash", "Algorithm", algorithm);
                document.Set("Hash", "Parallelism", parallelism.ToString(CultureInfo.InvariantCulture));
            });
            HashAlgorithm = algorithm;
            HashParallelism = parallelism;
        }

        /// <summary>
        /// Updates only the selected language while retaining the current output format
        /// </summary>
        /// <param name="language">Supported locale code</param>
        public void SetLanguage(string language) => SetLanguageAsync(language, false).GetAwaiter().GetResult();

        /// <summary>Persists language without blocking UI input on the cross-process configuration lock</summary>
        /// <param name="language">Supported locale code</param><param name="background">Whether to persist on a pool thread</param>
        /// <returns>Completion after language publication</returns>
        internal async Task SetLanguageAsync(string language, bool background = true)
        {
            string selected = TranslationService.Resolve(language);
            if (!string.Equals(selected, language, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unsupported language", "language");
            await UpdateAsync(doc => doc.Set("General", "Language", selected), background);
            Language = selected;
            TranslationService.UseLanguage(selected);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Updates only the output format while retaining the selected language
        /// </summary>
        /// <param name="quotePaths">Whether paths are wrapped in double quotes</param>
        public void SetQuotePaths(bool quotePaths) => SetQuotePathsAsync(quotePaths, false).GetAwaiter().GetResult();

        /// <summary>Persists path formatting before updating UI-bound preferences</summary>
        /// <param name="quotePaths">Whether paths use quotes</param><param name="background">Whether to persist on a pool thread</param>
        /// <returns>Completion after preference publication</returns>
        internal async Task SetQuotePathsAsync(bool quotePaths, bool background = true)
        {
            await UpdateAsync(doc => doc.Set("Copy", "QuotePaths", quotePaths ? "true" : "false"), background);
            QuotePaths = quotePaths;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Persists the selected appearance without changing other preferences</summary>
        /// <param name="theme">System, Light or Dark</param>
        public void SetTheme(string theme) => SetThemeAsync(theme, false).GetAwaiter().GetResult();

        /// <summary>Persists theme selection before publishing it on the caller's context</summary>
        /// <param name="theme">System, Light or Dark</param><param name="background">Whether to persist on a pool thread</param>
        /// <returns>Completion after theme publication</returns>
        internal async Task SetThemeAsync(string theme, bool background = true)
        {
            if (theme != "System" && theme != "Light" && theme != "Dark") throw new ArgumentException("Invalid theme");
            await UpdateAsync(doc => doc.Set("General", "Theme", theme), background);
            Theme = theme;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Keeps synchronous maintenance callers while moving interactive disk and mutex waits off-thread</summary>
        /// <param name="update">INI mutation</param><param name="background">Whether the caller owns a live UI</param>
        /// <returns>Completion of the atomic store update</returns>
        private Task UpdateAsync(Action<IniDocument> update, bool background)
        {
            if (background) return Task.Run(() => _store.Update(update));
            _store.Update(update);
            return Task.CompletedTask;
        }
    }
}
