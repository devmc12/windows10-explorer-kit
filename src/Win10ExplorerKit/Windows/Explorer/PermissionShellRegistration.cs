// Registers and removes the per-user permission submenu
using System;
using System.IO;
using Microsoft.Win32;

namespace Win10ExplorerKit.Windows.Explorer
{
    internal static class PermissionShellRegistration
    {
        // Stable COM identities distinguish shell actions without accepting command-line operations
        internal static readonly string[] ClassIds = {
            "{B3DF749C-F945-4B74-A9DF-791F02615C11}",
            "{B3DF749C-F945-4B74-A9DF-791F02615C12}",
            "{B3DF749C-F945-4B74-A9DF-791F02615C13}" };
        internal static readonly string[] Operations = { "deny", "allow", "manage" };
        // A per-user static cascade is visible for selections only, never drive or directory backgrounds
        private static readonly string[] Roots = { @"Software\Classes\*\shell\Win10ExplorerKit.Permissions",
            @"Software\Classes\Directory\shell\Win10ExplorerKit.Permissions" };

        /// <summary>Registers lightweight multi-selection commands in the existing native host</summary>
        internal static void Install()
        {
            string host = Path.Combine(AppPaths.DataDirectory, HashShellRegistration.HostFileName(Environment.Is64BitOperatingSystem));
            foreach (RegistryView view in Views())
            using (var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                foreach (string id in ClassIds)
                    using (var key = user.CreateSubKey(@"Software\Classes\CLSID\" + id + @"\LocalServer32"))
                    { key.SetValue("", "\"" + host + "\""); key.SetValue("ServerExecutable", host); }
            foreach (string root in Roots)
            using (var parent = Registry.CurrentUser.CreateSubKey(root))
            {
                parent.SetValue("SubCommands", "");
                parent.SetValue("Icon", "\"" + AppPaths.ExecutablePath + "\",0");
                for (int index = 0; index < Operations.Length; index++)
                using (var verb = parent.CreateSubKey("shell\\" + index + Operations[index]))
                {
                    verb.SetValue("MultiSelectModel", "Player");
                    // ECF_SEPARATORBEFORE separates management from direct operations
                    if (index == 2) verb.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord);
                    using (var command = verb.CreateSubKey("command")) command.SetValue("DelegateExecute", ClassIds[index]);
                }
            }
            RefreshLabels();
            NativeMethods.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Refreshes only already-installed permission menu labels</summary>
        internal static void RefreshLabels()
        {
            foreach (string root in Roots)
            using (var parent = Registry.CurrentUser.OpenSubKey(root, true))
            {
                if (parent == null) continue;
                parent.SetValue("MUIVerb", TranslationService.T("permissions.title"));
                for (int index = 0; index < Operations.Length; index++)
                using (var verb = parent.OpenSubKey("shell\\" + index + Operations[index], true))
                    verb?.SetValue("MUIVerb", TranslationService.T("permissions." + Operations[index]));
            }
        }

        /// <summary>Removes only the permission cascade and its owned COM identities</summary>
        internal static void Uninstall()
        {
            foreach (string root in Roots) Registry.CurrentUser.DeleteSubKeyTree(root, false);
            foreach (RegistryView view in Views())
            using (var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                foreach (string id in ClassIds) user.DeleteSubKeyTree(@"Software\Classes\CLSID\" + id, false);
        }

        /// <summary>Matches the native COM registration views used by Explorer</summary>
        /// <returns>Available registry views</returns>
        private static RegistryView[] Views() => Environment.Is64BitOperatingSystem ?
            new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 };
    }
}
