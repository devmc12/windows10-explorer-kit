using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Win10ExplorerKit.Features.FileHash;
namespace Win10ExplorerKit.UI.ViewModels
{
    internal sealed class HashViewModel : ObservableObject
    {
        // Limit UI work per tick while shell selections are arriving
        private const int BatchSize = 512;
        // Consolidate worker progress at ten updates per second
        private const int RefreshMilliseconds = 100;
        private readonly SettingsService _settings;
        private readonly Action<string> _notify;
        private readonly Func<string, bool> _copy;
        private readonly DispatcherTimer _timer;
        private readonly List<string> _sources = new List<string>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _deferred = new List<string>();
        private HashQueue _queue;
        private bool _changing;
        private bool _closing;
        public ObservableCollection<HashRowViewModel> Rows { get; } = new ObservableCollection<HashRowViewModel>();
        public string[] Algorithms => FileHasher.Algorithms;
        public int[] ParallelOptions => FileHasher.ParallelOptions;
        public int[] FolderDepthOptions => FileHasher.FolderDepthOptions;
        public int FolderDepth { get; private set; }
        public string Algorithm { get; private set; }
        public int Parallelism { get; private set; }
        internal int InputEpoch { get; private set; }
        public bool IsBusy => _changing || (_queue?.IsBusy ?? false);
        public bool CanChangeAlgorithm => !_changing && !_closing;
        public bool CanChangeParallelism => !IsBusy && !_closing;
        public bool CanClear => _sources.Count > 0 && !_changing;
        public bool CanCopyAll => Rows.Any(row => row.CanCopy) && !_changing;
        public string Summary => TranslationService.T("hash.summary", Rows.Count(r => r.CanCopy), Rows.Count,
            Rows.Count(r => r.Item.State == HashState.Failed));
        public string CopyAllLabel => TranslationService.T("hash.copyAll", Rows.Count(r => r.CanCopy));
        public double Progress => Rows.Count == 0 ? 0 : Rows.Count(r => r.CanCopy || r.Item.State == HashState.Failed || r.Item.State == HashState.Canceled) * 100.0 / Rows.Count;
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
            if (_changing) { _deferred.AddRange(added); return; }
            EnsureQueue(); _queue.Add(added); _timer.Start(); Refresh();
        }
        /// <summary>Creates workers lazily and excludes completed inputs after cancellation</summary>
        private void EnsureQueue()
        {
            if (_queue == null) _queue = new HashQueue(Algorithm, Parallelism, Rows.Where(r => r.Item.IsFile).Select(r => r.Item.Path), FolderDepth);
        }
        /// <summary>Drains one result batch and updates observable properties</summary>
        internal void Refresh()
        {
            int count = 0;
            while (_queue != null && count++ < BatchSize && _queue.NewItems.TryDequeue(out HashItem item)) Rows.Add(new HashRowViewModel(item));
            foreach (var row in Rows) row.Refresh();
            Changed(nameof(Summary)); Changed(nameof(CopyAllLabel)); Changed(nameof(Progress));
            Changed(nameof(IsBusy)); Changed(nameof(CanClear)); Changed(nameof(CanCopyAll));
            Changed(nameof(CanChangeAlgorithm)); Changed(nameof(CanChangeParallelism));
            if (!IsBusy && (_queue == null || _queue.NewItems.IsEmpty))
            {
                _timer.Stop();
                if (_queue != null && !_closing) RetireIdleQueue();
            }
        }
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
            await _queue.StopAsync();
            while (_queue.NewItems.TryDequeue(out HashItem item)) Rows.Add(new HashRowViewModel(item));
            _queue.Dispose(); _queue = null;
        }
        /// <summary>Cancels active readers while retaining visible results</summary>
        /// <returns>Completion after cancellation</returns>
        internal async Task CancelAsync()
        {
            if (_changing) return;
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
                if (algorithm != null) { _settings.SetHashOptions(algorithm, Parallelism); Algorithm = algorithm; }
                Rows.Clear();
                if (_sources.Count > 0) { EnsureQueue(); _queue.Add(_sources.ToArray()); _timer.Start(); }
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
            try { await StopQueueAsync(); _settings.SetHashOptions(Algorithm, parallelism); Parallelism = parallelism; }
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
            try { await StopQueueAsync(); _settings.SetHashFolderDepth(depth); FolderDepth = depth; }
            catch (Exception ex) { AppLogger.Write("Hash preferences failed", ex); _notify(TranslationService.T("error.settings")); }
            finally { _changing = false; AppendDeferred(); Changed(nameof(FolderDepth)); Refresh(); }
        }
        /// <summary>Copies a completed row or every completed checksum</summary>
        /// <param name="row">One row, or null for all results</param>
        internal void Copy(HashRowViewModel row = null)
        {
            if (row != null && !row.CanCopy) return;
            string text = row != null ? row.Value : string.Join(Environment.NewLine, Rows.Where(r => r.CanCopy).Select(r => r.Value + "  " + r.Item.Path));
            if (text.Length > 0) _notify(TranslationService.T(_copy(text) ? "hash.copied" : "hash.clipboardError"));
        }
        /// <summary>Stops all workers before application shutdown</summary>
        /// <returns>Completion after the final queue is disposed</returns>
        internal async Task CloseAsync()
        {
            _closing = true;
            while (_changing) await Task.Delay(20);
            _timer.Stop(); await StopQueueAsync(); _timer.Stop();
        }
    }
}
