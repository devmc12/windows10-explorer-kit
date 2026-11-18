using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Win10ExplorerKit
{
    internal static class Program
    {
        /// <summary>
        /// Routes maintenance and copy commands before entering the tray message loop
        /// </summary>
        /// <param name="args">Command-line switches and selected paths</param>
        /// <returns>Zero on success, one for invalid input, or two on failure</returns>
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (Array.IndexOf(args, "--shutdown") >= 0) return SingleInstance.RequestStop() ? 0 : 2;
                if (Array.IndexOf(args, "--uninstall") >= 0)
                {
                    if (!SingleInstance.RequestStop()) return 2;
                    ShellIntegration.Uninstall();
                    DataCleanup.Clean(AppPaths.DataDirectory);
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--clean-data")
                {
                    DataCleanup.Clean(AppPaths.DataDirectory);
                    return 0;
                }
                bool initialize = args.Length == 2 && args[0] == "--initialize-language";
                SettingsService settings = SettingsService.Load(initialize ? args[1] : null);
                if (initialize) { ShellIntegration.RefreshLabels(); return 0; }
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
                        SettingsService.FilePath, Application.ExecutablePath, ShellIntegration.Install);
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
