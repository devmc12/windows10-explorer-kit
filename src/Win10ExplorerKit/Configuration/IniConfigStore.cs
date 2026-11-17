using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Win10ExplorerKit.Configuration
{
    // 1. Scope: extensible INI storage independent of application UI or registry APIs
    // 2. Flow: reload under a process-shared lock, update requested keys, replace atomically
    // 3. Rules: preserve comments and unknown sections; never persist a stale full snapshot
    // 4. Usage: store.Update(document => document.Set("Section", "Key", "value"))
    internal sealed class IniDocument
    {
        // Original lines preserve comments and future settings during targeted updates
        private readonly List<string> _lines;

        /// <summary>
        /// Keeps an editable representation of an INI document including unknown content
        /// </summary>
        /// <param name="lines">Existing file lines or an empty sequence for a new file</param>
        public IniDocument(IEnumerable<string> lines) { _lines = new List<string>(lines); }

        /// <summary>
        /// Reads the last occurrence of a key using case-insensitive section and key names
        /// </summary>
        /// <param name="section">Section name without brackets</param>
        /// <param name="key">Setting name</param>
        /// <param name="fallback">Value returned when no matching key exists</param>
        /// <returns>The stored value or fallback</returns>
        public string Get(string section, string key, string fallback)
        {
            string current = "", result = fallback;
            foreach (string raw in _lines)
            {
                string line = raw.Trim();
                if (line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { current = line.Substring(1, line.Length - 2).Trim(); continue; }
                int separator = line.IndexOf('=');
                if (separator >= 0 && string.Equals(current, section, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(line.Substring(0, separator).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    result = line.Substring(separator + 1).Trim();
            }
            return result;
        }

        /// <summary>
        /// Reads a Boolean value while treating invalid user input as the supplied default
        /// </summary>
        /// <param name="section">Section name</param>
        /// <param name="key">Setting name</param>
        /// <param name="fallback">Default for missing or invalid values</param>
        /// <returns>The parsed Boolean or default</returns>
        public bool GetBoolean(string section, string key, bool fallback)
        {
            bool value;
            return bool.TryParse(Get(section, key, null), out value) ? value : fallback;
        }

        /// <summary>
        /// Updates a key without discarding unrelated settings, sections or comments
        /// </summary>
        /// <param name="section">Section name without control characters</param>
        /// <param name="key">Key name without separators</param>
        /// <param name="value">Single-line string value</param>
        public void Set(string section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key) || value == null ||
                section.IndexOfAny(new[] { '[', ']', '\r', '\n' }) >= 0 ||
                key.IndexOfAny(new[] { '=', '\r', '\n' }) >= 0 || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("Invalid INI section, key or value");
            string current = "";
            int match = -1, insertion = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                string line = _lines[i].Trim();
                if (line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    if (string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) insertion = i + 1;
                    continue;
                }
                if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
                insertion = i + 1;
                int separator = line.IndexOf('=');
                if (separator >= 0 && string.Equals(line.Substring(0, separator).Trim(), key, StringComparison.OrdinalIgnoreCase)) match = i;
            }
            if (match >= 0) _lines[match] = key + "=" + value;
            else if (insertion >= 0) _lines.Insert(insertion, key + "=" + value);
            else { if (_lines.Count > 0) _lines.Add(""); _lines.Add("[" + section + "]"); _lines.Add(key + "=" + value); }
        }

        /// <summary>
        /// Serializes preserved lines with Windows line endings
        /// </summary>
        /// <returns>Complete INI file content</returns>
        public override string ToString() { return string.Join("\r\n", _lines.ToArray()) + "\r\n"; }
    }

    internal sealed class ConfigStore
    {
        // Maximum wait in milliseconds for another process to complete a settings write
        private const int LockTimeoutMilliseconds = 5000;
        // Absolute file location supplied by the application or isolated tests
        private readonly string _path;
        // Path-derived session mutex serializes concurrent read-modify-write operations
        private readonly string _mutexName;

        /// <summary>
        /// Creates a store whose lock is scoped to its canonical file location
        /// </summary>
        /// <param name="path">Configuration file path</param>
        public ConfigStore(string path)
        {
            _path = Path.GetFullPath(path);
            using (var hash = SHA256.Create())
                _mutexName = @"Local\Win10ExplorerKit.Config." + BitConverter.ToString(
                    hash.ComputeHash(Encoding.UTF8.GetBytes(_path.ToUpperInvariant()))).Replace("-", "");
        }

        /// <summary>
        /// Reads a snapshot while allowing another process to replace the file atomically
        /// </summary>
        /// <returns>The file document, or an empty document if it does not exist</returns>
        public IniDocument Read()
        {
            try
            {
                using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var lines = new List<string>();
                    string line;
                    while ((line = reader.ReadLine()) != null) lines.Add(line);
                    return new IniDocument(lines);
                }
            }
            catch (FileNotFoundException) { return new IniDocument(new string[0]); }
            catch (DirectoryNotFoundException) { return new IniDocument(new string[0]); }
        }

        /// <summary>
        /// Merges an update into the latest document and atomically replaces the file
        /// </summary>
        /// <param name="update">Mutation restricted to the desired settings</param>
        public void Update(Action<IniDocument> update)
        {
            using (var mutex = new Mutex(false, _mutexName))
            {
                bool owns;
                try { owns = mutex.WaitOne(LockTimeoutMilliseconds); }
                catch (AbandonedMutexException) { owns = true; }
                if (!owns) throw new IOException("Timed out waiting for configuration lock");
                string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    IniDocument document = Read();
                    update(document);
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    File.WriteAllText(temporary, document.ToString(), new UTF8Encoding(false));
                    if (File.Exists(_path)) File.Replace(temporary, _path, null);
                    else File.Move(temporary, _path);
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    finally { mutex.ReleaseMutex(); }
                }
            }
        }
    }

}
