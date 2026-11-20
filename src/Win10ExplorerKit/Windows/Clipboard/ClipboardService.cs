using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Win10ExplorerKit.Windows.Clipboard
{
    internal static class ClipboardService
    {
        /// <summary>
        /// Writes Unicode text with bounded retries for temporary clipboard contention
        /// </summary>
        /// <param name="text">Text to place on the clipboard</param>
        /// <returns>True if the clipboard accepted the text</returns>
        public static bool SetText(string text)
        {
            // Clipboard ownership is transient; retry briefly without blocking indefinitely
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return true;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(40);
                }
                catch (InvalidOperationException)
                {
                    Thread.Sleep(40);
                }
            }

            return false;
        }
    }

}
