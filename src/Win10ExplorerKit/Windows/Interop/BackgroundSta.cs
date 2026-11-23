using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Win10ExplorerKit.Windows.Interop
{
    internal sealed class BackgroundSta : IDisposable
    {
        private readonly TaskCompletionSource<Dispatcher> _ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action _cleanup;
        private int _disposed;
        internal Task Completion => _completed.Task;

        /// <summary>Starts one message-pumping STA for apartment-owned shell resources</summary>
        /// <param name="name">Diagnostic worker name</param><param name="cleanup">Optional cleanup on the owning STA</param>
        internal BackgroundSta(string name, Action cleanup = null)
        {
            _cleanup = cleanup;
            var thread = new Thread(Run) { IsBackground = true, Name = name };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        /// <summary>Creates the dispatcher and releases COM resources only on their owning thread</summary>
        private void Run()
        {
            try
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                _ready.TrySetResult(dispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _disposed, 1);
                _ready.TrySetException(ex);
                AppLogger.Write("Background STA failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _disposed, 1);
                try
                {
                    try { if (!Dispatcher.CurrentDispatcher.HasShutdownFinished) Dispatcher.CurrentDispatcher.InvokeShutdown(); }
                    finally { _cleanup?.Invoke(); }
                }
                catch (Exception ex) { AppLogger.Write("Background STA cleanup failed", ex); }
                finally { _completed.TrySetResult(true); }
            }
        }

        /// <summary>Queues work without making the caller wait for startup or shell COM</summary>
        /// <typeparam name="T">Plain result type</typeparam>
        /// <param name="action">Operation restricted to the worker apartment</param>
        /// <param name="token">Cancels work that has not started</param>
        /// <returns>Completion with a plain result, never an owned COM reference</returns>
        internal async Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token = default)
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(BackgroundSta));
            Dispatcher dispatcher = await _ready.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(BackgroundSta));
            return await dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(BackgroundSta));
                return action();
            }, DispatcherPriority.Background, token).Task.ConfigureAwait(false);
        }

        /// <summary>Requests shutdown without joining a thread that may be waiting for Explorer</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _ = StopAsync();
        }

        /// <summary>Stops the message pump once startup completes</summary>
        /// <returns>Completion of the shutdown request, without waiting for COM teardown</returns>
        private async Task StopAsync()
        {
            try
            {
                Dispatcher dispatcher = await _ready.Task.ConfigureAwait(false);
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }
            catch (Exception ex) { AppLogger.Write("Background STA shutdown failed", ex); }
        }
    }
}
