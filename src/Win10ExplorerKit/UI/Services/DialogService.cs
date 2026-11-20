using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Markup;
namespace Win10ExplorerKit.UI.Services
{
    internal static class DialogService
    {
        /// <summary>Pumps the dispatcher for a maintenance-process error dialog</summary>
        /// <param name="message">Localized explanation</param>
        internal static void ShowBlocking(string message)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new System.Action(async () =>
            {
                try { await ShowAsync(message); }
                finally { frame.Continue = false; }
            }));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        /// <summary>Displays a themed failure even from a headless maintenance process</summary>
        /// <param name="message">Localized explanation without private exception details</param>
        /// <returns>Completion when dismissed</returns>
        internal static async Task ShowAsync(string message)
        {
            if (Application.Current == null)
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new ControlsDictionary());
            }
            var dialog = new Wpf.Ui.Controls.MessageBox { Title = ProductInfo.Name, Content = message,
                CloseButtonText = TranslationService.T("common.close"), Width = 440, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            await dialog.ShowDialogAsync();
        }
    }
}
