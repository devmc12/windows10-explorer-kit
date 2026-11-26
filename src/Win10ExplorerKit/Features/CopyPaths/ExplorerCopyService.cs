// Copies Explorer selections on a reusable background STA with bounded UI waits
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Win10ExplorerKit.Features.CopyPaths
{
    internal sealed class ExplorerCopyService : IDisposable
    {
        // Maximum selection wait in milliseconds before abandoning a shortcut request
        private const int CopyTimeoutMilliseconds = 3000;
        private readonly Action<string> _notify;
        private Task<string> _pending = Task.FromResult<string>(null);
        private BackgroundSta _worker;
        private CancellationTokenSource _activeRequest;
        private bool _disposed;
        internal Task Completion => Task.WhenAll(_pending, _worker?.Completion ?? Task.CompletedTask);

        /// <summary>Owns one outstanding STA copy operation without blocking the UI dispatcher</summary>
        /// <param name="notify">Feedback callback on the UI dispatcher</param>
        internal ExplorerCopyService(Action<string> notify) => _notify = notify;

        /// <summary>Reads and copies on a background STA, ignoring repeated shortcuts while it is occupied</summary>
        /// <param name="window">Explorer handle captured at shortcut dispatch</param>
        /// <param name="plain">Whether to omit surrounding quotes</param>
        /// <returns>Completion of the UI wait, which may precede a stalled COM call returning</returns>
        internal async Task CopyAsync(IntPtr window, bool plain)
        {
            if (_disposed) return;
            if (!_pending.IsCompleted)
            {
                _notify(TranslationService.T("error.copyPending"));
                return;
            }
            using (var request = new CancellationTokenSource(CopyTimeoutMilliseconds))
            {
                _activeRequest = request;
                CancellationToken token = request.Token;
                try
                {
                    // Keep a message-pumping apartment alive and balance WPF OLE initialization at shutdown
                    if (_worker == null) _worker = new BackgroundSta("Explorer path copy");
                    _pending = _worker.InvokeAsync(() => ReadAndCopy(window, plain, token), token);
                    Task finished = await Task.WhenAny(_pending, Task.Delay(Timeout.Infinite, token));
                    if (_disposed) return;
                    if (finished != _pending)
                    {
                        // Keep the occupied task until COM returns instead of spawning more blocked threads
                        AppLogger.Write("Explorer path copy timed out", null);
                        _ = ObserveExpiredAsync(_pending);
                        _notify(TranslationService.T("error.copyTimeout"));
                    }
                    else
                    {
                        string error = await _pending;
                        if (error != null) _notify(TranslationService.T(error));
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!_disposed)
                    {
                        AppLogger.Write("Explorer path copy timed out", null);
                        _notify(TranslationService.T("error.copyTimeout"));
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Write("Explorer path copy dispatch failed", ex);
                    if (!_disposed) _notify(TranslationService.T("error.copyPaths"));
                }
                finally
                {
                    request.Cancel();
                    if (ReferenceEquals(_activeRequest, request)) _activeRequest = null;
                }
            }
        }

        /// <summary>Completes shell access and clipboard ownership entirely on the worker STA</summary>
        /// <param name="window">Original Explorer window</param>
        /// <param name="plain">Whether copied paths omit quotes</param>
        /// <param name="token">Cancellation checked before writing stale results</param>
        /// <returns>Localized error key, or null when no feedback is needed</returns>
        private static string ReadAndCopy(IntPtr window, bool plain, CancellationToken token)
        {
            string stage = "Explorer selection read failed";
            try
            {
                var paths = ExplorerSelection.GetSelectedPaths(window, token);
                string text = PathFormatter.Format(paths, plain);
                token.ThrowIfCancellationRequested();
                // An empty selection is a silent no-op; switching apps does not change the captured selection
                if (text.Length == 0) return null;
                stage = "Explorer path clipboard write failed";
                if (ClipboardService.SetText(text, token)) return null;
                return "error.copyClipboard";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLogger.Write(stage, ex);
                return stage == "Explorer selection read failed" ? "error.copyPaths" : "error.copyClipboard";
            }
        }

        /// <summary>Observes late worker completion after the interactive timeout</summary>
        /// <param name="pending">The original operation still occupying the STA</param>
        /// <returns>Completion after the pending operation unwinds</returns>
        private static async Task ObserveExpiredAsync(Task pending)
        {
            try { await pending.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Write("Expired path copy failed", ex); }
        }

        /// <summary>Discards pending results without waiting for an unresponsive Explorer process</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activeRequest?.Cancel();
            _worker?.Dispose();
        }
    }
}
