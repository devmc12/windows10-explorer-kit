using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Shell;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Win10ExplorerKit.UI.Views.Pages;
using Win10ExplorerKit.UI.ViewModels;
namespace Win10ExplorerKit.UI.Views
{
    public partial class MainWindow : System.Windows.Window, IServiceProvider
    {
        private readonly Dictionary<Type, object> _pages = new Dictionary<Type, object>();
        private readonly SnackbarService _notifications = new SnackbarService();
        private bool _opening;
        internal bool AllowClose { get; set; }
        /// <summary>Builds a shared navigation shell with persistent page models</summary>
        /// <param name="hash">Background hashing state</param>
        /// <param name="settings">Settings and folder commands</param>
        internal MainWindow(HashViewModel hash, SettingsViewModel settings)
        {
            InitializeComponent();
            // Use the framework chrome after successful binding initialization
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                // Caption height in device-independent pixels, matching the visible title row
                CaptionHeight = 48,
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
                UseAeroCaptionButtons = false
            });
            _pages.Add(typeof(HashPage), new HashPage(hash));
            _pages.Add(typeof(FoldersPage), new FoldersPage(settings));
            _pages.Add(typeof(SettingsPage), new SettingsPage(settings));
            Navigation.SetServiceProvider(this);
            _notifications.SetSnackbarPresenter(Notifications);
            AllowDrop = true;
            PreviewDragOver += (sender, args) =>
            {
                args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) && (args.AllowedEffects & DragDropEffects.Copy) != 0
                    ? DragDropEffects.Copy : DragDropEffects.None;
                args.Handled = true;
            };
            PreviewDrop += (sender, args) =>
            {
                if ((args.AllowedEffects & DragDropEffects.Copy) != 0 && args.Data.GetDataPresent(DataFormats.FileDrop) && args.Data.GetData(DataFormats.FileDrop) is string[] paths)
                { Open("hash"); hash.AddPaths(paths); }
                args.Handled = true;
            };
            SizeChanged += (sender, args) =>
            {
                Navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                Navigation.IsPaneOpen = ActualWidth >= 900;
            };
        }
        /// <summary>Minimizes the shell using the standard window command</summary>
        /// <param name="sender">Caption button</param><param name="args">Click event</param>
        private void MinimizeWindow(object sender, RoutedEventArgs args) => SystemCommands.MinimizeWindow(this);
        /// <summary>Toggles the shell between normal and maximized states</summary>
        /// <param name="sender">Caption button</param><param name="args">Click event</param>
        private void ToggleMaximize(object sender, RoutedEventArgs args)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }
        /// <summary>Routes the caption close action through the hide-to-tray policy</summary>
        /// <param name="sender">Caption button</param><param name="args">Click event</param>
        private void CloseWindow(object sender, RoutedEventArgs args) => Close();
        /// <summary>Resolves the stable page instance requested by navigation</summary>
        /// <param name="serviceType">Requested page type</param>
        /// <returns>The matching page or null</returns>
        public object GetService(Type serviceType) => _pages.TryGetValue(serviceType, out object page) ? page : null;
        /// <summary>Shows and activates the requested tool without creating duplicate windows</summary>
        /// <param name="page">hash, folders or settings</param>
        internal void Open(string page)
        {
            if (_opening) return;
            _opening = true;
            try
            {
                if (!IsVisible) Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Navigation.Navigate(page == "settings" ? typeof(SettingsPage) : page == "folders" ? typeof(FoldersPage) : typeof(HashPage));
                Activate();
            }
            finally { _opening = false; }
        }
        /// <summary>Shows concise feedback using the shared Fluent snackbar</summary>
        /// <param name="message">Localized feedback</param>
        internal void Notify(string message) => _notifications.Show("", message, ControlAppearance.Secondary, null, TimeSpan.FromSeconds(4));
        /// <summary>Hides the shell while retaining active background work</summary>
        /// <param name="args">Window close request</param>
        protected override void OnClosing(CancelEventArgs args)
        {
            if (!AllowClose) { args.Cancel = true; Hide(); }
            base.OnClosing(args);
        }
    }
}
