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
        private Task<bool> _pending = Task.FromResult(true);
        private CancellationTokenSource _activeRequest;
        private bool _disposed;
        internal Task Completion => _pending;

        /// <summary>Owns one outstanding STA copy operation without blocking the UI dispatcher</summary>
        /// <param name="notify">Feedback callback on the UI dispatcher</param>
        internal ExplorerCopyService(Action<string> notify) => _notify = notify;

        /// <summary>Reads and copies on a background STA, ignoring repeated shortcuts while it is occupied</summary>
        /// <param name="window">Explorer handle captured at shortcut dispatch</param>
        /// <param name="plain">Whether to omit surrounding quotes</param>
        /// <returns>Completion of the UI wait, which may precede a stalled COM call returning</returns>
        internal async Task CopyAsync(IntPtr window, bool plain)
        {
            if (_disposed || !_pending.IsCompleted) return;
            using (var request = new CancellationTokenSource(CopyTimeoutMilliseconds))
            {
                _activeRequest = request;
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending = completion.Task;
                CancellationToken token = request.Token;
                bool started = false;
                try
                {
                    // Shell RCWs stay on the worker; only completion returns to the UI
                    var worker = new Thread(() => ReadAndCopy(window, plain, token, completion))
                    { IsBackground = true, Name = "Explorer path copy" };
                    worker.SetApartmentState(ApartmentState.STA);
                    worker.Start();
                    started = true;
                    Task finished = await Task.WhenAny(completion.Task, Task.Delay(Timeout.Infinite, token));
                    if (_disposed) return;
                    if (finished != completion.Task)
                    {
                        // Keep the occupied task until COM returns instead of spawning more blocked threads
                        AppLogger.Write("Explorer path copy timed out", null);
                        _notify(TranslationService.T("error.copyPaths"));
                    }
                    else if (!await completion.Task) _notify(TranslationService.T("error.copyPaths"));
                }
                catch (Exception ex)
                {
                    if (!started) completion.TrySetResult(false);
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
        /// <param name="completion">Plain result returned to the UI dispatcher</param>
        private static void ReadAndCopy(IntPtr window, bool plain, CancellationToken token, TaskCompletionSource<bool> completion)
        {
            bool copied = false;
            try
            {
                var paths = ExplorerSelection.GetSelectedPaths(window, token);
                string text = PathFormatter.Format(paths, plain);
                token.ThrowIfCancellationRequested();
                // An empty selection is a silent no-op; switching apps does not change the captured selection
                copied = text.Length == 0 || ClipboardService.SetText(text);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Write("Explorer path copy failed", ex); }
            finally { completion.TrySetResult(copied); }
        }

        /// <summary>Discards pending results without waiting for an unresponsive Explorer process</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activeRequest?.Cancel();
        }
    }
}
