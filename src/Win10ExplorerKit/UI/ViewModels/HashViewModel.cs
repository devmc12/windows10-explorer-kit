using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Win10ExplorerKit.Features.FileHash;
namespace Win10ExplorerKit.UI.ViewModels
{
    internal sealed class HashViewModel : ObservableObject
    {
        // Limit UI work per tick while shell selections are arriving
        private const int BatchSize = 128;
        // Yield UI updates after this many milliseconds even if a result batch is incomplete
        private const int RefreshBudgetMilliseconds = 8;
        // Consolidate worker progress at ten updates per second
        private const int RefreshMilliseconds = 100;
        // Keep idle workers briefly so adjacent shell batches do not repeatedly create reader threads
        private const int IdleGraceMilliseconds = 500;
        private readonly SettingsService _settings;
        private readonly Action<string> _notify;
        private readonly Func<string, bool> _copy;
        private readonly DispatcherTimer _timer;
        private readonly List<string> _sources = new List<string>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _deferred = new List<string>();
        private readonly Queue<HashRowViewModel> _pendingRows = new Queue<HashRowViewModel>();
        private int _completed;
        private int _failed;
        private int _finished;
        private int _languageRefreshIndex = -1;
        private DateTime? _idleSince;
        private HashQueue _queue;
        private bool _changing;
        private bool _closing;
        private bool _copying;
        private BackgroundSta _clipboard;
        private Task _closeTask;
        public ObservableCollection<HashRowViewModel> Rows { get; } = new ObservableCollection<HashRowViewModel>();
        public string[] Algorithms => FileHasher.Algorithms;
        public int[] ParallelOptions => FileHasher.ParallelOptions;
        public int[] FolderDepthOptions => FileHasher.FolderDepthOptions;
        public int FolderDepth { get; private set; }
        public string Algorithm { get; private set; }
        public int Parallelism { get; private set; }
        internal int InputEpoch { get; private set; }
        public bool IsBusy => _changing || (_queue?.IsBusy ?? false) || (_queue != null && !_queue.NewItems.IsEmpty) || _pendingRows.Count > 0;
        public bool CanChangeAlgorithm => !_changing && !_closing;
        public bool CanChangeParallelism => !IsBusy && !_closing;
        public bool CanClear => _sources.Count > 0 && !_changing && !_closing;
        public bool CanCopyAll => _completed > 0 && !_changing && !_closing;
        public string Summary => TranslationService.T("hash.summary", _completed, Rows.Count, _failed);
        public string CopyAllLabel => TranslationService.T("hash.copyAll", _completed);
        public double Progress => Rows.Count == 0 ? 0 : _finished * 100.0 / Rows.Count;
        /// <summary>Owns hash rounds independently of navigation and window visibility</summary>
        /// <param name="settings">Persisted algorithm preferences</param>
        /// <param name="notify">Localized user feedback</param>
        /// <param name="copy">Optional clipboard substitute for isolated tests</param>
        internal HashViewModel(SettingsService settings, Action<string> notify, Func<string, bool> copy = null)
        {
            _settings = settings; _notify = notify; _copy = copy ?? ClipboardService.SetText;
            Algorithm = settings.HashAlgorithm; Parallelism = settings.HashParallelism; FolderDepth = settings.HashFolderDepth;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(RefreshMilliseconds) };
            _timer.Tick += (sender, args) => Refresh();
        }
        /// <summary>Accepts selections without enumerating files on the dispatcher</summary>
        /// <param name="paths">File or folder paths</param>
        internal void AddPaths(string[] paths)
        {
            if (_closing) return;
            var added = paths.Where(path => !string.IsNullOrWhiteSpace(path) && _seen.Add(path)).ToArray();
            _sources.AddRange(added);
            if (added.Length == 0) return;
            _idleSince = null;
            if (_changing) { _deferred.AddRange(added); return; }
            EnsureQueue(); _queue.Add(added); _timer.Start();
        }
        /// <summary>Creates workers lazily and excludes completed inputs after cancellation</summary>
        private void EnsureQueue()
        {
            if (_queue == null) _queue = new HashQueue(Algorithm, Parallelism, Rows.Where(r => r.Item.IsFile).Select(r => r.Item.Path), FolderDepth);
        }
        /// <summary>Drains one result batch and updates observable properties</summary>
        internal void Refresh()
        {
            if (_closing) { _timer.Stop(); return; }
            var budget = Stopwatch.StartNew();
            int pending = Math.Min(_pendingRows.Count, BatchSize);
            while (pending-- > 0 && budget.ElapsedMilliseconds < RefreshBudgetMilliseconds)
            {
                HashRowViewModel row = _pendingRows.Dequeue();
                UpdateRow(row);
                if (!IsFinished(row.DisplayState)) _pendingRows.Enqueue(row);
            }
            int count = 0;
            while (_queue != null && count++ < BatchSize && budget.ElapsedMilliseconds < RefreshBudgetMilliseconds &&
                _queue.NewItems.TryDequeue(out HashItem item)) AddRow(item);
            count = 0;
            while (_languageRefreshIndex >= 0 && _languageRefreshIndex < Rows.Count && count++ < BatchSize &&
                budget.ElapsedMilliseconds < RefreshBudgetMilliseconds) UpdateRow(Rows[_languageRefreshIndex++]);
            if (_languageRefreshIndex >= Rows.Count) _languageRefreshIndex = -1;
            Changed(nameof(Summary)); Changed(nameof(CopyAllLabel)); Changed(nameof(Progress));
            Changed(nameof(IsBusy)); Changed(nameof(CanClear)); Changed(nameof(CanCopyAll));
            Changed(nameof(CanChangeAlgorithm)); Changed(nameof(CanChangeParallelism));
            if (!IsBusy && _languageRefreshIndex < 0)
            {
                if (_queue == null || _closing) _timer.Stop();
                else
                {
                    if (!_idleSince.HasValue) _idleSince = DateTime.UtcNow;
                    if ((DateTime.UtcNow - _idleSince.Value).TotalMilliseconds >= IdleGraceMilliseconds)
                    { _timer.Stop(); RetireIdleQueue(); }
                }
            }
            else _idleSince = null;
        }
        /// <summary>Refreshes localized row text in bounded batches instead of walking the whole table</summary>
        internal void RefreshLanguage() { if (_closing) return; _languageRefreshIndex = 0; _timer.Start(); }

        /// <summary>Publishes one discovered row and registers only unfinished work for progress updates</summary>
        /// <param name="item">Worker-owned result</param>
        private void AddRow(HashItem item)
        {
            var row = new HashRowViewModel(item);
            Rows.Add(row);
            CountState(row.DisplayState, 1);
            if (!IsFinished(row.DisplayState)) _pendingRows.Enqueue(row);
        }
        /// <summary>Updates a row and adjusts summary counters only when its published state changes</summary>
        /// <param name="row">Existing observable result</param>
        private void UpdateRow(HashRowViewModel row)
        {
            HashState previous = row.DisplayState;
            row.Refresh();
            if (previous == row.DisplayState) return;
            CountState(previous, -1);
            CountState(row.DisplayState, 1);
        }
        /// <summary>Maintains constant-time summary properties for large selections</summary>
        /// <param name="state">State entering or leaving the displayed counts</param><param name="delta">Count adjustment</param>
        private void CountState(HashState state, int delta)
        {
            if (state == HashState.Complete) _completed += delta;
            if (state == HashState.Failed) _failed += delta;
            if (IsFinished(state)) _finished += delta;
        }
        /// <summary>Identifies rows that no longer need periodic progress updates</summary>
        /// <param name="state">Published worker state</param><returns>Whether the state is terminal</returns>
        private static bool IsFinished(HashState state) => state == HashState.Complete || state == HashState.Failed || state == HashState.Canceled;
        /// <summary>Releases idle worker threads while deferring newly arriving selections</summary>
        private async void RetireIdleQueue()
        {
            _changing = true;
            try { await StopQueueAsync(); }
            catch (Exception ex) { AppLogger.Write("Hash worker retirement failed", ex); }
            finally { _changing = false; AppendDeferred(); Refresh(); }
        }
        /// <summary>Stops readers before releasing the queue and preserves published results</summary>
        /// <returns>Completion after file handles are released</returns>
        private async Task StopQueueAsync()
        {
            if (_queue == null) return;
            HashQueue queue = _queue;
            try
            {
                await queue.StopAsync();
                while (!_closing && !queue.NewItems.IsEmpty)
                {
                    var budget = Stopwatch.StartNew();
                    int count = 0;
                    while (count++ < BatchSize && budget.ElapsedMilliseconds < RefreshBudgetMilliseconds && queue.NewItems.TryDequeue(out HashItem item))
                        AddRow(item);
                    if (!queue.NewItems.IsEmpty) await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }
            finally { queue.Dispose(); if (ReferenceEquals(_queue, queue)) _queue = null; }
        }
        /// <summary>Cancels active readers while retaining visible results</summary>
        /// <returns>Completion after cancellation</returns>
        internal async Task CancelAsync()
        {
            if (_changing || _closing) return;
            InputEpoch++;
            _changing = true; Refresh();
            try { await StopQueueAsync(); }
            finally { _changing = false; AppendDeferred(); Refresh(); }
        }
        /// <summary>Clears results or changes the algorithm as an atomic queue transition</summary>
        /// <param name="clear">Whether to discard existing inputs</param>
        /// <param name="algorithm">Optional replacement algorithm</param>
        /// <returns>Completion after the replacement round is ready</returns>
        internal async Task ChangeAsync(bool clear, string algorithm = null)
        {
            if (_changing || _closing) return;
            _changing = true; Refresh();
            if (clear) { InputEpoch++; _sources.Clear(); _seen.Clear(); _deferred.Clear(); }
            try
            {
                await StopQueueAsync();
                if (algorithm != null) { await Task.Run(() => _settings.SetHashOptions(algorithm, Parallelism)); Algorithm = algorithm; }
                Rows.Clear();
                _pendingRows.Clear(); _completed = _failed = _finished = 0; _languageRefreshIndex = -1;
                if (!_closing && _sources.Count > 0) { EnsureQueue(); _queue.Add(_sources.ToArray()); _timer.Start(); }
                _deferred.Clear();
            }
            catch (Exception ex) { AppLogger.Write("Hash round update failed", ex); _notify(TranslationService.T("error.settings")); }
            finally { _changing = false; AppendDeferred(); Changed(nameof(Algorithm)); Refresh(); }
        }
        /// <summary>Appends inputs received while cancellation was awaiting worker shutdown</summary>
        private void AppendDeferred()
        {
            if (_deferred.Count > 0 && !_closing) { EnsureQueue(); _queue.Add(_deferred.ToArray()); _timer.Start(); }
            _deferred.Clear();
        }
        /// <summary>Updates idle concurrency without mixing worker limits in a round</summary>
        /// <param name="parallelism">Requested reader count</param>
        /// <returns>Completion after idle workers are released</returns>
        internal async Task SetParallelismAsync(int parallelism)
        {
            if (!CanChangeParallelism) return;
            _changing = true; Refresh();
            try { await StopQueueAsync(); await Task.Run(() => _settings.SetHashOptions(Algorithm, parallelism)); Parallelism = parallelism; }
            catch (Exception ex) { AppLogger.Write("Hash preferences failed", ex); _notify(TranslationService.T("error.settings")); }
            finally { _changing = false; AppendDeferred(); Changed(nameof(Parallelism)); Refresh(); }
        }
        /// <summary>Updates the depth for subsequent inputs while retaining completed results</summary>
        /// <param name="depth">Selected depth, with zero including all levels</param>
        /// <returns>Completion after the idle queue is released</returns>
        internal async Task SetFolderDepthAsync(int depth)
        {
            if (!CanChangeParallelism) return;
            _changing = true; Refresh();
            try { await StopQueueAsync(); await Task.Run(() => _settings.SetHashFolderDepth(depth)); FolderDepth = depth; }
            catch (Exception ex) { AppLogger.Write("Hash preferences failed", ex); _notify(TranslationService.T("error.settings")); }
            finally { _changing = false; AppendDeferred(); Changed(nameof(FolderDepth)); Refresh(); }
        }
        /// <summary>Copies a completed row or every completed checksum</summary>
        /// <param name="row">One row, or null for all results</param>
        /// <returns>Completion after the clipboard STA finishes</returns>
        internal async Task CopyAsync(HashRowViewModel row = null)
        {
            if (_closing || _copying || (row != null && !row.CanCopy)) return;
            _copying = true;
            try
            {
                string[] lines = row != null ? new[] { row.Value } : Rows.Where(r => r.CanCopy).Select(r => r.Value + "  " + r.Item.Path).ToArray();
                if (lines.Length == 0) return;
                if (_clipboard == null) _clipboard = new BackgroundSta("Hash clipboard");
                bool copied = await _clipboard.InvokeAsync(() => _copy(string.Join(Environment.NewLine, lines)));
                if (!_closing) _notify(TranslationService.T(copied ? "hash.copied" : "hash.clipboardError"));
            }
            catch (Exception ex)
            {
                AppLogger.Write("Hash clipboard operation failed", ex);
                if (!_closing) _notify(TranslationService.T("hash.clipboardError"));
            }
            finally { _copying = false; }
        }
        /// <summary>Stops all workers before application shutdown</summary>
        /// <returns>Completion after the final queue is disposed</returns>
        internal Task CloseAsync() => _closeTask ?? (_closeTask = CloseCoreAsync());

        /// <summary>Releases worker-owned resources after their final operations complete</summary>
        /// <returns>Completion of reader and clipboard shutdown</returns>
        private async Task CloseCoreAsync()
        {
            _closing = true;
            _timer.Stop();
            _clipboard?.Dispose();
            try
            {
                while (_changing) await Task.Delay(20);
                await StopQueueAsync();
            }
            finally
            {
                _timer.Stop();
                if (_clipboard != null) await _clipboard.Completion;
            }
        }
    }
}
