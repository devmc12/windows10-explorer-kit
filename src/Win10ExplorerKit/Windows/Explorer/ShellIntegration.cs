using System;
using Microsoft.Win32;
using Win10ExplorerKit.Windows.Startup;

namespace Win10ExplorerKit.Windows.Explorer
{
    internal static class ShellIntegration
    {
        // Registry locations for file, directory and drive verbs in the current account
        private static readonly string[] MenuRoots = {
        @"Software\Classes\*\shell\Win10ExplorerKit",
        @"Software\Classes\Directory\shell\Win10ExplorerKit",
        @"Software\Classes\Drive\shell\Win10ExplorerKit"
    };
        // Shell association-change event used after updating registered verbs
        private const uint AssociationChanged = 0x08000000;

        /// <summary>
        /// Registers per-user verbs and login startup for the executable at its current path
        /// </summary>
        public static void Install()
        {
            string quotedExe = "\"" + AppPaths.ExecutablePath + "\"";
            foreach (string path in MenuRoots)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
                {
                    key.SetValue("Icon", quotedExe + ",0");
                    key.SetValue("MultiSelectModel", "Player");
                    using (RegistryKey command = key.CreateSubKey("command"))
                    {
                        // Keep the single-item fallback for shells that do not expand %*
                        command.SetValue("", quotedExe + " --copy %* \"%1\"");
                    }
                }
            }
            HashShellRegistration.Install();
            new StartupRegistration(AppPaths.ExecutablePath).SetEnabled(true);
            RefreshLabels();
        }

        /// <summary>
        /// Updates only existing verbs so portable use does not silently install menus
        /// </summary>
        public static void RefreshLabels()
        {
            HashShellRegistration.RefreshLabels();
            string label = TranslationService.T("shell.copyPaths");
            foreach (string path in MenuRoots)
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true))
                {
                    if (key == null) continue;
                    key.SetValue("", label);
                    key.SetValue("MUIVerb", label);
                }
            }
            NativeMethods.SHChangeNotify(AssociationChanged, 0, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Removes owned verbs and login startup while retaining the language preference
        /// </summary>
        public static void Uninstall()
        {
            HashShellRegistration.Uninstall();
            foreach (string path in MenuRoots)
            {
                Registry.CurrentUser.DeleteSubKeyTree(path, false);
            }
            new StartupRegistration(AppPaths.ExecutablePath).SetEnabled(false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Win10ExplorerKit", false);
            NativeMethods.SHChangeNotify(AssociationChanged, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }

}
