using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Win10ExplorerKit.App.Hashing;

namespace Win10ExplorerKit
{
    internal static class Program
    {
        /// <summary>Stops both tray and on-demand hash processes before maintenance</summary>
        /// <returns>Whether all owned processes released their files</returns>
        private static bool StopHosts()
        {
            bool tray = SingleInstance.RequestStop();
            bool hash = SingleInstance.RequestStop("Hash");
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
                            Application.EnableVisualStyles();
                            Application.SetCompatibleTextRenderingDefault(false);
                            using (var host = new HashApplicationContext(settings, owner)) Application.Run(host);
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
                    if (!instance.IsOwner) return 0;
                    PortableSetup.EnsureRegistered(AppDomain.CurrentDomain.BaseDirectory,
                        SettingsService.FilePath, Application.ExecutablePath, ShellIntegration.Install, HashShellRegistration.Install);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += (sender, e) => AppLogger.Write("UI event failed", e.Exception);
                    using (var context = new TrayApplicationContext(settings, instance,
                        Array.IndexOf(args, "--settings") >= 0, Array.IndexOf(args, "--plain") >= 0)) Application.Run(context);
                }
                return 0;
            }
            catch (Exception ex)
            {
                AppLogger.Write("Process failed", ex);
                if (Array.IndexOf(args, "--install") >= 0 || args.Length == 0)
                    MessageBox.Show(TranslationService.T("error.setup", ex.Message), ProductInfo.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }
    }
}
