using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using Win10ExplorerKit.App.Activation;
using Win10ExplorerKit.UI.Services;
using System.Runtime.InteropServices;
using Win10ExplorerKit.App.Hashing;

namespace Win10ExplorerKit
{
    internal static class Program
    {
        // Maximum final log flush in milliseconds, kept below the installer shutdown deadline
        private const int LogFlushMilliseconds = 500;
        /// <summary>Flushes diagnostics before releasing host ownership without waiting indefinitely for storage</summary>
        private static void FlushDiagnostics()
        {
            try { AppLogger.FlushAsync().Wait(LogFlushMilliseconds); }
            catch (AggregateException) { }
        }
        /// <summary>Stops both tray and on-demand hash processes before maintenance</summary>
        /// <returns>Whether all owned processes released their files</returns>
        private static bool StopHosts()
        {
            bool hash = SingleInstance.RequestStop("Hash");
            bool tray = SingleInstance.RequestStop();
            return tray && hash;
        }

        /// <summary>
        /// Routes maintenance and copy commands before entering the tray message loop
        /// </summary>
        /// <param name="args">Command-line switches and selected paths</param>
        /// <returns>Zero on success, one for invalid input, or two on failure</returns>
        [STAThread]
        internal static int Main(string[] args)
        {
            bool diagnosticsFlushed = false;
            try
            {
                if (Array.IndexOf(args, "--shutdown") >= 0) return StopHosts() ? 0 : 2;
                if (Array.IndexOf(args, "--uninstall") >= 0)
                {
                    if (!StopHosts()) return 2;
                    ShellIntegration.Uninstall();
                    DataCleanup.Clean(AppPaths.DataDirectory);
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--clean-data")
                {
                    if (!StopHosts()) return 2;
                    HashShellRegistration.Uninstall();
                    DataCleanup.Clean(AppPaths.DataDirectory);
                    return 0;
                }
                bool hashServer = Array.IndexOf(args, "--hash-server") >= 0;
                if (hashServer) AppLogger.Write("Hash host starting", null);
                bool initialize = args.Length == 2 && args[0] == "--initialize-language";
                SettingsService settings = SettingsService.Load(initialize ? args[1] : null);
                if (initialize) { ShellIntegration.RefreshLabels(); return 0; }
                if (args.Length == 1 && args[0] == "--register-hash")
                {
                    HashShellRegistration.Install();
                    return 0;
                }
                if (hashServer)
                {
                    Marshal.ThrowExceptionForHR(HashComNative.OleInitialize(IntPtr.Zero));
                    try
                    {
                        using (var owner = new SingleInstance("Hash"))
                        {
                            if (!owner.IsOwner) { AppLogger.Write("Hash host already running", null); return 0; }
                            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                            using (var host = new HashApplicationContext(settings, owner)) Dispatcher.Run();
                        }
                    }
                    finally { HashComNative.OleUninitialize(); AppLogger.Write("Hash host stopped", null); }
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--set-language")
                {
                    settings.SetLanguage(args[1]);
                    ShellIntegration.RefreshLabels();
                    return 0;
                }
                if (Array.IndexOf(args, "--install") >= 0)
                {
                    if (Array.IndexOf(args, "--plain") >= 0) settings.SetQuotePaths(false);
                    ShellIntegration.Install();
                    return 0;
                }
                bool plain = Array.IndexOf(args, "--plain") >= 0 || !settings.QuotePaths;
                var paths = new List<string>();
                foreach (string arg in args)
                {
                    if (arg == "--copy" || arg == "--plain" || arg == "--hotkey" || arg == "--settings" || arg == "%*") continue;
                    if (arg.StartsWith("--", StringComparison.Ordinal)) return 1;
                    paths.Add(arg);
                }
                if (paths.Count > 0) return CopyPathService.CopyFromShell(paths, plain) ? 0 : 2;
                if (Array.IndexOf(args, "--copy") >= 0) return 1;
                using (var instance = new SingleInstance())
                {
                    if (!instance.IsOwner)
                    {
                        if (Array.IndexOf(args, "--hotkey") < 0)
                            ActivationChannel.SendAsync(new ActivationRequest { Action = Array.IndexOf(args, "--settings") >= 0 ? "settings" : "hash" }, false).GetAwaiter().GetResult();
                        return 0;
                    }
                    PortableSetup.EnsureRegistered(AppDomain.CurrentDomain.BaseDirectory,
                        SettingsService.FilePath, AppPaths.ExecutablePath, ShellIntegration.Install, HashShellRegistration.Install);
                    var app = new DesktopApplication(settings, instance, Array.IndexOf(args, "--hotkey") >= 0,
                        Array.IndexOf(args, "--settings") >= 0 ? "settings" : "hash", Array.IndexOf(args, "--plain") >= 0);
                    int exitCode = app.Run();
                    // Keep the installer waiting on host ownership until the final normal flush finishes
                    FlushDiagnostics();
                    diagnosticsFlushed = true;
                    return exitCode;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Write("Process failed", ex);
                if (Array.IndexOf(args, "--install") >= 0 || args.Length == 0)
                    DialogService.ShowBlocking(TranslationService.T("error.operation"));
                return 2;
            }
            finally
            {
                if (!diagnosticsFlushed) FlushDiagnostics();
            }
        }
    }
}
