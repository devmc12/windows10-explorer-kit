using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Win10ExplorerKit.Diagnostics
{
    internal static class AppLogger
    {
        // Daily log directory beside the executable
        public static readonly string LogDirectory = Path.Combine(AppPaths.DataDirectory, "logs");

        /// <summary>
        /// Appends diagnostics without interrupting clipboard or hotkey handling
        /// </summary>
        /// <param name="message">Event description without copied path contents</param>
        /// <param name="exception">Optional exception whose type and error code are safe to record</param>
        public static void Write(string message, Exception exception)
        {
            Write(LogDirectory, DateTime.Now, message, exception);
        }

        /// <summary>Appends the event to its local calendar day's log</summary>
        /// <param name="directory">Log folder</param>
        /// <param name="now">Local event time</param>
        /// <param name="message">Diagnostic event</param>
        /// <param name="exception">Optional sanitized exception</param>
        internal static void Write(string directory, DateTime now, string message, Exception exception)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(path,
                    now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [" +
                    Process.GetCurrentProcess().Id + "] " + message +
                    (exception == null ? "" : Environment.NewLine + DescribeException(exception)) +
                    Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
                // Logging must not interrupt clipboard or hotkey handling
            }
        }

        /// <summary>
        /// Keeps diagnostic identifiers without exception messages, paths, stack traces or custom data
        /// </summary>
        /// <param name="exception">Failure whose payload must remain private</param>
        /// <returns>The exception type and hexadecimal error code</returns>
        internal static string DescribeException(Exception exception)
        {
            return exception.GetType().FullName + " (HRESULT 0x" + exception.HResult.ToString("X8") + ")";
        }
    }
}
