using System;
using System.Threading;

namespace Win10ExplorerKit.App
{
    internal sealed class SingleInstance : IDisposable
    {
        // Session-local host ownership preserves independent Windows login sessions
        private const string MutexName = @"Local\Win10ExplorerKit.Hotkey";
        // Graceful shutdown signal consumed by the tray's message loop
        private const string StopEventName = @"Local\Win10ExplorerKit.Stop";
        private readonly Mutex _mutex = new Mutex(false, MutexName);
        private readonly EventWaitHandle _stop;
        public bool IsOwner { get; private set; }

        /// <summary>
        /// Acquires host ownership without blocking another running instance
        /// </summary>
        public SingleInstance()
        {
            try { IsOwner = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { IsOwner = true; }
            if (IsOwner) _stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName);
        }

        /// <summary>
        /// Checks for a shutdown request without blocking the UI thread
        /// </summary>
        /// <returns>True when another process requested host shutdown</returns>
        public bool StopRequested() { return _stop != null && _stop.WaitOne(0); }

        /// <summary>
        /// Requests shutdown and waits for ownership to be released before uninstalling
        /// </summary>
        /// <returns>True if no host remains after the bounded wait</returns>
        public static bool RequestStop()
        {
            try { using (var stop = EventWaitHandle.OpenExisting(StopEventName)) stop.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            using (var mutex = new Mutex(false, MutexName))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(5000); }
                catch (AbandonedMutexException) { acquired = true; }
                if (acquired) mutex.ReleaseMutex();
                return acquired;
            }
        }

        /// <summary>
        /// Releases ownership and the shutdown event at application exit
        /// </summary>
        public void Dispose()
        {
            if (_stop != null) _stop.Dispose();
            if (IsOwner) { _mutex.ReleaseMutex(); IsOwner = false; }
            _mutex.Dispose();
        }
    }
}
