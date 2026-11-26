// Publishes complete Unicode text without OLE delayed rendering
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Win10ExplorerKit.Windows.Clipboard
{
    internal static class ClipboardService
    {
        // CF_UNICODETEXT stores a complete null-terminated UTF-16 string
        private const uint UnicodeText = 13;
        // Clipboard ownership requires movable global memory
        private const uint MovableMemory = 0x0002;
        // Maximum clipboard acquisition wait in milliseconds on the caller's worker thread
        private const int OpenTimeoutMilliseconds = 1000;
        // Delay in milliseconds between attempts to acquire a busy clipboard
        private const int RetryDelayMilliseconds = 50;
        // HWND_MESSAGE creates an invisible owner without a taskbar entry
        private static readonly IntPtr MessageOnlyWindow = new IntPtr(-3);

        /// <summary>Writes persistent Unicode text without OLE delayed rendering or a second flush operation</summary>
        /// <param name="text">Text to place on the clipboard</param>
        /// <returns>True after ownership of the complete text transfers to Windows</returns>
        public static bool SetText(string text) => SetText(text, CancellationToken.None);

        /// <summary>Acquires the clipboard with bounded retries and publishes only a still-current request</summary>
        /// <param name="text">Text to place on the clipboard</param>
        /// <param name="token">Cancellation checked before replacing clipboard contents</param>
        /// <returns>True after ownership of the complete text transfers to Windows</returns>
        internal static bool SetText(string text, CancellationToken token)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            token.ThrowIfCancellationRequested();
            IntPtr memory = IntPtr.Zero;
            var owner = new NativeWindow();
            bool opened = false;
            try
            {
                // Prepare the data before taking the clipboard lock or removing previous contents
                byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
                memory = ClipboardNative.GlobalAlloc(MovableMemory, new UIntPtr((uint)bytes.Length));
                if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr address = ClipboardNative.GlobalLock(memory);
                if (address == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try { Marshal.Copy(bytes, 0, address, bytes.Length); }
                finally { ClipboardNative.GlobalUnlock(memory); }

                owner.CreateHandle(new CreateParams { Parent = MessageOnlyWindow });
                var elapsed = Stopwatch.StartNew();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (ClipboardNative.OpenClipboard(owner.Handle)) { opened = true; break; }
                    int error = Marshal.GetLastWin32Error();
                    if (elapsed.ElapsedMilliseconds >= OpenTimeoutMilliseconds) throw new Win32Exception(error);
                    Thread.Sleep(RetryDelayMilliseconds);
                }
                token.ThrowIfCancellationRequested();
                if (!ClipboardNative.EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (ClipboardNative.SetClipboardData(UnicodeText, memory) == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                // Windows now owns this allocation and retains it after the worker or process exits
                memory = IntPtr.Zero;
                return true;
            }
            catch (Win32Exception ex)
            {
                AppLogger.Write("Clipboard write failed, Win32 code " + ex.NativeErrorCode, ex);
                return false;
            }
            catch (ExternalException ex) { AppLogger.Write("Clipboard write failed", ex); return false; }
            catch (InvalidOperationException ex) { AppLogger.Write("Clipboard write failed", ex); return false; }
            finally
            {
                // Never free a block already transferred to Windows or leave the clipboard locked
                if (opened && !ClipboardNative.CloseClipboard())
                    AppLogger.Write("Clipboard close failed, Win32 code " + Marshal.GetLastWin32Error(), null);
                if (memory != IntPtr.Zero) ClipboardNative.GlobalFree(memory);
                owner.DestroyHandle();
            }
        }
    }

}
