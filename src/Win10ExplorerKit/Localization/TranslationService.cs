using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace Win10ExplorerKit.Localization
{
    // 1. Scope: locale discovery, translation lookup and English fallback
    // 2. Flow: load embedded defaults, overlay external JSON, select the configured locale
    // 3. Rules: UI code uses stable keys; missing or invalid translations fall back safely
    // 4. Usage: TranslationService.T("tray.settings") or TranslationService.T("error.setup", details)
    internal static class TranslationService
    {
        // English catalog used when a locale or translation key is unavailable
        public const string DefaultLanguage = "en";
        // Catalogs loaded once per process; external files can override embedded defaults
        private static readonly Dictionary<string, Dictionary<string, string>> Catalogs =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        // Selected language code; display names live in their respective catalogs
        private static string _language = DefaultLanguage;

        /// <summary>
        /// Loads bundled catalogs and optional files from the executable's locales directory
        /// </summary>
        static TranslationService()
        {
            Assembly assembly = typeof(TranslationService).Assembly;
            foreach (string code in new[] { "en", "zh-CN" })
            {
                using (Stream stream = assembly.GetManifestResourceStream("locales." + code + ".json"))
                {
                    if (stream == null) throw new InvalidOperationException("Missing embedded locale: " + code);
                    using (var reader = new StreamReader(stream))
                        Catalogs[code] = Parse(reader.ReadToEnd());
                }
            }
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales");
            if (!Directory.Exists(folder)) return;
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    CultureInfo.GetCultureInfo(code);
                    var values = Parse(File.ReadAllText(file));
                    if (!Catalogs.ContainsKey(code)) Catalogs[code] = new Dictionary<string, string>();
                    foreach (var pair in values) Catalogs[code][pair.Key] = pair.Value;
                }
                catch (Exception ex)
                {
                    // A damaged optional locale must not prevent hotkey or clipboard startup
                    AppLogger.Write("Ignored invalid locale file", ex);
                }
            }
        }

        /// <summary>
        /// Validates a flat JSON translation catalog before it becomes available to the UI
        /// </summary>
        /// <param name="json">UTF-8 JSON content containing string keys and values</param>
        /// <returns>A validated translation dictionary</returns>
        internal static Dictionary<string, string> Parse(string json)
        {
            var values = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(json);
            if (values == null) throw new FormatException("Locale must be a JSON object");
            foreach (var pair in values)
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    throw new FormatException("Locale keys and values must not be empty");
            return values;
        }

        /// <summary>
        /// Returns available locale codes in a stable order for the language menu
        /// </summary>
        public static IEnumerable<string> AvailableLanguages
        {
            get { var codes = new List<string>(Catalogs.Keys); codes.Sort(StringComparer.OrdinalIgnoreCase); return codes; }
        }

        /// <summary>
        /// Resolves exact, Chinese-family and neutral culture matches before English fallback
        /// </summary>
        /// <param name="code">Saved language code or Windows UI culture name</param>
        /// <returns>An available canonical language code</returns>
        public static string Resolve(string code)
        {
            foreach (string candidate in Catalogs.Keys)
                if (string.Equals(candidate, code, StringComparison.OrdinalIgnoreCase)) return candidate;
            if (string.IsNullOrWhiteSpace(code)) return DefaultLanguage;
            if (code.Equals("zh", StringComparison.OrdinalIgnoreCase) || code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
                return "zh-CN";
            int separator = code.IndexOf('-');
            return separator > 0 ? Resolve(code.Substring(0, separator)) : DefaultLanguage;
        }

        /// <summary>
        /// Selects a supported language without writing configuration or changing shell state
        /// </summary>
        /// <param name="code">Requested locale code</param>
        public static void UseLanguage(string code) { _language = Resolve(code); }

        /// <summary>
        /// Returns a locale's self-described language name for the selection menu
        /// </summary>
        /// <param name="code">Available locale code</param>
        /// <returns>The native display name or the locale code</returns>
        public static string LanguageName(string code)
        {
            string name;
            return Catalogs[code].TryGetValue("language.name", out name) ? name : code;
        }

        /// <summary>
        /// Translates a stable key and formats optional arguments with English fallback
        /// </summary>
        /// <param name="key">Stable translation key shared by all catalogs</param>
        /// <param name="arguments">Optional positional values for composite formatting</param>
        /// <returns>Translated text, English fallback, or the visibly bracketed missing key</returns>
        public static string T(string key, params object[] arguments)
        {
            string fallback;
            if (!Catalogs[DefaultLanguage].TryGetValue(key, out fallback)) fallback = "[" + key + "]";
            string value;
            if (!Catalogs[_language].TryGetValue(key, out value)) value = fallback;
            if (arguments.Length == 0) return value;
            try { return string.Format(CultureInfo.CurrentCulture, value, arguments); }
            catch (FormatException)
            {
                try { return string.Format(CultureInfo.CurrentCulture, fallback, arguments); }
                catch (FormatException) { return "[" + key + "]"; }
            }
        }
    }
}
