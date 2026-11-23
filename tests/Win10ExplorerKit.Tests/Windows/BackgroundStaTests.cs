using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Windows.Interop;

namespace Win10ExplorerKit.Tests.Windows
{
    [TestClass]
    public sealed class BackgroundStaTests
    {
        /// <summary>Finishes active STA work before cleanup and rejects queued work after disposal</summary>
        /// <returns>Completion of an isolated worker without shell or clipboard access</returns>
        [TestMethod]
        public async Task ShutdownPreservesApartmentOwnershipAndRejectsQueuedWork()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var release = new ManualResetEventSlim())
            {
                int cleanupThread = 0;
                bool queuedRan = false;
                var worker = new BackgroundSta("Isolated STA lifecycle", () => cleanupThread = Thread.CurrentThread.ManagedThreadId);
                try
                {
                    Task<int> active = worker.InvokeAsync(() =>
                    {
                        Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
                        entered.TrySetResult(true);
                        if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Worker was not released");
                        return Thread.CurrentThread.ManagedThreadId;
                    });
                    Assert.AreSame(entered.Task, await Task.WhenAny(entered.Task, Task.Delay(5000)));
                    Task<bool> queued = worker.InvokeAsync(() => { queuedRan = true; return true; });
                    worker.Dispose();
                    Assert.IsFalse(worker.Completion.IsCompleted);
                    release.Set();
                    int ownerThread = await active;
                    try { await queued; Assert.Fail("Disposed worker accepted queued work"); }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { }
                    Assert.AreSame(worker.Completion, await Task.WhenAny(worker.Completion, Task.Delay(5000)));
                    Assert.AreEqual(ownerThread, cleanupThread);
                    Assert.IsFalse(queuedRan);
                    worker.Dispose();
                }
                finally { release.Set(); worker.Dispose(); }
            }
        }
    }
}
