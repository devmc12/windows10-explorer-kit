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
        // Store the greatest remaining depth so overlapping selections can extend a previous scan
        private readonly Dictionary<string, int> _seenDirectories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly int _folderDepth;
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
        /// <param name="folderDepth">Selected folder counts as level one; zero includes all levels</param>
        internal HashQueue(string algorithm, int parallelism, IEnumerable<string> existingFiles = null, int folderDepth = FileHasher.DefaultFolderDepth)
        {
            if (!FileHasher.Algorithms.Contains(algorithm)) throw new ArgumentException("Invalid algorithm");
            if (Array.IndexOf(FileHasher.ParallelOptions, parallelism) < 0) throw new ArgumentOutOfRangeException(nameof(parallelism));
            if (Array.IndexOf(FileHasher.FolderDepthOptions, folderDepth) < 0) throw new ArgumentOutOfRangeException(nameof(folderDepth));
            _folderDepth = folderDepth == 0 ? int.MaxValue : folderDepth;
            string[] existing = existingFiles?.ToArray();
            // Creating up to a hundred worker threads must not stall shell-batch acceptance
            _completion = Task.Run(async () =>
            {
                if (existing != null) _seenFiles.UnionWith(existing);
                var tasks = new List<Task>();
                try
                {
                    tasks.Add(Task.Factory.StartNew(Discover, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
                    for (int i = 0; i < parallelism; i++)
                        tasks.Add(Task.Factory.StartNew(() => Calculate(algorithm), CancellationToken.None,
                            TaskCreationOptions.LongRunning, TaskScheduler.Default));
                }
                catch { _cancel.Cancel(); throw; }
                finally { await Task.WhenAll(tasks).ConfigureAwait(false); }
            });
        }

        /// <summary>Appends a bounded UI or shell batch without touching the filesystem on the caller</summary>
        /// <param name="paths">Paths to files or selected directory roots</param>
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
                                DiscoverDirectory(path);
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

        /// <summary>Streams a depth-first traversal without recursive call stacks or following linked subfolders</summary>
        /// <param name="root">Explicitly selected directory, counted as the first level</param>
        private void DiscoverDirectory(string root)
        {
            var frames = new Stack<DirectoryFrame>();
            try
            {
                PushDirectory(frames, root, _folderDepth);
                while (frames.Count > 0)
                {
                    _cancel.Token.ThrowIfCancellationRequested();
                    DirectoryFrame frame = frames.Peek();
                    string entry;
                    try
                    {
                        if (!frame.Entries.MoveNext()) { frames.Pop().Dispose(); continue; }
                        entry = frame.Entries.Current;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        NewItems.Enqueue(new HashItem(frame.Path, false) { State = HashState.Failed, Error = FileHasher.ErrorKey(ex) });
                        frames.Pop().Dispose();
                        continue;
                    }
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.Directory) == 0) AddFile(entry);
                        // Do not follow junctions or directory links discovered below an explicit root
                        else if (frame.Remaining > 1 && (attributes & FileAttributes.ReparsePoint) == 0)
                            PushDirectory(frames, entry, frame.Remaining == int.MaxValue ? int.MaxValue : frame.Remaining - 1);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        NewItems.Enqueue(new HashItem(entry) { State = HashState.Failed, Error = FileHasher.ErrorKey(ex) });
                    }
                }
            }
            finally
            {
                // Enumeration handles must also close when a bounded file queue is canceled
                while (frames.Count > 0) frames.Pop().Dispose();
            }
        }

        /// <summary>Schedules a directory only when the new root permits deeper discovery than before</summary>
        /// <param name="frames">Traversal-owned enumerator stack</param><param name="path">Directory path</param>
        /// <param name="remaining">Remaining file levels, or the unlimited sentinel</param>
        private void PushDirectory(Stack<DirectoryFrame> frames, string path, int remaining)
        {
            string identity = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (_seenDirectories.TryGetValue(identity, out int seenDepth) && seenDepth >= remaining) return;
            frames.Push(new DirectoryFrame(path, remaining));
            _seenDirectories[identity] = remaining;
        }

        private sealed class DirectoryFrame : IDisposable
        {
            internal readonly string Path;
            internal readonly int Remaining;
            internal readonly IEnumerator<string> Entries;
            /// <summary>Owns one lazy enumerator while its child directories are visited</summary>
            /// <param name="path">Directory to enumerate</param><param name="remaining">Remaining file levels</param>
            internal DirectoryFrame(string path, int remaining)
            {
                Path = path;
                Remaining = remaining;
                Entries = Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly).GetEnumerator();
            }
            /// <summary>Closes the directory enumeration handle</summary>
            public void Dispose() => Entries.Dispose();
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
            try { await _completion.ConfigureAwait(false); }
            finally
            {
                while (_files.TryTake(out HashItem item)) item.State = HashState.Canceled;
                Interlocked.Exchange(ref _requestsPending, 0);
                Interlocked.Exchange(ref _filesPending, 0);
            }
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
