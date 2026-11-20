using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Win10ExplorerKit.UI.ViewModels;
using Forms = System.Windows.Forms;
namespace Win10ExplorerKit.UI.Tray
{
    internal sealed class TrayController : IDisposable
    {
        private readonly SettingsService _settings;
        private readonly SettingsViewModel _model;
        private readonly Action<string> _open;
        private readonly Action _exit;
        private readonly Forms.NotifyIcon _icon;
        private readonly Icon _image;
        private readonly ContextMenu _menu = new ContextMenu { Placement = PlacementMode.MousePoint, StaysOpen = false };
        /// <summary>Uses a native notification icon with an entirely WPF popup menu</summary>
        /// <param name="settings">Language preferences</param><param name="model">Shared settings commands</param><param name="open">Window navigation</param><param name="exit">Application shutdown</param>
        internal TrayController(SettingsService settings, SettingsViewModel model, Action<string> open, Action exit)
        {
            _settings = settings; _model = model; _open = open; _exit = exit;
            _image = Icon.ExtractAssociatedIcon(AppPaths.ExecutablePath);
            _icon = new Forms.NotifyIcon { Icon = _image, Text = ProductInfo.Name, Visible = true };
            _icon.MouseClick += (sender, args) =>
            {
                if (args.Button == Forms.MouseButtons.Left) { _menu.IsOpen = false; _open("hash"); }
                else if (args.Button == Forms.MouseButtons.Right) ShowMenu();
            };
            _menu.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuStyle");
            _menu.Opened += (sender, args) =>
            {
                // Activate the popup itself so there is no hidden owner in the taskbar
                if (PresentationSource.FromVisual(_menu) is HwndSource source) NativeMethods.SetForegroundWindow(source.Handle);
            };
            _settings.Changed += Refresh;
            Rebuild();
            // Prepare the popup template without showing a window or recreating it on every click
            _menu.ApplyTemplate();
            _menu.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        /// <summary>Opens the cached menu without waiting for double-click timing or rebuilding controls</summary>
        private void ShowMenu() { if (!_menu.IsOpen) _menu.IsOpen = true; }
        /// <summary>Creates a localized menu command</summary><param name="key">Locale key</param><param name="action">Command</param><returns>Menu entry</returns>
        private static MenuItem Item(string key, Action action)
        {
            var item = new MenuItem { Header = TranslationService.T(key).Replace("&", "") };
            item.Click += (sender, args) => action(); return item;
        }
        /// <summary>Updates open menu text after a locale change</summary><param name="sender">Settings service</param><param name="args">Changed event</param>
        private void Refresh(object sender, EventArgs args) { _menu.IsOpen = false; Rebuild(); }
        /// <summary>Builds the small set of tray commands without showing a window</summary>
        private void Rebuild()
        {
            _menu.Items.Clear();
            _menu.Items.Add(Item("tray.open", () => _open("hash")));
            _menu.Items.Add(Item("folders.restore", async () => await _model.OpenFoldersAsync(false)));
            _menu.Items.Add(Item("folders.openFixed", async () => await _model.OpenFoldersAsync(true)));
            _menu.Items.Add(new Separator());
            _menu.Items.Add(Item("settings.title", () => _open("settings")));
            var languages = new MenuItem { Header = TranslationService.T("tray.language").Replace("&", "") };
            foreach (string code in TranslationService.AvailableLanguages)
            {
                string selected = code;
                var item = new MenuItem { Header = TranslationService.LanguageName(code), IsCheckable = true, IsChecked = code == _settings.Language };
                item.Click += (sender, args) => _model.Language = selected;
                languages.Items.Add(item);
            }
            _menu.Items.Add(languages);
            _menu.Items.Add(Item("settings.openLogs", _model.OpenLogs));
            _menu.Items.Add(new Separator()); _menu.Items.Add(Item("tray.exit", _exit));
        }
        /// <summary>Reports background feedback without forcing open the main window</summary><param name="message">Localized message</param>
        internal void Notify(string message) => _icon.ShowBalloonTip(4000, ProductInfo.Name, message, Forms.ToolTipIcon.Info);
        /// <summary>Releases tray resources before dispatcher shutdown</summary>
        public void Dispose()
        {
            _settings.Changed -= Refresh; _menu.IsOpen = false;
            _icon.Visible = false; _icon.Dispose(); _image?.Dispose();
        }
    }
}
