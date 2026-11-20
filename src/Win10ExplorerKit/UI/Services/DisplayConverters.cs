using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
namespace Win10ExplorerKit.UI.Services
{
    internal sealed class HashCursorConverter : IValueConverter
    {
        /// <summary>Uses the hand cursor only for cells with a completed hash</summary><param name="value">Copy availability</param><param name="targetType">Cursor type</param><param name="parameter">Unused</param><param name="culture">Current culture</param><returns>Pointer cursor</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool ready && ready ? Cursors.Hand : Cursors.Arrow;
        /// <summary>Rejects reverse conversion</summary><param name="value">Unused</param><param name="targetType">Unused</param><param name="parameter">Unused</param><param name="culture">Unused</param><returns>No source update</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    internal sealed class FolderDepthConverter : IMultiValueConverter
    {
        /// <summary>Displays numeric levels and translates the unlimited option when the language changes</summary>
        /// <param name="values">Depth and locale refresh binding</param><param name="targetType">Display type</param>
        /// <param name="parameter">Unused</param><param name="culture">Current culture</param><returns>The depth label</returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(values[0] is int depth)) return "";
            return depth == 0 ? TranslationService.T("hash.allDepths") : depth.ToString(culture);
        }
        /// <summary>Leaves the numeric depth selection unchanged by display conversion</summary>
        /// <param name="value">Unused</param><param name="targetTypes">Unused</param><param name="parameter">Unused</param>
        /// <param name="culture">Unused</param><returns>No source updates</returns>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }
    internal sealed class LanguageNameConverter : IValueConverter
    {
        /// <summary>Displays each locale in its own language</summary><param name="value">Locale code</param><param name="targetType">Text type</param><param name="parameter">Unused</param><param name="culture">Current culture</param><returns>Native language name</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => TranslationService.LanguageName((string)value);
        /// <summary>Rejects reverse conversion</summary><param name="value">Unused</param><param name="targetType">Unused</param><param name="parameter">Unused</param><param name="culture">Unused</param><returns>No source update</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    internal sealed class ThemeNameConverter : IMultiValueConverter
    {
        /// <summary>Refreshes translated theme labels with the locale binding</summary><param name="values">Theme code and locale trigger</param><param name="targetType">Text type</param><param name="parameter">Unused</param><param name="culture">Current culture</param><returns>Localized theme label</returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => TranslationService.T("theme." + values[0]);
        /// <summary>Rejects reverse conversion</summary><param name="value">Unused</param><param name="targetTypes">Unused</param><param name="parameter">Unused</param><param name="culture">Unused</param><returns>No source updates</returns>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }
}
