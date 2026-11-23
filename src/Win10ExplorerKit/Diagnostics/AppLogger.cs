using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Win10ExplorerKit.Diagnostics
{
    internal static class AppLogger
    {
        // Daily log directory beside the executable
        public static readonly string LogDirectory = Path.Combine(AppPaths.DataDirectory, "logs");
        // Drop excess diagnostics rather than retaining unbounded data behind a stalled disk
        private const int MaximumPendingEntries = 256;
        private static readonly object PendingLock = new object();
        private static readonly Queue<(DateTime Time, string Text)> Pending = new Queue<(DateTime, string)>();
        private static Task _writer = Task.CompletedTask;
        private static bool _writing;

        /// <summary>
        /// Appends diagnostics without interrupting clipboard or hotkey handling
        /// </summary>
        /// <param name="message">Event description without copied path contents</param>
        /// <param name="exception">Optional exception whose type and error code are safe to record</param>
        public static void Write(string message, Exception exception)
        {
            try
            {
                DateTime now = DateTime.Now;
                string text = FormatEntry(now, message, exception);
                lock (PendingLock)
                {
                    if (Pending.Count >= MaximumPendingEntries) return;
                    Pending.Enqueue((now, text));
                    if (!_writing) { _writing = true; _writer = Task.Run(Drain); }
                }
            }
            catch { }
        }

        /// <summary>Writes queued records serially without performing disk I/O on event handlers</summary>
        private static void Drain()
        {
            while (true)
            {
                (DateTime Time, string Text) entry;
                lock (PendingLock)
                {
                    if (Pending.Count == 0) { _writing = false; return; }
                    entry = Pending.Dequeue();
                }
                Append(LogDirectory, entry.Time, entry.Text);
            }
        }

        /// <summary>Exposes current writer completion for bounded shutdown flushing</summary>
        /// <returns>Current write task</returns>
        internal static Task FlushAsync() { lock (PendingLock) return _writer; }

        /// <summary>Appends the event to its local calendar day's log</summary>
        /// <param name="directory">Log folder</param>
        /// <param name="now">Local event time</param>
        /// <param name="message">Diagnostic event</param>
        /// <param name="exception">Optional sanitized exception</param>
        internal static void Write(string directory, DateTime now, string message, Exception exception)
            => Append(directory, now, FormatEntry(now, message, exception));

        /// <summary>Formats safe metadata before it enters the bounded log queue</summary>
        /// <param name="now">Event timestamp</param><param name="message">Event name</param><param name="exception">Optional sanitized exception</param>
        /// <returns>One complete log record</returns>
        private static string FormatEntry(DateTime now, string message, Exception exception)
        {
            using (var process = Process.GetCurrentProcess())
                return now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [" + process.Id + "] " + message +
                    (exception == null ? "" : Environment.NewLine + DescribeException(exception)) + Environment.NewLine;
        }

        /// <summary>Appends a preformatted record to its original local date</summary>
        /// <param name="directory">Log directory</param><param name="now">Original event timestamp</param><param name="text">Sanitized log text</param>
        private static void Append(string directory, DateTime now, string text)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(path, text, Encoding.UTF8);
            }
            catch
            {
                // Logging must not interrupt clipboard or hotkey handling
            }
        }

        /// <summary>
        /// Keeps diagnostic identifiers and method names without exception messages, paths or custom data
        /// </summary>
        /// <param name="exception">Failure whose payload must remain private</param>
        /// <returns>Exception types, hexadecimal error codes and bounded method-only call sites</returns>
        internal static string DescribeException(Exception exception)
        {
            var description = new StringBuilder();
            // Bound the cause chain and record identifiers only, never messages or file paths
            for (int depth = 0; exception != null && depth < 8; depth++, exception = exception.InnerException)
            {
                if (depth > 0) description.Append(" -> ");
                description.Append(exception.GetType().FullName).Append(" (HRESULT 0x")
                    .Append(exception.HResult.ToString("X8")).Append(')');
                // Method metadata locates UI failures without exposing source paths or argument values
                StackFrame[] frames = new StackTrace(exception, false).GetFrames();
                if (frames == null) continue;
                for (int index = 0; index < Math.Min(frames.Length, 16); index++)
                {
                    var method = frames[index].GetMethod();
                    if (method == null) continue;
                    description.Append(Environment.NewLine).Append("  at ")
                        .Append(method.DeclaringType?.FullName).Append('.').Append(method.Name);
                }
            }
            return description.ToString();
        }
    }
}
