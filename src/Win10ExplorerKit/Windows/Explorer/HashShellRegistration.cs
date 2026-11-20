using System;
using System.IO;
using Microsoft.Win32;
using System.Windows.Forms;

namespace Win10ExplorerKit.Windows.Explorer
{
    internal static class HashShellRegistration
    {
        // Stable COM class identity used for DelegateExecute and LocalServer32
        internal const string ClassId = "{DA8D5701-BCAA-4760-B788-C4DB943CAADF}";
        // Retired AnyCPU activation identity whose cached architecture can cause launch timeouts
        private const string LegacyClassId = "{D9E1835A-DA54-488B-BC60-CE691468E49B}";
        // Per-user class location written explicitly into both registry views
        private const string ClassPath = @"Software\Classes\CLSID\" + ClassId;
        // Only file and directory selections expose the hash command
        private static readonly string[] MenuPaths = {
            @"Software\Classes\*\shell\Win10ExplorerKit.Hash",
            @"Software\Classes\Directory\shell\Win10ExplorerKit.Hash"
        };

        /// <summary>Selects the native host while retaining one package for both Windows architectures</summary>
        /// <param name="is64BitWindows">Whether the operating system is 64-bit</param>
        /// <returns>Host filename beside the application</returns>
        internal static string HostFileName(bool is64BitWindows)
        {
            return "Win10ExplorerKit.HashHost." + (is64BitWindows ? "x64" : "x86") + ".exe";
        }

        /// <summary>Enumerates independent per-user COM registry views</summary>
        /// <returns>Both views on 64-bit Windows, otherwise only the 32-bit view</returns>
        private static RegistryView[] Views()
        {
            return Environment.Is64BitOperatingSystem
                ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
                : new[] { RegistryView.Registry32 };
        }

        /// <summary>Registers the on-demand EXE server without changing login startup preferences</summary>
        internal static void Install()
        {
            string host = Path.Combine(AppPaths.DataDirectory, HostFileName(Environment.Is64BitOperatingSystem));
            if (!File.Exists(host)) throw new FileNotFoundException("Missing COM hash host");
            foreach (RegistryView view in Views())
            using (RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
            {
                using (RegistryKey key = user.CreateSubKey(ClassPath + @"\LocalServer32"))
                {
                    key.SetValue("", "\"" + host + "\"");
                    key.SetValue("ServerExecutable", host);
                }
                user.DeleteSubKeyTree(@"Software\Classes\CLSID\" + LegacyClassId, false);
            }
            foreach (string path in MenuPaths)
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
            {
                key.SetValue("Icon", "\"" + Application.ExecutablePath + "\",0");
                key.SetValue("MultiSelectModel", "Player");
                using (RegistryKey command = key.CreateSubKey("command"))
                    command.SetValue("DelegateExecute", ClassId);
            }
            RefreshLabels();
            NativeMethods.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Updates existing hash menu labels after language changes</summary>
        internal static void RefreshLabels()
        {
            foreach (string path in MenuPaths)
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true))
                key?.SetValue("MUIVerb", TranslationService.T("hash.menu"));
        }

        /// <summary>Removes only the hash verb and its owned COM activation class</summary>
        internal static void Uninstall()
        {
            foreach (string path in MenuPaths) Registry.CurrentUser.DeleteSubKeyTree(path, false);
            foreach (RegistryView view in Views())
            using (RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
            {
                user.DeleteSubKeyTree(ClassPath, false);
                user.DeleteSubKeyTree(@"Software\Classes\CLSID\" + LegacyClassId, false);
            }
            NativeMethods.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
