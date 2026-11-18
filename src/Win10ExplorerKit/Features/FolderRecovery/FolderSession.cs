using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Win10ExplorerKit.Features.FolderRecovery
{
    internal sealed class FolderSession
    {
        // Owned data file beside the executable, also recognized during uninstall
        internal const string FileName = "FolderSession.json";
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private string[] _current = Array.Empty<string>();
        private string[] _previous = Array.Empty<string>();

        internal string[] RestorePaths => _previous.Length > 0 ? _previous : _current;

        /// <summary>Loads the last checkpoint before a new Explorer session can replace it</summary>
        /// <param name="directory">Application directory</param>
        internal FolderSession(string directory)
        {
            _path = Path.Combine(directory, FileName);
            if (!File.Exists(_path)) return;
            try
            {
                var stored = _json.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(_path));
                if (stored == null) return;
                stored.TryGetValue("current", out var current);
                stored.TryGetValue("previous", out var previous);
                _current = FolderPaths.Distinct(current);
                _previous = FolderPaths.Distinct(previous);
                if (_previous.Length == 0) Preserve();
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException)
            { AppLogger.Write("Session read failed", ex); }
        }

        /// <summary>Pins the last complete group through an empty desktop or shell restart</summary>
        internal void Preserve()
        {
            if (_current.Length == 0) return;
            // When an older offer is on disk, retire it before pinning the most recent group
            if (_previous.Length > 0 && !new HashSet<string>(_current, StringComparer.OrdinalIgnoreCase).SetEquals(_previous))
            {
                try { Save(_current, Array.Empty<string>()); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                { AppLogger.Write("Recovery checkpoint failed", ex); }
            }
            _previous = _current;
        }

        /// <summary>Commits a changed, complete set using atomic replacement</summary>
        /// <param name="paths">Current Explorer filesystem folders</param>
        internal void Observe(IEnumerable<string> paths)
        {
            string[] next = FolderPaths.Distinct(paths);
            if (next.Length == 0) { Preserve(); return; }
            var open = new HashSet<string>(next, StringComparer.OrdinalIgnoreCase);
            bool recovered = _previous.Length > 0 && open.IsSupersetOf(_previous);
            if (open.SetEquals(_current) && !recovered) return;
            Save(next, recovered ? Array.Empty<string>() : _previous);
        }

        /// <summary>Publishes a complete file before exposing its state to tray commands</summary>
        /// <param name="current">Latest observed group</param>
        /// <param name="previous">Recovery group protected from new windows</param>
        private void Save(string[] current, string[] previous)
        {
            string temp = _path + ".tmp";
            try
            {
                File.Delete(temp);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(_json.Serialize(new { current, previous }));
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
                _current = current;
                _previous = previous;
            }
            finally { File.Delete(temp); }
        }
    }
}
