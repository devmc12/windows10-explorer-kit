using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
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
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private ContextMenu _menu;
        private bool _clickPending;
        private bool _disposed;
        /// <summary>Uses a native notification icon with an entirely WPF popup menu</summary>
        /// <param name="settings">Language preferences</param><param name="model">Shared settings commands</param><param name="open">Window navigation</param><param name="exit">Application shutdown</param>
        internal TrayController(SettingsService settings, SettingsViewModel model, Action<string> open, Action exit)
        {
            _settings = settings; _model = model; _open = open; _exit = exit;
            _image = Icon.ExtractAssociatedIcon(AppPaths.ExecutablePath);
            _icon = new Forms.NotifyIcon { Icon = _image, Text = ProductInfo.Name, Visible = true };
            _icon.MouseClick += (sender, args) => QueueClick(args.Button);
            _settings.Changed += Refresh;
        }
        /// <summary>Leaves the native callback before creating or activating WPF visual roots</summary>
        /// <param name="button">Mouse button reported by the notification icon</param>
        private void QueueClick(Forms.MouseButtons button)
        {
            if (_disposed || _clickPending || (button != Forms.MouseButtons.Left && button != Forms.MouseButtons.Right)) return;
            _clickPending = true;
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    if (_disposed) return;
                    if (button == Forms.MouseButtons.Left) { CloseMenu(); _open("hash"); }
                    else ShowMenu();
                }
                finally { _clickPending = false; }
            }));
        }
        /// <summary>Creates a fresh popup tree after the previous menu has been dismissed</summary>
        private void ShowMenu()
        {
            if (_menu?.IsOpen == true) return;
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint, StaysOpen = false };
            menu.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuStyle");
            menu.Opened += (sender, args) =>
            {
                // Activate only after WPF has finished attaching the popup root
                _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (!_disposed && menu.IsOpen && PresentationSource.FromVisual(menu) is HwndSource source)
                        NativeMethods.SetForegroundWindow(source.Handle);
                }));
            };
            menu.Closed += (sender, args) => { if (ReferenceEquals(_menu, menu)) _menu = null; };
            BuildMenu(menu);
            _menu = menu;
            menu.IsOpen = true;
        }
        /// <summary>Dismisses the current popup without reusing its visual root</summary>
        private void CloseMenu()
        {
            if (_menu != null) _menu.IsOpen = false;
            _menu = null;
        }
        /// <summary>Creates a localized menu command</summary><param name="key">Locale key</param><param name="action">Command</param><returns>Menu entry</returns>
        private MenuItem Item(string key, Action action)
        {
            var item = new MenuItem { Header = TranslationService.T(key).Replace("&", "") };
            item.Click += (sender, args) => QueueCommand(action); return item;
        }
        /// <summary>Runs commands after popup input and dismissal have finished</summary>
        /// <param name="action">Selected tray command</param>
        private void QueueCommand(Action action)
        {
            CloseMenu();
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { if (!_disposed) action(); }));
        }
        /// <summary>Updates open menu text after a locale change</summary><param name="sender">Settings service</param><param name="args">Changed event</param>
        private void Refresh(object sender, EventArgs args) => CloseMenu();
        /// <summary>Builds the small set of tray commands without showing a window</summary>
        /// <param name="menu">Popup that owns these menu items</param>
        private void BuildMenu(ContextMenu menu)
        {
            menu.Items.Add(Item("tray.open", () => _open("hash")));
            menu.Items.Add(Item("folders.restore", async () => await _model.OpenFoldersAsync(false)));
            menu.Items.Add(Item("folders.openFixed", async () => await _model.OpenFoldersAsync(true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("settings.title", () => _open("settings")));
            var languages = new MenuItem { Header = TranslationService.T("tray.language").Replace("&", "") };
            foreach (string code in TranslationService.AvailableLanguages)
            {
                string selected = code;
                var item = new MenuItem { Header = TranslationService.LanguageName(code), IsCheckable = true, IsChecked = code == _settings.Language };
                item.Click += (sender, args) => QueueCommand(() => _model.Language = selected);
                languages.Items.Add(item);
            }
            menu.Items.Add(languages);
            menu.Items.Add(Item("settings.openLogs", _model.OpenLogs));
            menu.Items.Add(new Separator()); menu.Items.Add(Item("tray.exit", _exit));
        }
        /// <summary>Reports background feedback without forcing open the main window</summary><param name="message">Localized message</param>
        internal void Notify(string message) => _icon.ShowBalloonTip(4000, ProductInfo.Name, message, Forms.ToolTipIcon.Info);
        /// <summary>Releases tray resources before dispatcher shutdown</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _settings.Changed -= Refresh; CloseMenu();
            _icon.Visible = false; _icon.Dispose(); _image?.Dispose();
        }
    }
}
