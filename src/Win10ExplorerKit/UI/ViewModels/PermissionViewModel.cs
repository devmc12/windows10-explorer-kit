// Keeps permission targets, selection and progress in memory
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.AccessControl;
using System.Windows.Threading;
using Win10ExplorerKit.Features.Permissions;

namespace Win10ExplorerKit.UI.ViewModels
{
    internal sealed class PermissionRow : ObservableObject
    {
        public string Path { get; }
        public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\'));
        private PermissionResult _result;
        public string Result => _result == null ? "" : TranslationService.T(_result.Reason, DescribeRights(_result.Rights)) +
            (_result.ErrorCode == 0 ? "" : " (" + _result.ErrorCode + ")");
        /// <summary>Formats the selected rights using the current UI language</summary>
        /// <param name="mask">Applied custom permission bits</param><returns>Localized permission list</returns>
        private static string DescribeRights(int mask)
        {
            var rights = new[] { FileSystemRights.Read, FileSystemRights.Write, FileSystemRights.ExecuteFile,
                FileSystemRights.Delete, FileSystemRights.ChangePermissions, FileSystemRights.TakeOwnership };
            string[] keys = { "read", "write", "execute", "delete", "change", "owner" };
            return string.Join(", ", rights.Select((right, index) => (mask & (int)right) == (int)right
                ? TranslationService.T("permissions." + keys[index]) : null).Where(name => name != null));
        }
        /// <summary>Creates an in-memory target without probing filesystem permissions</summary><param name="path">Canonical target</param>
        internal PermissionRow(string path) => Path = path;
        /// <summary>Updates the localized result cell</summary><param name="result">Sanitized outcome</param>
        internal void SetResult(PermissionResult result) { _result = result; Changed(nameof(Result)); }
        /// <summary>Refreshes translated outcomes</summary>
        internal void RefreshLanguage() => Changed(nameof(Result));
    }

    internal sealed class PermissionViewModel : ObservableObject
    {
        // Bound shell operations while a previous operation or elevation prompt is still active
        private const int MaximumQueuedBatches = 16;
        private readonly Action<string> _notify;
        private readonly Queue<PermissionRequest> _queue = new Queue<PermissionRequest>();
        private readonly Dictionary<string, PermissionRow> _byPath = new Dictionary<string, PermissionRow>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<PermissionResult> _results = new ConcurrentQueue<PermissionResult>();
        private readonly DispatcherTimer _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private string[] _selected = Array.Empty<string>();
        private CancellationTokenSource _cancel;
        private Task _running = Task.CompletedTask;
        private bool _busy, _stopping;
        private int _done, _total;
        public ObservableCollection<PermissionRow> Rows { get; } = new ObservableCollection<PermissionRow>();
        public bool IsBusy => _busy;
        public bool CanEdit => !_busy && !_stopping;
        public bool CanApply => CanEdit && Rows.Count > 0;
        public int TargetCount => _selected.Length > 0 ? _selected.Length : Rows.Count;
        public string TargetLabel => TranslationService.T("permissions.targets", TargetCount);
        public string Progress => TranslationService.T("permissions.progress", _done, _total);
        public Task Completion => _running;

        /// <summary>Owns page state and one serial background task queue</summary><param name="notify">UI feedback</param>
        internal PermissionViewModel(Action<string> notify)
        {
            _notify = notify;
            _refresh.Tick += (sender, args) => DrainResults();
        }

        /// <summary>Adds targets without reading ACLs or changing permissions</summary>
        /// <param name="paths">Dropped or selected paths</param><returns>Whether the entire addition fits the list</returns>
        internal bool AddPaths(IEnumerable<string> paths)
        {
            string[] additions;
            try { additions = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !_byPath.ContainsKey(path)).ToArray(); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { _notify(TranslationService.T("permissions.invalidPath")); return false; }
            if (_stopping || Rows.Count + additions.Length > PermissionInbox.MaximumItems)
            { _notify(TranslationService.T("permissions.limit")); return false; }
            foreach (string path in additions) { var row = new PermissionRow(path); _byPath.Add(path, row); Rows.Add(row); }
            Refresh();
            return true;
        }

        /// <summary>Snapshots selected rows independently of virtualization</summary><param name="rows">Current selection</param>
        internal void Select(IEnumerable<PermissionRow> rows) { _selected = rows.Select(row => row.Path).ToArray(); Refresh(); }

        /// <summary>Enqueues the current selection or the whole page without retaining settings</summary>
        /// <param name="operation">Fixed operation</param><param name="mask">Custom rights</param>
        internal void Apply(string operation, int mask = 0)
        {
            if (!CanApply) return;
            ApplyTo(SnapshotTargets(), operation, mask);
        }

        /// <summary>Captures explicit targets before opening a modal or accepting more shell selections</summary>
        /// <returns>Selected paths, or the current list when nothing is selected</returns>
        internal string[] SnapshotTargets() => _selected.Length > 0 ? (string[])_selected.Clone() : Rows.Select(row => row.Path).ToArray();

        /// <summary>Retains discovery failures on the original rows without counting them as ACL writes</summary>
        /// <param name="result">Read-only inspection outcome</param>
        internal void ReportInspection(PermissionResult result)
        {
            if (_byPath.TryGetValue(result.Path, out var row)) row.SetResult(result);
        }

        /// <summary>Queues a direct operation against a captured selection</summary>
        /// <param name="paths">Captured targets</param><param name="operation">Operation</param><param name="mask">Custom rights</param>
        internal void ApplyTo(string[] paths, string operation, int mask)
        {
            if (!Enqueue(new PermissionRequest { Operation = operation, Mask = mask,
                Paths = paths,
                ProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }))
                _notify(TranslationService.T("permissions.limit"));
        }

        /// <summary>Reserves the serial queue while a custom dialog discovers and selects principals</summary>
        /// <param name="prompt">Modal workflow using the same worker session as its eventual write</param>
        /// <returns>Completion after dialog cleanup and any queued shell operations</returns>
        internal Task CustomizeAsync(Func<PermissionSession, CancellationToken, Task<PermissionRequest>> prompt)
        {
            if (!CanApply) return Task.CompletedTask;
            _busy = true;
            Refresh();
            _running = RunCustomAsync(prompt);
            return _running;
        }

        /// <summary>Keeps discovery, cancellation and elevation under the application shutdown lifetime</summary>
        /// <param name="prompt">Dialog and immutable option snapshot</param><returns>Completion after helper teardown</returns>
        private async Task RunCustomAsync(Func<PermissionSession, CancellationToken, Task<PermissionRequest>> prompt)
        {
            var session = new PermissionSession();
            _refresh.Start();
            try
            {
                using (_cancel = new CancellationTokenSource())
                {
                    var request = await prompt(session, _cancel.Token);
                    if (request != null && request.IsValid() && !_stopping && !_cancel.IsCancellationRequested)
                        await ExecuteBatchAsync(request, _cancel.Token, session);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Write("Custom permission workflow failed", ex); if (!_stopping) _notify(TranslationService.T("permissions.failed")); }
            finally
            {
                _cancel = null;
                try { await session.CloseAsync(); }
                finally
                {
                    _refresh.Stop();
                    DrainResults();
                    _busy = false;
                    Refresh();
                }
            }
            if (_queue.Count > 0 && !_stopping)
            {
                _busy = true;
                Refresh();
                await RunAsync();
            }
        }

        /// <summary>Accepts a complete shell transaction into the serial task queue</summary>
        /// <param name="request">Immutable validated batch</param><returns>Whether the entire batch was accepted</returns>
        internal bool Enqueue(PermissionRequest request)
        {
            if (_stopping || _queue.Count >= MaximumQueuedBatches || !request.IsValid() ||
                request.Paths == null || request.Paths.Length == 0 || !AddPaths(request.Paths)) return false;
            _queue.Enqueue(request);
            if (!_busy) { _busy = true; Refresh(); _running = RunAsync(); }
            return true;
        }

        /// <summary>Runs batches without doing ACL or UAC work on the dispatcher</summary><returns>Completion after all accepted work is retired</returns>
        private async Task RunAsync()
        {
            _refresh.Start();
            try
            {
                while (_queue.Count > 0 && !_stopping)
                {
                    var request = _queue.Dequeue();
                    using (_cancel = new CancellationTokenSource())
                    {
                        await ExecuteBatchAsync(request, _cancel.Token);
                    }
                    _cancel = null;
                }
            }
            catch (Exception ex) { AppLogger.Write("Permission batch failed", ex); if (!_stopping) _notify(TranslationService.T("permissions.failed")); }
            finally { _cancel = null; _queue.Clear(); _busy = false; _refresh.Stop(); DrainResults(); Refresh(); }
        }

        /// <summary>Publishes identical progress and results for direct and custom operations</summary>
        /// <param name="request">Captured operation</param><param name="token">Batch cancellation</param>
        /// <param name="session">Optional worker already used for discovery</param><returns>Completion after all results are displayed</returns>
        private async Task ExecuteBatchAsync(PermissionRequest request, CancellationToken token, PermissionSession session = null)
        {
            AppLogger.Write("Permission batch " + request.Operation + " started, count " + request.Paths.Length, null);
            _done = 0; _total = request.Paths.Length;
            foreach (string path in request.Paths) _byPath[path].SetResult(PermissionResult.Create(path, "pending", "permissions.pending"));
            Refresh();
            int succeeded = 0, failed = 0, skipped = 0, canceled = 0;
            await PermissionService.RunAsync(request, result =>
            {
                if (result.Status == "success") succeeded++;
                else if (result.Status == "skipped") skipped++;
                else if (result.Status == "canceled") canceled++;
                else failed++;
                _results.Enqueue(result);
            }, token, session);
            while (!_results.IsEmpty)
            {
                DrainResults();
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            if (!_stopping) _notify(TranslationService.T("permissions.summary", succeeded, failed, skipped, canceled));
            AppLogger.Write("Permission batch finished: success " + succeeded + ", failed " + failed +
                ", skipped " + skipped + ", canceled " + canceled, null);
        }

        /// <summary>Updates a bounded set of result rows on each dispatcher tick</summary>
        private void DrainResults()
        {
            int count = 0;
            while (count++ < 128 && _results.TryDequeue(out var result))
            { if (_byPath.TryGetValue(result.Path, out var row)) row.SetResult(result); _done++; }
            Changed(nameof(Progress));
        }

        /// <summary>Cancels queued operations and lets the current native write finish</summary>
        internal void Cancel()
        {
            foreach (var request in _queue)
                foreach (string path in request.Paths) _byPath[path].SetResult(PermissionResult.Create(path, "canceled", "permissions.canceled"));
            _queue.Clear(); _cancel?.Cancel();
        }

        /// <summary>Stops accepting work before shutdown waits for the active worker</summary>
        internal void Stop() { _stopping = true; Cancel(); Refresh(); }

        /// <summary>Removes selected targets or all targets without changing any ACL</summary><param name="all">Whether to clear the page</param>
        internal void Remove(bool all)
        {
            if (!CanEdit) return;
            foreach (var row in Rows.Where(row => all || _selected.Contains(row.Path)).ToArray()) { Rows.Remove(row); _byPath.Remove(row.Path); }
            _selected = Array.Empty<string>(); Refresh();
        }

        /// <summary>Updates translated row results and labels</summary>
        internal void RefreshLanguage() { foreach (var row in Rows) row.RefreshLanguage(); Refresh(); }

        /// <summary>Refreshes command availability and selection counters</summary>
        private void Refresh()
        {
            Changed(nameof(IsBusy)); Changed(nameof(CanEdit)); Changed(nameof(CanApply));
            Changed(nameof(TargetCount)); Changed(nameof(TargetLabel)); Changed(nameof(Progress));
        }
    }
}
