using System.Windows;
using System.Windows.Controls;
using Win10ExplorerKit.UI.ViewModels;
namespace Win10ExplorerKit.UI.Views.Pages
{
    public partial class SettingsPage : Page
    {
        private readonly SettingsViewModel _model;
        /// <summary>Connects the page to shared preferences</summary>
        /// <param name="model">Settings and folder actions</param>
        internal SettingsPage(SettingsViewModel model) { _model = model; InitializeComponent(); DataContext = model; }
        /// <summary>Invokes the corresponding settings action</summary>
        /// <param name="sender">Button</param><param name="args">Click event</param>
        private void OpenLogs(object sender, RoutedEventArgs args) => _model.OpenLogs();
    }
}
