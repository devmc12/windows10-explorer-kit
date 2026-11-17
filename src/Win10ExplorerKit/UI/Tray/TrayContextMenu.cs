using System.Windows.Forms;

namespace Win10ExplorerKit.UI.Tray
{
    internal sealed class TrayContextMenu : ContextMenuStrip
    {
        // WS_EX_TOOLWINDOW excludes popup windows from the taskbar and Alt+Tab
        private const int ToolWindowStyle = 0x00000080;
        // WS_EX_APPWINDOW forces a visible top-level window onto the taskbar
        private const int AppWindowStyle = 0x00040000;

        /// <summary>
        /// Keeps tray menus out of the taskbar even when foreground activation changes their owner
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle = (parameters.ExStyle | ToolWindowStyle) & ~AppWindowStyle;
                return parameters;
            }
        }
    }
}
