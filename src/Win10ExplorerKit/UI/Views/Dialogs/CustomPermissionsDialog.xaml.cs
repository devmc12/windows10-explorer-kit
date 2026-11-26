// Hosts the custom permission form using the application's shared styles
using System.Windows.Controls;
using Win10ExplorerKit.UI.ViewModels;

namespace Win10ExplorerKit.UI.Views.Dialogs
{
    public partial class CustomPermissionsDialog : UserControl
    {
        /// <summary>Connects the transient choices to the two-column dialog layout</summary>
        /// <param name="model">Dialog state</param>
        internal CustomPermissionsDialog(CustomPermissionViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
