using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Win10ExplorerKit.App.Activation;
using Win10ExplorerKit.UI.Services;
using Win10ExplorerKit.UI.ViewModels;
using Win10ExplorerKit.UI.Views;
using Win10ExplorerKit.Features.Permissions;
namespace Win10ExplorerKit.App
{
    public partial class DesktopApplication : Application
    {
        // Bound graceful worker shutdown below the installer's five-second host wait
        private const int ShutdownTimeoutMilliseconds = 3500;
        private readonly SettingsService _settings;
        private readonly SingleInstance _instance;
        private readonly bool _hidden;
        private readonly string _initialPage;
        private readonly bool _plain;
        private readonly DispatcherTimer _shutdown = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        private FolderRecoveryService _folders;
        private HashViewModel _hash;
        private PermissionViewModel _permissions;
        private PermissionInbox _permissionInbox;
        private SettingsViewModel _preferences;
        private TrayController _tray;
        private ExplorerHotkeyService _hotkey;
        private ExplorerCopyService _copy;
        private ActivationChannel _channel;
        private UI.Views.MainWindow _window;
        private bool _stopping;
        private bool _openingWindow;
        private bool _labelsPending;
        private bool _updatingLabels;
        private Task _labelRefresh = Task.CompletedTask;
        private string _displayLanguage;
        private string _displayTheme;
        /// <summary>Configures a dispatcher-owned host independently of visible windows</summary>
        /// <param name="settings">Persistent preferences</param><param name="instance">Session ownership</param><param name="hidden">Login startup mode</param><param name="page">Initial page</param><param name="plain">Legacy unquoted-path override</param>
        internal DesktopApplication(SettingsService settings, SingleInstance instance, bool hidden, string page, bool plain)
        {
            _settings = settings; _instance = instance; _hidden = hidden; _initialPage = page; _plain = plain;
            InitializeComponent();
            DispatcherUnhandledException += (sender, args) =>
            { AppLogger.Write("UI operation failed", args.Exception); args.Handled = true; Notify(TranslationService.T("error.operation")); };
        }
        /// <summary>Starts background services and opens the shell only for interactive launches</summary><param name="args">Startup arguments</param>
        protected override void OnStartup(StartupEventArgs args)
        {
            base.OnStartup(args); ApplyTheme();
            _displayLanguage = _settings.Language; _displayTheme = _settings.Theme;
            _folders = new FolderRecoveryService(_settings);
            _hash = new HashViewModel(_settings, Notify);
            _permissions = new PermissionViewModel(Notify);
            _permissionInbox = new PermissionInbox(request =>
            {
                if (request.Operation == "manage")
                {
                    if (!_permissions.AddPaths(request.Paths)) return false;
                    Open("permissions"); return true;
                }
                return _permissions.Enqueue(request);
            });
            _preferences = new SettingsViewModel(_settings, _folders, Notify);
            _tray = new TrayController(_settings, _preferences, Open, async () => await StopAsync());
            _copy = new ExplorerCopyService(Notify);
            _hotkey = new ExplorerHotkeyService(window => { _ = _copy.CopyAsync(window, _plain || !_settings.QuotePaths); },
                () => Notify(TranslationService.T("error.hotkey")));
            _channel = new ActivationChannel((request, token) => Dispatcher.InvokeAsync(() => Accept(request), DispatcherPriority.Background, token).Task);
            _settings.Changed += SettingsChanged;
            SystemEvents.UserPreferenceChanged += SystemChanged;
            _shutdown.Tick += async (sender, tick) => { _permissionInbox.Sweep(); if (_instance.StopRequested()) await StopAsync(); };
            _shutdown.Start();
            RefreshShellLabels();
            if (!_hidden) Open(_initialPage);
        }
        /// <summary>Accepts activation only while the application owns the session</summary><param name="request">Validated IPC request</param><returns>Accepted epoch or minus one when canceled</returns>
        private int Accept(ActivationRequest request)
        {
            if (_stopping) return -1;
            if (request.Action.EndsWith("-permissions", StringComparison.Ordinal)) return _permissionInbox.Receive(request);
            if (request.Action == "append-hash")
            {
                if (request.Epoch != _hash.InputEpoch) return -1;
                _hash.AddPaths(request.Paths); return _hash.InputEpoch;
            }
            Open(request.Action == "begin-hash" ? "hash" : request.Action);
            return _hash.InputEpoch;
        }
        /// <summary>Lazily creates the one visible navigation shell</summary><param name="page">Destination page</param>
        internal void Open(string page)
        {
            if (_stopping || _openingWindow) return;
            _openingWindow = true;
            try
            {
                if (_window == null) { _window = new UI.Views.MainWindow(_hash, _preferences, _permissions); MainWindow = _window; }
                _window.Open(page);
            }
            finally { _openingWindow = false; }
        }
        /// <summary>Routes feedback to the visible shell or the Windows notification area</summary><param name="message">Localized message</param>
        private void Notify(string message)
        {
            if (_stopping) return;
            if (_window?.IsVisible == true) _window.Notify(message); else _tray?.Notify(message);
        }
        /// <summary>Refreshes themes and localized bindings after committed changes</summary><param name="sender">Settings service</param><param name="args">Changed event</param>
        private void SettingsChanged(object sender, EventArgs args)
        {
            if (_stopping) return;
            if (_displayLanguage != _settings.Language)
            {
                _displayLanguage = _settings.Language;
                LocalizedText.Instance.Refresh(); _hash.RefreshLanguage(); _permissions.RefreshLanguage(); RefreshShellLabels();
            }
            if (_displayTheme != _settings.Theme) { _displayTheme = _settings.Theme; ApplyTheme(); }
        }
        /// <summary>Coalesces registry and shell notifications without making settings changes wait for Explorer</summary>
        private async void RefreshShellLabels()
        {
            _labelsPending = true;
            if (_updatingLabels) return;
            _updatingLabels = true;
            try
            {
                while (_labelsPending && !_stopping)
                {
                    _labelsPending = false;
                    try { _labelRefresh = Task.Run(ShellIntegration.RefreshLabels); await _labelRefresh; }
                    catch (Exception ex) { AppLogger.Write("Shell labels update failed", ex); }
                }
            }
            finally { _updatingLabels = false; }
        }
        /// <summary>Applies system preference changes on the dispatcher</summary><param name="sender">System event source</param><param name="args">Preference change</param>
        private void SystemChanged(object sender, UserPreferenceChangedEventArgs args)
        { if (!_stopping && _settings.Theme == "System") Dispatcher.BeginInvoke(new Action(ApplyTheme)); }
        /// <summary>Uses solid Fluent surfaces on all supported Windows 10 releases</summary>
        private void ApplyTheme()
        {
            bool dark = _settings.Theme == "Dark";
            if (_settings.Theme == "System")
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None);
            ThemePalette.Apply(dark);
        }
        /// <summary>Stops accepting requests before releasing files and native integrations</summary><returns>Completion after background work stops</returns>
        private async Task StopAsync()
        {
            if (_stopping) return; _stopping = true;
            _permissions.Stop();
            _hotkey?.Dispose(); _tray?.Dispose();
            _copy?.Dispose();
            _folders?.Dispose();
            _shutdown.Stop(); _channel?.Dispose();
            try
            {
                // Permission writes must finish before releasing installer ownership, even if a native call stalls
                // The installer times out and reports busy instead of replacing files during an ACL write
                await _permissions.Completion;
                Task cleanup = Task.WhenAll(_preferences.FlushAsync(), _hash.CloseAsync(), _folders.Completion,
                    _copy.Completion, _channel.Completion, _labelRefresh);
                if (await Task.WhenAny(cleanup, Task.Delay(ShutdownTimeoutMilliseconds)) == cleanup) await cleanup;
                else
                {
                    AppLogger.Write("Worker shutdown exceeded grace period", null);
                    _ = ObserveCleanupAsync(cleanup);
                }
            }
            catch (Exception ex) { AppLogger.Write("Application shutdown cleanup failed", ex); }
            finally
            {
                try { if (_window != null) { _window.AllowClose = true; _window.Close(); } }
                finally { Shutdown(); }
            }
        }
        /// <summary>Observes late worker failures without keeping an unresponsive shell process alive</summary>
        /// <param name="cleanup">Combined worker completion</param><returns>Completion after workers finish</returns>
        private static async Task ObserveCleanupAsync(Task cleanup)
        {
            try { await cleanup.ConfigureAwait(false); }
            catch (Exception ex) { AppLogger.Write("Late worker cleanup failed", ex); }
        }
        /// <summary>Disposes process-owned subscriptions and adapters</summary><param name="args">Exit details</param>
        protected override void OnExit(ExitEventArgs args)
        {
            _stopping = true; _shutdown.Stop(); _channel?.Dispose();
            SystemEvents.UserPreferenceChanged -= SystemChanged; _settings.Changed -= SettingsChanged;
            _copy?.Dispose(); _hotkey?.Dispose(); _tray?.Dispose(); _preferences?.Dispose(); _folders?.Dispose();
            base.OnExit(args);
        }
    }
}
