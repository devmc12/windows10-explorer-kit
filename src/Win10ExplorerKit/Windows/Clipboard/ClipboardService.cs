using System;
using System.Runtime.InteropServices;
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
            // WPF already retries OLE clipboard contention internally
            // An outer retry loop multiplies those waits and can stall the caller for seconds
            try
            {
                System.Windows.Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException) { }
            catch (InvalidOperationException) { }
            return false;
        }
    }

}
