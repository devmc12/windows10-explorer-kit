using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Win10ExplorerKit.UI.Forms.Settings;

namespace Win10ExplorerKit.UI.Tray
{
    internal sealed class TrayController : IDisposable
    {
        private readonly SettingsService _settings;
        private readonly FolderRecoveryService _folders;
        private readonly Timer _leftClick = new Timer { Interval = SystemInformation.DoubleClickTime };
        private readonly ToolStripMenuItem _restore = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _openFixed = new ToolStripMenuItem();
        private bool _disposed;
        private readonly NotifyIcon _icon;
        private readonly Icon _image;
        private readonly ContextMenuStrip _menu = new TrayContextMenu();
        private readonly ToolStripMenuItem _status = new ToolStripMenuItem { Enabled = false };
        private readonly ToolStripMenuItem _languages = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _settingsItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _log = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _exit = new ToolStripMenuItem();
        private readonly Dictionary<string, ToolStripMenuItem> _languageItems = new Dictionary<string, ToolStripMenuItem>();
        private SettingsForm _settingsForm;

        /// <summary>
        /// Builds native menus and subscribes to settings changes shared with GUI windows
        /// </summary>
        /// <param name="settings">Configuration service shared by all user interfaces</param>
        /// <param name="exit">Application-level exit callback</param>
        /// <param name="folders">Folder monitoring and restore commands</param>
        public TrayController(SettingsService settings, Action exit, FolderRecoveryService folders)
        {
            _settings = settings;
            _folders = folders;
            _restore.Click += (sender, e) => OpenFolders(false);
            _openFixed.Click += (sender, e) => OpenFolders(true);
            _languages.DropDown = new TrayContextMenu();
            foreach (string code in TranslationService.AvailableLanguages)
            {
                string selected = code;
                var item = new ToolStripMenuItem(TranslationService.LanguageName(code), null, (sender, e) =>
                {
                    try { _settings.SetLanguage(selected); }
                    catch (Exception ex) { AppLogger.Write("Language update failed", ex); MessageBox.Show(TranslationService.T("error.language"), ProductInfo.Name); }
                });
                _languageItems.Add(code, item);
                _languages.DropDownItems.Add(item);
            }
            _settingsItem.Click += (sender, e) => ShowSettings();
            _log.Click += (sender, e) =>
            {
                System.IO.Directory.CreateDirectory(AppLogger.LogDirectory);
                Process.Start(new ProcessStartInfo(AppLogger.LogDirectory) { UseShellExecute = true });
            };
            _exit.Click += (sender, e) => exit();
            _menu.Items.AddRange(new ToolStripItem[] { _status, _restore, _openFixed, new ToolStripSeparator(), _settingsItem, _languages, _log, new ToolStripSeparator(), _exit });
            _image = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
            _icon = new NotifyIcon { Icon = _image, Text = ProductInfo.Name, ContextMenuStrip = _menu, Visible = true };
            _leftClick.Tick += (sender, e) =>
            {
                _leftClick.Stop();
                _menu.Show(Cursor.Position);
                NativeMethods.SetForegroundWindow(_menu.Handle);
            };
            _menu.Opening += (sender, e) => _leftClick.Stop();
            _icon.MouseClick += ShowMenu;
            _icon.MouseDoubleClick += (sender, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _leftClick.Stop();
                OpenFolders(false);
            };
            _settings.Changed += SettingsChanged;
            RefreshText();
        }

        /// <summary>
        /// Opens the shared tray menu on a left click and activates it for dismissal outside the app
        /// </summary>
        /// <param name="sender">Tray notification icon</param>
        /// <param name="args">Mouse button and pointer information</param>
        private void ShowMenu(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
            _leftClick.Start();
        }

        /// <summary>Runs a tray command and reports empty or unavailable folders</summary>
        /// <param name="fixedList">Use the configured list instead of a snapshot</param>
        private async void OpenFolders(bool fixedList)
        {
            string status = await _folders.OpenAsync(fixedList);
            if (!_disposed && status != null) _icon.ShowBalloonTip(4000, ProductInfo.Name, status, ToolTipIcon.Info);
        }

        /// <summary>
        /// Opens one settings window and activates the existing instance on repeated requests
        /// </summary>
        public void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed) _settingsForm = new SettingsForm(_settings);
            _settingsForm.Show();
            _settingsForm.Activate();
        }

        /// <summary>
        /// Updates shell labels and tray text after a committed configuration change
        /// </summary>
        /// <param name="sender">Settings service raising the event</param>
        /// <param name="args">Event arguments</param>
        private void SettingsChanged(object sender, EventArgs args)
        {
            RefreshText();
            try { ShellIntegration.RefreshLabels(); }
            catch (Exception ex) { AppLogger.Write("Shell labels could not be refreshed", ex); }
        }

        /// <summary>
        /// Applies the active catalog without rebuilding menus or losing keyboard focus
        /// </summary>
        private void RefreshText()
        {
            _status.Text = TranslationService.T("tray.status");
            _languages.Text = TranslationService.T("tray.language");
            _restore.Text = TranslationService.T("folders.restore");
            _openFixed.Text = TranslationService.T("folders.openFixed");
            _settingsItem.Text = TranslationService.T("tray.settings");
            _log.Text = TranslationService.T("tray.openLog");
            _exit.Text = TranslationService.T("tray.exit");
            foreach (var pair in _languageItems) pair.Value.Checked = pair.Key == _settings.Language;
        }

        /// <summary>
        /// Displays a localized notification when another program owns the shortcut
        /// </summary>
        public void ShowHotkeyConflict() { _icon.ShowBalloonTip(4000, ProductInfo.Name, TranslationService.T("error.hotkey"), ToolTipIcon.Warning); }

        /// <summary>
        /// Unsubscribes from shared services and releases all UI resources
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            _leftClick.Dispose();
            _settings.Changed -= SettingsChanged;
            if (_settingsForm != null) _settingsForm.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _image.Dispose();
            _menu.Dispose();
        }
    }
}
