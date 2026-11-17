using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

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
        // Per-user configuration survives application directory moves and binary updates
        public static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "Config.ini");
        // Active store can point to an isolated file during configuration tests
        private readonly ConfigStore _store;
        // Canonical locale code resolved from saved settings or first-run system language
        public string Language { get; private set; }
        // Persistent output format shared by hotkey and shell commands
        public bool QuotePaths { get; private set; }
        public event EventHandler Changed;

        /// <summary>
        /// Loads per-user INI settings and migrates legacy language and output preferences
        /// </summary>
        /// <returns>The settings service for the current Windows user</returns>
        public static SettingsService Load()
        {
            string legacyLanguage = null, legacyStartup = null;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LegacyRegistryPath))
                if (key != null) legacyLanguage = key.GetValue("Language") as string;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                if (key != null) legacyStartup = key.GetValue("Win10ExplorerKitHotkey") as string;
            var settings = new SettingsService(FilePath, CultureInfo.CurrentUICulture.Name, legacyLanguage,
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
            QuotePaths = current.GetBoolean("Copy", "QuotePaths", true);
            TranslationService.UseLanguage(Language);
        }

        /// <summary>
        /// Saves a complete preferences snapshot before notifying all open interfaces
        /// </summary>
        /// <param name="options">Validated UI or command-line preferences</param>
        public void Save(AppOptions options)
        {
            string canonical = TranslationService.Resolve(options.Language);
            if (!string.Equals(canonical, options.Language, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unsupported language", "options");
            _store.Update(delegate (IniDocument document)
            {
                document.Set("General", "Language", canonical);
                document.Set("Copy", "QuotePaths", options.QuotePaths ? "true" : "false");
            });
            Language = canonical;
            QuotePaths = options.QuotePaths;
            TranslationService.UseLanguage(canonical);
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        /// <summary>
        /// Updates only the selected language while retaining the current output format
        /// </summary>
        /// <param name="language">Supported locale code</param>
        public void SetLanguage(string language) { Save(new AppOptions { Language = language, QuotePaths = QuotePaths }); }

        /// <summary>
        /// Updates only the output format while retaining the selected language
        /// </summary>
        /// <param name="quotePaths">Whether paths are wrapped in double quotes</param>
        public void SetQuotePaths(bool quotePaths) { Save(new AppOptions { Language = Language, QuotePaths = quotePaths }); }
    }
}
