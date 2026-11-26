// Connects permission page interactions to the shared view model
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Threading;
using System.Threading.Tasks;
using Win10ExplorerKit.Features.Permissions;
using Win10ExplorerKit.UI.Views.Dialogs;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Win10ExplorerKit.UI.ViewModels;
using Win10ExplorerKit.UI.Services;

namespace Win10ExplorerKit.UI.Views.Pages
{
    public partial class PermissionsPage : Page
    {
        private readonly PermissionViewModel _model;
        private bool _dialogOpen;
        /// <summary>Builds a persistent page without starting any permission work</summary><param name="model">Shared state</param>
        internal PermissionsPage(PermissionViewModel model) { _model = model; DataContext = model; InitializeComponent(); }
        /// <summary>Offers file-copy drop semantics on the page and its table</summary>
        /// <param name="sender">Drop surface</param><param name="args">Drag data</param>
        private void DragOverFiles(object sender, DragEventArgs args)
        {
            args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) && (args.AllowedEffects & DragDropEffects.Copy) != 0
                ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        }
        /// <summary>Adds dropped paths without automatically modifying permissions</summary>
        /// <param name="sender">Drop surface</param><param name="args">Dropped data</param>
        private void DropFiles(object sender, DragEventArgs args)
        {
            DragOverFiles(sender, args);
            if (args.Effects == DragDropEffects.Copy && args.Data.GetData(DataFormats.FileDrop) is string[] paths)
                _model.AddPaths(paths);
        }
        /// <summary>Adds selected files without probing their ACL</summary><param name="sender">Button</param><param name="args">Click</param>
        private void AddFiles(object sender, RoutedEventArgs args)
        {
            var picker = new OpenFileDialog { Multiselect = true, CheckFileExists = true };
            if (picker.ShowDialog(Window.GetWindow(this)) == true) _model.AddPaths(picker.FileNames);
        }
        /// <summary>Adds folders through the native shell picker</summary><param name="sender">Button</param><param name="args">Click</param>
        private void AddFolders(object sender, RoutedEventArgs args)
        {
            try { _model.AddPaths(FolderPicker.Show(Window.GetWindow(this))); }
            catch (Exception ex) { AppLogger.Write("Folder picker failed", ex); ((MainWindow)Window.GetWindow(this)).Notify(TranslationService.T("error.operation")); }
        }
        /// <summary>Snapshots selection independently of recycled row containers</summary><param name="sender">Grid</param><param name="args">Selection change</param>
        private void SelectionChanged(object sender, SelectionChangedEventArgs args) => _model.Select(Targets.SelectedItems.Cast<PermissionRow>());
        /// <summary>Denies every existing principal full control</summary><param name="sender">Button</param><param name="args">Click</param>
        private void DenyAll(object sender, RoutedEventArgs args) => _model.Apply("deny");
        /// <summary>Allows every existing principal full control</summary><param name="sender">Button</param><param name="args">Click</param>
        private void AllowAll(object sender, RoutedEventArgs args) => _model.Apply("allow");
        /// <summary>Removes selected rows without touching permissions</summary><param name="sender">Button</param><param name="args">Click</param>
        private void Remove(object sender, RoutedEventArgs args) => _model.Remove(false);
        /// <summary>Clears only in-memory targets</summary><param name="sender">Button</param><param name="args">Click</param>
        private void Clear(object sender, RoutedEventArgs args) => _model.Remove(true);
        /// <summary>Cancels operations not yet started</summary><param name="sender">Button</param><param name="args">Click</param>
        private void Cancel(object sender, RoutedEventArgs args) => _model.Cancel();
        /// <summary>Collects transient custom options with an explicit apply action</summary><param name="sender">Button</param><param name="args">Click</param>
        private async void Custom(object sender, RoutedEventArgs args)
        {
            if (_dialogOpen || !_model.CanApply) return;
            string[] paths = _model.SnapshotTargets();
            _dialogOpen = true;
            try { await _model.CustomizeAsync((session, token) => ShowCustomAsync(paths, session, token)); }
            finally { _dialogOpen = false; }
        }

        /// <summary>Loads principals while keeping the modal responsive and cancels discovery when it closes</summary>
        /// <param name="paths">Captured targets</param><param name="session">Optional shared elevated worker</param>
        /// <param name="token">Application or batch cancellation</param><returns>Chosen operation or no mutation</returns>
        private async Task<PermissionRequest> ShowCustomAsync(string[] paths, PermissionSession session, CancellationToken token)
        {
            var model = new CustomPermissionViewModel();
            var dialog = new ContentDialog(((MainWindow)Window.GetWindow(this)).PermissionDialogHost)
            {
                Title = TranslationService.T("permissions.custom"), Content = new CustomPermissionsDialog(model),
                PrimaryButtonText = TranslationService.T("permissions.apply"), CloseButtonText = TranslationService.T("hash.cancel"),
                DialogMaxWidth = 640
            };
            var buttonStyle = new Style(typeof(Wpf.Ui.Controls.Button), (Style)FindResource(typeof(Wpf.Ui.Controls.Button)));
            buttonStyle.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));
            dialog.Resources.Add(typeof(Wpf.Ui.Controls.Button), buttonStyle);
            dialog.Loaded += PlaceCustomDialogActions;
            dialog.SetBinding(ContentDialog.IsPrimaryButtonEnabledProperty, new Binding(nameof(model.CanApply)) { Source = model });
            using (var loadingCancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task loading = model.LoadAsync(paths, session, _model.ReportInspection, loadingCancellation.Token);
                try
                {
                    return await dialog.ShowAsync(token) == ContentDialogResult.Primary && !token.IsCancellationRequested
                        ? model.CreateRequest() : null;
                }
                finally
                {
                    loadingCancellation.Cancel();
                    await loading;
                    dialog.Loaded -= PlaceCustomDialogActions;
                    BindingOperations.ClearBinding(dialog, ContentDialog.IsPrimaryButtonEnabledProperty);
                }
            }
        }

        /// <summary>Moves the primary Fluent footer column to the right while preserving its visibility triggers</summary>
        /// <param name="sender">Custom permission dialog</param><param name="args">Template load event</param>
        private void PlaceCustomDialogActions(object sender, RoutedEventArgs args)
        {
            var dialog = (ContentDialog)sender;
            if (!(dialog.Template.FindName("PrimaryButton", dialog) is Wpf.Ui.Controls.Button apply) ||
                !(dialog.Template.FindName("CloseButton", dialog) is Wpf.Ui.Controls.Button cancel) ||
                !(apply.Parent is Grid footer) || footer.ColumnDefinitions.Count != 5 ||
                Grid.GetColumn(apply) != 0 || Grid.GetColumn(cancel) != 4) return;
            var primaryColumn = footer.ColumnDefinitions[0];
            var closeColumn = footer.ColumnDefinitions[4];
            footer.ColumnDefinitions.RemoveAt(4);
            footer.ColumnDefinitions.RemoveAt(0);
            footer.ColumnDefinitions.Insert(0, closeColumn);
            footer.ColumnDefinitions.Add(primaryColumn);
            Grid.SetColumn(cancel, 0);
            Grid.SetColumn(apply, 4);
            System.Windows.Input.KeyboardNavigation.SetTabIndex(cancel, 0);
            System.Windows.Input.KeyboardNavigation.SetTabIndex(apply, 1);
        }
    }
}
