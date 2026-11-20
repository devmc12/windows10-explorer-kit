using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Win10ExplorerKit.Features.FileHash
{
    internal sealed class HashQueue : IDisposable
    {
        // Discovery pauses when this many files are waiting for a hash worker
        private const int QueueCapacity = 256;
        private readonly BlockingCollection<string[]> _requests = new BlockingCollection<string[]>();
        private readonly BlockingCollection<HashItem> _files = new BlockingCollection<HashItem>(QueueCapacity);
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly HashSet<string> _seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Task _completion;
        private int _requestsPending;
        private int _filesPending;
        internal readonly ConcurrentQueue<HashItem> NewItems = new ConcurrentQueue<HashItem>();
        internal bool IsBusy => Volatile.Read(ref _requestsPending) > 0 || Volatile.Read(ref _filesPending) > 0;
        internal bool IsCanceled => _cancel.IsCancellationRequested;

        /// <summary>Starts one discovery producer and a fixed set of file workers for one algorithm round</summary>
        /// <param name="algorithm">Algorithm shared by this round</param>
        /// <param name="parallelism">Maximum active file readers</param>
        /// <param name="existingFiles">Already displayed files excluded when appending after cancellation</param>
        internal HashQueue(string algorithm, int parallelism, IEnumerable<string> existingFiles = null)
        {
            if (!FileHasher.Algorithms.Contains(algorithm)) throw new ArgumentException("Invalid algorithm");
            if (parallelism != 1 && parallelism != 2 && parallelism != 4) throw new ArgumentOutOfRangeException(nameof(parallelism));
            if (existingFiles != null) _seenFiles.UnionWith(existingFiles);
            var tasks = new List<Task> { Task.Factory.StartNew(Discover, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default) };
            for (int i = 0; i < parallelism; i++)
                tasks.Add(Task.Factory.StartNew(() => Calculate(algorithm), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default));
            _completion = Task.WhenAll(tasks);
        }

        /// <summary>Appends a bounded UI or shell batch without touching the filesystem on the caller</summary>
        /// <param name="paths">Paths to files or first-level directories</param>
        internal void Add(string[] paths)
        {
            if (IsCanceled || paths.Length == 0) return;
            Interlocked.Increment(ref _requestsPending);
            _requests.Add(paths);
        }

        /// <summary>Expands directories lazily while keeping discovery and deduplication on one thread</summary>
        private void Discover()
        {
            try
            {
                foreach (string[] request in _requests.GetConsumingEnumerable(_cancel.Token))
                {
                    try
                    {
                        foreach (string raw in request)
                        {
                            _cancel.Token.ThrowIfCancellationRequested();
                            try
                            {
                                string path = Path.GetFullPath(raw);
                                FileAttributes attributes = File.GetAttributes(path);
                                if ((attributes & FileAttributes.Directory) == 0) { AddFile(path); continue; }
                                if (!_seenDirectories.Add(path)) continue;
                                bool found = false;
                                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
                                {
                                    found = true;
                                    AddFile(file);
                                }
                                if (!found) NewItems.Enqueue(new HashItem(path, false) { State = HashState.Empty });
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                NewItems.Enqueue(new HashItem(raw) { State = HashState.Failed, Error = FileHasher.ErrorKey(ex) });
                            }
                        }
                    }
                    finally { Interlocked.Decrement(ref _requestsPending); }
                }
            }
            catch (OperationCanceledException) { }
            finally { _files.CompleteAdding(); }
        }

        /// <summary>Adds a file once and waits for bounded queue space without blocking the UI</summary>
        /// <param name="path">Absolute file path</param>
        private void AddFile(string path)
        {
            _cancel.Token.ThrowIfCancellationRequested();
            if (!_seenFiles.Add(path)) return;
            var item = new HashItem(path);
            NewItems.Enqueue(item);
            Interlocked.Increment(ref _filesPending);
            try { _files.Add(item, _cancel.Token); }
            catch (OperationCanceledException)
            {
                item.State = HashState.Canceled;
                Interlocked.Decrement(ref _filesPending);
                throw;
            }
        }

        /// <summary>Processes independent files with a reusable buffer owned by this worker</summary>
        /// <param name="algorithm">Algorithm selected for this round</param>
        private void Calculate(string algorithm)
        {
            byte[] buffer = new byte[FileHasher.BufferSize];
            try
            {
                foreach (HashItem item in _files.GetConsumingEnumerable(_cancel.Token))
                {
                    try { FileHasher.Compute(item, algorithm, buffer, _cancel.Token); }
                    catch (OperationCanceledException) { item.State = HashState.Canceled; }
                    catch (Exception ex)
                    {
                        item.Error = FileHasher.ErrorKey(ex);
                        item.State = HashState.Failed;
                    }
                    finally { Interlocked.Decrement(ref _filesPending); }
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>Cancels discovery and readers and waits for all file handles to be released</summary>
        /// <returns>Completion task observed by the owning window before changing rounds</returns>
        internal async Task StopAsync()
        {
            _cancel.Cancel();
            await _completion;
            while (_files.TryTake(out HashItem item)) item.State = HashState.Canceled;
            Interlocked.Exchange(ref _requestsPending, 0);
            Interlocked.Exchange(ref _filesPending, 0);
        }

        /// <summary>Releases queues after StopAsync has completed</summary>
        public void Dispose()
        {
            _requests.Dispose();
            _files.Dispose();
            _cancel.Dispose();
        }
    }
}
