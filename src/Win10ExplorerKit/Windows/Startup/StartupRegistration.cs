using System;
using Microsoft.Win32;

namespace Win10ExplorerKit.Windows.Startup
{
    internal sealed class StartupRegistration
    {
        // Per-user Run entries launch after sign-in without elevation
        private const string DefaultRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Stable entry shared by the installer and portable integration
        private const string ValueName = "Win10ExplorerKitHotkey";
        private readonly string _registryPath;
        private readonly string _command;

        /// <summary>
        /// Uses Windows registration as the source of truth instead of duplicating it in INI
        /// </summary>
        /// <param name="executablePath">Executable to launch after user sign-in</param>
        /// <param name="registryPath">Optional isolated current-user key for tests</param>
        public StartupRegistration(string executablePath, string registryPath = DefaultRegistryPath)
        {
            _registryPath = registryPath;
            _command = "\"" + executablePath + "\" --hotkey";
        }

        /// <summary>
        /// Reads whether this executable has a matching login registration
        /// </summary>
        /// <returns>True when the current executable is registered</returns>
        public bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_registryPath))
                return key != null && string.Equals(key.GetValue(ValueName) as string, _command, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Updates only the application's current-user login entry
        /// </summary>
        /// <param name="enabled">Whether to register or remove login startup</param>
        public void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_registryPath))
                    key.SetValue(ValueName, _command, RegistryValueKind.String);
            }
            else
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_registryPath, true))
                    if (key != null) key.DeleteValue(ValueName, false);
            }
        }
    }
}
