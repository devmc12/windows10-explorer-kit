using System;
using System.Collections.Generic;
using System.IO;

namespace Win10ExplorerKit.Features.CopyPaths
{
    internal static class PathFormatter
    {
        /// <summary>
        /// Normalizes paths and preserves first occurrence order
        /// </summary>
        /// <param name="inputPaths">Raw shell or command-line paths</param>
        /// <returns>Case-insensitively unique normalized paths</returns>
        public static List<string> NormalizeAndDeduplicate(IEnumerable<string> inputPaths)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in inputPaths)
            {
                if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "%*", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string path = raw.Trim();
                try
                {
                    path = Path.GetFullPath(path);
                }
                catch (ArgumentException)
                {
                    // Keep shell supplied virtual paths if Path.GetFullPath cannot parse them
                }
                catch (NotSupportedException)
                {
                    // Keep shell supplied virtual paths if Path.GetFullPath cannot parse them
                }

                if (seen.Add(path))
                {
                    result.Add(path);
                }
            }

            return result;
        }

        /// <summary>
        /// Joins unique normalized paths using the requested quoting mode
        /// </summary>
        /// <param name="paths">Filesystem paths to format</param>
        /// <param name="plain">Whether surrounding quotes are omitted</param>
        /// <returns>CRLF-delimited Unicode text</returns>
        public static string Format(IEnumerable<string> paths, bool plain)
        {
            var lines = new List<string>();
            foreach (string path in NormalizeAndDeduplicate(paths))
                lines.Add(plain ? path : "\"" + path + "\"");
            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }

}
