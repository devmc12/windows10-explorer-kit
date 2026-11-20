using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Win10ExplorerKit.Features.FileHash;
using Win10ExplorerKit.Windows.Interop;

namespace Win10ExplorerKit.UI.Forms.Hash
{
    internal sealed class HashForm : Form
    {
        // UI updates are consolidated at most ten times per second
        private const int RefreshMilliseconds = 100;
        // Bound the work done by one UI tick when thousands of rows arrive
        private const int RowsPerTick = 512;
        // Copy confirmation lifetime in milliseconds
        private const int CopyFeedbackMilliseconds = 1200;
        private readonly SettingsService _settings;
        private readonly Func<string, bool> _copyText;
        private readonly CopyCursor _copyCursor;
        private DateTime _copiedUntil;
        private readonly ComboBox _algorithm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly ComboBox _parallelism = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64 };
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _summary = new Label { AutoSize = true };
        private readonly Button _copyAll = new Button { AutoSize = true };
        private readonly Button _cancel = new Button { AutoSize = true };
        private readonly Button _clear = new Button { AutoSize = true };
        private readonly Timer _refresh = new Timer { Interval = RefreshMilliseconds };
        private readonly List<HashItem> _rows = new List<HashItem>();
        private readonly List<string> _sources = new List<string>();
        private readonly HashSet<string> _sourceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _deferred = new List<string>();
        private HashQueue _queue;
        private bool _changing;
        private bool _closing;
        private bool _canClose;
        private int _receiving;
        internal int InputEpoch { get; private set; }
        internal bool IsBusy => _changing || _receiving > 0 || (_queue?.IsBusy ?? false);
        internal IReadOnlyList<HashItem> Results => _rows;

        /// <summary>Builds a resizable virtual results window with localized controls</summary>
        /// <param name="settings">Configuration service for hash preferences</param>
        /// <param name="copyText">Optional clipboard writer for isolated interaction tests</param>
        internal HashForm(SettingsService settings, Func<string, bool> copyText = null)
        {
            _settings = settings;
            _copyText = copyText ?? ClipboardService.SetText;
            _copyCursor = new CopyCursor();
            Text = ProductInfo.Name + " — " + TranslationService.T("hash.title");
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(730, 450);
            MinimumSize = new Size(730, 380);
            StartPosition = FormStartPosition.CenterScreen;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false,
                Padding = new Padding(0, 0, 0, 8) };
            top.Controls.Add(new Label { Text = TranslationService.T("hash.algorithm"), AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            top.Controls.Add(_algorithm);
            top.Controls.Add(new Label { Text = TranslationService.T("hash.parallelism"), AutoSize = true, Margin = new Padding(20, 6, 8, 0) });
            top.Controls.Add(_parallelism);
            _algorithm.AccessibleName = TranslationService.T("hash.algorithm");
            _parallelism.AccessibleName = TranslationService.T("hash.parallelism");
            _algorithm.Items.AddRange(FileHasher.Algorithms);
            _parallelism.Items.AddRange(new object[] { 1, 2, 4 });
            _algorithm.SelectedItem = settings.HashAlgorithm;
            _parallelism.SelectedItem = settings.HashParallelism;
            _algorithm.SelectedIndexChanged += async (sender, args) => await ChangeAsync(false);
            _parallelism.SelectedIndexChanged += async (sender, args) =>
            {
                await CancelAsync();
                SavePreferences();
            };
            _grid.Dock = DockStyle.Fill;
            _grid.VirtualMode = true;
            _grid.ReadOnly = true;
            _grid.ShowCellToolTips = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.AccessibleName = TranslationService.T("hash.results");
            _grid.Columns.Add("name", TranslationService.T("hash.file"));
            _grid.Columns.Add("directory", TranslationService.T("hash.directory"));
            _grid.Columns.Add("value", TranslationService.T("hash.value"));
            int[] widths = { 150, 180, 300 };
            for (int i = 0; i < widths.Length; i++) _grid.Columns[i].Width = widths[i];
            _grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns[2].MinimumWidth = 220;
            _grid.Columns[2].DefaultCellStyle.Font = new Font("Consolas", 9F);
            foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.CellValueNeeded += CellValue;
            _grid.MouseClick += (sender, args) =>
            {
                int row = HitCopyValue(args.Location);
                if (args.Button == MouseButtons.Left && row >= 0) Copy(_rows[row].Value);
            };
            // MouseMove also covers the empty area below the last row
            _grid.MouseMove += (sender, args) => UpdateCopyCursor();
            _grid.MouseLeave += (sender, args) => _grid.Cursor = Cursors.Default;
            _grid.Scroll += (sender, args) => UpdateCopyCursor();
            _grid.ColumnWidthChanged += (sender, args) => UpdateCopyCursor();
            _grid.KeyDown += (sender, args) =>
            {
                if (_grid.CurrentCell != null && ((args.Control && args.KeyCode == Keys.C) ||
                    (args.KeyCode == Keys.Enter && _grid.CurrentCell.ColumnIndex == 2)))
                {
                    HashItem row = _rows[_grid.CurrentCell.RowIndex];
                    if (row.State == HashState.Complete) Copy(row.Value);
                    args.SuppressKeyPress = true;
                }
            };
            var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
            _summary.Margin = new Padding(0, 8, 16, 0);
            _cancel.Text = TranslationService.T("hash.cancel");
            _clear.Text = TranslationService.T("hash.clear");
            bottom.Controls.AddRange(new Control[] { _summary, _copyAll, _cancel, _clear });
            _copyAll.Click += (sender, args) => Copy(string.Join(Environment.NewLine,
                _rows.Where(r => r.State == HashState.Complete).Select(r => r.Value + "  " + r.Path)));
            _cancel.Click += async (sender, args) => await CancelAsync();
            _clear.Click += async (sender, args) => await ChangeAsync(true);
            layout.Controls.Add(top, 0, 0);
            layout.Controls.Add(_grid, 0, 1);
            layout.Controls.Add(bottom, 0, 2);
            Controls.Add(layout);
            EnableFileDrop(this);
            _refresh.Tick += (sender, args) => UpdateResults();
            _refresh.Start();
            UpdateResults();
        }

        /// <summary>Registers every child surface because WinForms drag events do not bubble</summary>
        /// <param name="target">Window or child control accepting filesystem selections</param>
        private void EnableFileDrop(Control target)
        {
            target.AllowDrop = true;
            target.DragEnter += FileDragEnter;
            target.DragOver += FileDragEnter;
            target.DragDrop += FilesDropped;
            foreach (Control child in target.Controls) EnableFileDrop(child);
        }

        /// <summary>Advertises copy-only drops without reading any files on the UI thread</summary>
        /// <param name="sender">Drop surface</param><param name="args">Dragged formats and allowed operations</param>
        private void FileDragEnter(object sender, DragEventArgs args)
        {
            args.Effect = !_closing && (args.AllowedEffect & DragDropEffects.Copy) != 0 &&
                args.Data != null && args.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// <summary>Feeds dropped files and folders into the existing background discovery queue</summary>
        /// <param name="sender">Drop surface</param><param name="args">Explorer filesystem selection</param>
        private void FilesDropped(object sender, DragEventArgs args)
        {
            FileDragEnter(sender, args);
            if (args.Effect != DragDropEffects.Copy) return;
            try
            {
                if (args.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
            }
            catch (Exception ex)
            {
                AppLogger.Write("Hash file drop failed", ex);
                MessageBox.Show(this, TranslationService.T("hash.selectionError"), ProductInfo.Name);
            }
        }

        /// <summary>Restricts copy affordances to completed hash cells</summary>
        /// <param name="row">Row index, possibly a header</param><param name="column">Column index</param>
        /// <returns>Whether clicking the cell can copy a hash</returns>
        private bool CanCopy(int row, int column)
        {
            return column == 2 && row >= 0 && row < _rows.Count && _rows[row].State == HashState.Complete;
        }

        /// <summary>Finds a completed hash only when the pointer is over its rendered text</summary>
        /// <param name="point">Pointer position in grid client coordinates</param>
        /// <returns>The copyable row index, or minus one outside hash text</returns>
        private int HitCopyValue(Point point)
        {
            var hit = _grid.HitTest(point.X, point.Y);
            if (hit.Type != DataGridViewHitTestType.Cell || !CanCopy(hit.RowIndex, hit.ColumnIndex)) return -1;
            Rectangle cell = _grid.GetCellDisplayRectangle(hit.ColumnIndex, hit.RowIndex, false);
            Rectangle text = _grid[hit.ColumnIndex, hit.RowIndex].GetContentBounds(hit.RowIndex);
            text.Offset(cell.Location);
            // Use the cell's own text layout so alignment, clipping and DPI stay consistent
            return text.Contains(point) ? hit.RowIndex : -1;
        }

        /// <summary>Keeps hover feedback aligned with the current text after layout and result changes</summary>
        private void UpdateCopyCursor()
        {
            if (!_grid.IsHandleCreated || _closing) return;
            Point point = _grid.PointToClient(Control.MousePosition);
            _grid.Cursor = _grid.ClientRectangle.Contains(point) && HitCopyValue(point) >= 0
                ? _copyCursor.Value : Cursors.Default;
        }

        /// <summary>Tracks shell enumeration separately from file calculation</summary>
        /// <param name="starting">Whether a new selection is arriving</param>
        internal void Receiving(bool starting)
        {
            _receiving += starting ? 1 : -1;
            UpdateResults();
        }

        /// <summary>Appends a shell batch and restarts a canceled queue when necessary</summary>
        /// <param name="paths">Filesystem paths received from Explorer</param>
        internal void AddPaths(string[] paths)
        {
            if (_closing) return;
            var added = new List<string>();
            foreach (string path in paths)
                if (_sourceSet.Add(path)) { _sources.Add(path); added.Add(path); }
            if (added.Count == 0) return;
            if (_changing) { _deferred.AddRange(added); return; }
            if (_queue == null)
                _queue = new HashQueue((string)_algorithm.SelectedItem, (int)_parallelism.SelectedItem,
                    _rows.Where(r => r.IsFile).Select(r => r.Path));
            _queue.Add(added.ToArray());
            UpdateResults();
        }

        /// <summary>Renders current state without creating controls for individual rows</summary>
        /// <param name="sender">Virtual results table</param>
        /// <param name="args">Requested row and column</param>
        private void CellValue(object sender, DataGridViewCellValueEventArgs args)
        {
            if (args.RowIndex >= _rows.Count) return;
            HashItem row = _rows[args.RowIndex];
            switch (args.ColumnIndex)
            {
                case 0: args.Value = System.IO.Path.GetFileName(row.Path); break;
                case 1: args.Value = System.IO.Path.GetDirectoryName(row.Path); break;
                case 2: args.Value = row.State == HashState.Complete ? row.Value :
                    row.State == HashState.Failed ? TranslationService.T(row.Error) :
                    TranslationService.T("hash." + row.State.ToString().ToLowerInvariant()) +
                    (row.State == HashState.Reading ? " " + row.Percent() + "%" : ""); break;
            }
        }

        /// <summary>Drains a bounded result batch and refreshes summary and command availability</summary>
        private void UpdateResults()
        {
            int count = 0;
            while (_queue != null && count++ < RowsPerTick && _queue.NewItems.TryDequeue(out HashItem row)) _rows.Add(row);
            _grid.RowCount = _rows.Count;
            _grid.Invalidate();
            int completed = _rows.Count(r => r.State == HashState.Complete);
            int failed = _rows.Count(r => r.State == HashState.Failed);
            _summary.Text = TranslationService.T("hash.summary", completed, _rows.Count, failed) +
                (_receiving > 0 ? " · " + TranslationService.T("hash.receiving") : "") +
                (DateTime.UtcNow < _copiedUntil ? " · " + TranslationService.T("hash.copied") : "");
            _copyAll.Text = TranslationService.T("hash.copyAll", completed);
            _copyAll.Enabled = completed > 0 && !_changing;
            _cancel.Enabled = IsBusy && !_changing;
            _clear.Enabled = _sources.Count > 0 && !_changing;
            _algorithm.Enabled = !_changing && !_closing;
            _parallelism.Enabled = !IsBusy && !_closing;
            UpdateCopyCursor();
        }

        /// <summary>Stores selected preferences and reports any write failure to the user</summary>
        private void SavePreferences()
        {
            try { _settings.SetHashOptions((string)_algorithm.SelectedItem, (int)_parallelism.SelectedItem); }
            catch (Exception ex)
            {
                AppLogger.Write("Hash preferences save failed", ex);
                MessageBox.Show(this, TranslationService.T("error.settings"), ProductInfo.Name);
            }
        }

        /// <summary>Stops and disposes the old round before creating any replacement readers</summary>
        /// <returns>Completion after every reader has stopped</returns>
        private async Task StopQueueAsync()
        {
            if (_queue == null) return;
            await _queue.StopAsync();
            while (_queue.NewItems.TryDequeue(out HashItem row)) _rows.Add(row);
            _queue.Dispose();
            _queue = null;
        }

        /// <summary>Cancels the current round while preserving completed results</summary>
        /// <returns>Completion after pending readers have stopped</returns>
        internal async Task CancelAsync()
        {
            if (_changing) return;
            InputEpoch++;
            _changing = true;
            UpdateResults();
            try { await StopQueueAsync(); }
            finally
            {
                _changing = false;
                if (_deferred.Count > 0 && !_closing)
                {
                    _queue = new HashQueue((string)_algorithm.SelectedItem, (int)_parallelism.SelectedItem,
                        _rows.Where(r => r.IsFile).Select(r => r.Path));
                    _queue.Add(_deferred.ToArray());
                }
                _deferred.Clear();
                UpdateResults();
            }
        }

        /// <summary>Changes rounds atomically so callbacks from the old algorithm cannot populate the new table</summary>
        /// <param name="clear">Whether to discard the source list as well</param>
        /// <returns>Completion after the replacement round is ready</returns>
        private async Task ChangeAsync(bool clear)
        {
            if (_changing || _closing) return;
            _changing = true;
            if (clear) { InputEpoch++; _sources.Clear(); _sourceSet.Clear(); _deferred.Clear(); }
            UpdateResults();
            try
            {
                await StopQueueAsync();
                _rows.Clear();
                SavePreferences();
                if (_sources.Count > 0)
                {
                    _queue = new HashQueue((string)_algorithm.SelectedItem, (int)_parallelism.SelectedItem);
                    _queue.Add(_sources.ToArray());
                }
                _deferred.Clear();
            }
            finally { _changing = false; UpdateResults(); }
        }

        /// <summary>Writes clipboard text through the existing retry-aware clipboard service</summary>
        /// <param name="text">Hash or checksum lines</param>
        private void Copy(string text)
        {
            if (text.Length == 0) return;
            if (!_copyText(text))
                MessageBox.Show(this, TranslationService.T("hash.clipboardError"), ProductInfo.Name);
            else
            {
                _copiedUntil = DateTime.UtcNow.AddMilliseconds(CopyFeedbackMilliseconds);
                UpdateResults();
            }
        }

        /// <summary>Cancels file operations before allowing the window and its server to exit</summary>
        /// <param name="args">Close request</param>
        protected override async void OnFormClosing(FormClosingEventArgs args)
        {
            if (!_canClose)
            {
                args.Cancel = true;
                if (!_closing)
                {
                    _closing = true;
                    InputEpoch++;
                    // Defer the second Close even when there is no asynchronous work left
                    await Task.Yield();
                    while (_changing) await Task.Delay(50);
                    await CancelAsync();
                    _canClose = true;
                    Close();
                }
            }
            base.OnFormClosing(args);
        }

        /// <summary>Releases the UI timer once all background readers have stopped</summary>
        /// <param name="disposing">Whether managed controls are being disposed</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refresh.Dispose();
                _grid.Cursor = Cursors.Default;
                _copyCursor?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
