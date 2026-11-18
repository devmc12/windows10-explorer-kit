using System;
using System.Collections.Generic;
using System.IO;

namespace Win10ExplorerKit.Features.FolderRecovery
{
    internal static class FolderPaths
    {
        /// <summary>Validates absolute drive and UNC paths without contacting the filesystem</summary>
        /// <param name="value">Folder path from settings or Explorer</param>
        /// <returns>Canonical filesystem path or null for unsupported input</returns>
        internal static string Normalize(string value)
        {
            string path = (value ?? "").Trim().Trim('"').Replace('/', '\\');
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.IndexOfAny(new[] { '*', '?' }) >= 0 ||
                path.StartsWith(@"\\.\", StringComparison.Ordinal)) return null;
            bool drive = path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\';
            if (!drive && (!path.StartsWith(@"\\") || path.Trim('\\').Split('\\').Length < 2)) return null;
            if (path.IndexOf(':', drive ? 2 : 0) >= 0) return null;
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetPathRoot(full);
                string trimmed = full.TrimEnd('\\');
                return trimmed.Length == root.TrimEnd('\\').Length ? trimmed + "\\" : trimmed;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException)
            { return null; }
        }

        /// <summary>Deduplicates folders in input order, optionally rejecting invalid settings</summary>
        /// <param name="paths">Candidate paths</param>
        /// <param name="strict">Whether to reject a nonempty invalid entry</param>
        /// <returns>Distinct absolute paths</returns>
        internal static string[] Distinct(IEnumerable<string> paths, bool strict = false)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (string raw in paths ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string path = Normalize(raw);
                if (path == null && strict) throw new FormatException("Invalid folder path");
                if (path != null && seen.Add(path)) result.Add(path);
            }
            return result.ToArray();
        }
    }
}
