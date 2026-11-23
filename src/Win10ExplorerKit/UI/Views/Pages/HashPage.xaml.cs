using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Win10ExplorerKit.UI.ViewModels;
namespace Win10ExplorerKit.UI.Views.Pages
{
    public partial class HashPage : Page
    {
        private readonly HashViewModel _model;
        /// <summary>Connects file interaction to the shared hashing model</summary>
        /// <param name="model">State retained across navigation</param>
        internal HashPage(HashViewModel model) { _model = model; InitializeComponent(); DataContext = model; }
        /// <summary>Queues selected files from the native picker</summary><param name="sender">Button</param><param name="args">Click</param>
        private void AddFiles(object sender, RoutedEventArgs args)
        {
            var picker = new OpenFileDialog { Multiselect = true, CheckFileExists = true };
            if (picker.ShowDialog(Window.GetWindow(this)) == true) _model.AddPaths(picker.FileNames);
        }
        /// <summary>Offers copy semantics only for filesystem drops</summary><param name="sender">Drop surface</param><param name="args">Drag data</param>
        private void DragOverFiles(object sender, DragEventArgs args)
        {
            args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) && (args.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        }
        /// <summary>Queues files and directories without foreground enumeration</summary><param name="sender">Drop surface</param><param name="args">Drop data</param>
        private void DropFiles(object sender, DragEventArgs args)
        {
            DragOverFiles(sender, args);
            if (args.Effects == DragDropEffects.Copy && args.Data.GetData(DataFormats.FileDrop) is string[] paths) _model.AddPaths(paths);
        }
        /// <summary>Shows the complete filename only when the rendered text is truncated</summary>
        /// <param name="sender">Filename text element</param><param name="args">Tooltip opening request</param>
        private void FileNameToolTipOpening(object sender, ToolTipEventArgs args)
        {
            var text = (TextBlock)sender;
            var formatted = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
                text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            args.Handled = formatted.WidthIncludingTrailingWhitespace <= text.ActualWidth;
        }
        /// <summary>Copies a completed hash from any point inside its cell</summary><param name="sender">Hash cell</param><param name="args">Pointer event</param>
        private async void CopyValue(object sender, MouseButtonEventArgs args)
        {
            if (((FrameworkElement)sender).DataContext is HashRowViewModel row && row.CanCopy) { args.Handled = true; await _model.CopyAsync(row); }
        }
        /// <summary>Supports keyboard copying of the selected result</summary><param name="sender">Results table</param><param name="args">Key event</param>
        private async void CopyKey(object sender, KeyEventArgs args)
        {
            if ((args.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) || (args.Key == Key.Enter && Results.CurrentColumn == HashValueColumn))
            { args.Handled = true; if (Results.SelectedItem is HashRowViewModel row) await _model.CopyAsync(row); }
        }
        /// <summary>Starts a new algorithm round</summary><param name="sender">Selector</param><param name="args">Selection event</param>
        private async void AlgorithmChanged(object sender, SelectionChangedEventArgs args)
        { if (Algorithm.SelectedItem is string value && value != _model.Algorithm) await _model.ChangeAsync(false, value); }
        /// <summary>Changes idle reader concurrency</summary><param name="sender">Selector</param><param name="args">Selection event</param>
        private async void ParallelismChanged(object sender, SelectionChangedEventArgs args)
        { if (Parallelism.SelectedItem is int value && value != _model.Parallelism) await _model.SetParallelismAsync(value); }
        /// <summary>Saves the directory discovery depth for subsequent inputs</summary>
        /// <param name="sender">Depth selector</param><param name="args">Selection event</param>
        private async void FolderDepthChanged(object sender, SelectionChangedEventArgs args)
        {
            if (FolderDepth.SelectedItem is int depth && depth != _model.FolderDepth)
                await _model.SetFolderDepthAsync(depth);
        }
        /// <summary>Copies completed checksum lines</summary><param name="sender">Button</param><param name="args">Click</param>
        private async void CopyAll(object sender, RoutedEventArgs args) => await _model.CopyAsync();
        /// <summary>Cancels pending work</summary><param name="sender">Button</param><param name="args">Click</param>
        private async void Cancel(object sender, RoutedEventArgs args) => await _model.CancelAsync();
        /// <summary>Clears the current round</summary><param name="sender">Button</param><param name="args">Click</param>
        private async void Clear(object sender, RoutedEventArgs args) => await _model.ChangeAsync(true);
    }
}
