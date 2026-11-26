// Defines semantic colors shared by application pages and dialogs
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
namespace Win10ExplorerKit.UI.Services
{
    internal static class ThemePalette
    {
        /// <summary>Applies a blue accent and layered surfaces while retaining Fluent control states</summary>
        /// <param name="dark">Whether the active appearance is dark</param>
        internal static void Apply(bool dark)
        {
            ApplicationAccentColorManager.Apply(Color.FromRgb(37, 99, 235), dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
            Set("KitShellBrush", dark ? "#111827" : "#EEF3FC");
            Set("KitSurfaceBrush", dark ? "#182234" : "#FFFFFF");
            Set("KitHeaderBrush", dark ? "#22324B" : "#E7EFFC");
            Set("KitBorderBrush", dark ? "#34435B" : "#CEDCF0");
            Set("KitTableSelectionBrush", dark ? "#203047" : "#EFF5FE");
            Set("KitAccentSoftBrush", dark ? "#253F65" : "#DBEAFE");
            Set("KitAccentTextBrush", dark ? "#93C5FD" : "#1D4ED8");
            Set("KitToastBrush", dark ? "#193451" : "#E8F1FE");
            Set("KitToastBorderBrush", dark ? "#42658B" : "#93B8EE");
            Set("KitToastTextBrush", dark ? "#CFE4FF" : "#164780");
            Set("KitFolderBrush", dark ? "#FBBF24" : "#B45309");
        }
        /// <summary>Replaces a semantic brush so existing views follow theme switches</summary>
        /// <param name="key">Shared resource key</param><param name="hex">Theme color</param>
        private static void Set(string key, string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
    }
}
