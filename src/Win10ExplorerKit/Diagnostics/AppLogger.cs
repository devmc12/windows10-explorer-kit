using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Win10ExplorerKit.Diagnostics
{
    internal static class AppLogger
    {
        // Per-user diagnostic log location; copied text is not written deliberately
        public static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "logs", "application.log");

        /// <summary>
        /// Appends diagnostics without interrupting clipboard or hotkey handling
        /// </summary>
        /// <param name="message">Event description without copied path contents</param>
        /// <param name="exception">Optional exception whose type and error code are safe to record</param>
        public static void Write(string message, Exception exception)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.AppendAllText(FilePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" +
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
