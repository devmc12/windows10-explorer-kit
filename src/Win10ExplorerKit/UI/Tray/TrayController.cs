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
        public TrayController(SettingsService settings, Action exit)
        {
            _settings = settings;
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
            _log.Click += (sender, e) => Process.Start("notepad.exe", "\"" + AppLogger.FilePath + "\"");
            _exit.Click += (sender, e) => exit();
            _menu.Items.AddRange(new ToolStripItem[] { _status, _settingsItem, _languages, _log, new ToolStripSeparator(), _exit });
            _image = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
            _icon = new NotifyIcon { Icon = _image, Text = ProductInfo.Name, ContextMenuStrip = _menu, Visible = true };
            _icon.MouseClick += ShowMenu;
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
            _menu.Show(Cursor.Position);
            NativeMethods.SetForegroundWindow(_menu.Handle);
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
            _settings.Changed -= SettingsChanged;
            if (_settingsForm != null) _settingsForm.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _image.Dispose();
            _menu.Dispose();
        }
    }
}
