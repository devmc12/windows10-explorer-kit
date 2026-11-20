using System.Windows;
using System.Windows.Controls;
using Win10ExplorerKit.UI.ViewModels;
namespace Win10ExplorerKit.UI.Views.Pages
{
    public partial class FoldersPage : Page
    {
        private readonly SettingsViewModel _model;
        /// <summary>Connects the page to shared preferences</summary>
        /// <param name="model">Settings and folder actions</param>
        internal FoldersPage(SettingsViewModel model) { _model = model; InitializeComponent(); DataContext = model; }
        /// <summary>Invokes the corresponding settings action</summary>
        /// <param name="sender">Button</param><param name="args">Click event</param>
        private void SaveFolders(object sender, RoutedEventArgs args) => _model.SaveFixedFolders();
        /// <summary>Invokes the corresponding settings action</summary>
        /// <param name="sender">Button</param><param name="args">Click event</param>
        private async void RestoreFolders(object sender, RoutedEventArgs args) => await _model.OpenFoldersAsync(false);
        /// <summary>Invokes the corresponding settings action</summary>
        /// <param name="sender">Button</param><param name="args">Click event</param>
        private async void OpenFixed(object sender, RoutedEventArgs args) => await _model.OpenFoldersAsync(true);
    }
}
