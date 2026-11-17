using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.UI.Tray;
using Win10ExplorerKit.Windows.Interop;

namespace Win10ExplorerKit.Tests.UI
{
    [TestClass]
    public class TrayContextMenuTests
    {
        // GWL_EXSTYLE reads the native extended style on both x86 and x64
        private const int ExtendedStyleIndex = -20;
        // WS_EX_TOOLWINDOW prevents taskbar and Alt+Tab entries
        private const int ToolWindowStyle = 0x80;
        // WS_EX_APPWINDOW would force a taskbar entry
        private const int AppWindowStyle = 0x40000;

        /// <summary>
        /// Reads native window styles rather than only checking managed creation parameters
        /// </summary>
        /// <param name="window">Created menu window</param>
        /// <param name="index">Window data index</param>
        /// <returns>The requested 32-bit window style value</returns>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        /// <summary>
        /// Verifies visible menus and submenus retain tool-window styles after activation and reopening
        /// </summary>
        [TestMethod]
        public void ActivatedMenusStayOutOfTaskbarAndRemainUsable()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var menu = new TrayContextMenu())
                    using (var submenu = new TrayContextMenu())
                    {
                        bool clicked = false;
                        var language = new ToolStripMenuItem("Language") { DropDown = submenu };
                        var english = new ToolStripMenuItem("English", null, (sender, args) => clicked = true);
                        submenu.Items.Add(english);
                        menu.Items.Add(language);
                        for (int i = 0; i < 2; i++)
                        {
                            menu.Show(new Point(20, 20));
                            NativeMethods.SetForegroundWindow(menu.Handle);
                            language.ShowDropDown();
                            Assert.IsTrue(menu.Visible);
                            Assert.IsTrue(submenu.Visible);
                            foreach (ToolStripDropDown popup in new ToolStripDropDown[] { menu, submenu })
                            {
                                int style = GetWindowLong(popup.Handle, ExtendedStyleIndex);
                                Assert.AreNotEqual(0, style & ToolWindowStyle);
                                Assert.AreEqual(0, style & AppWindowStyle);
                            }
                            english.PerformClick();
                            Assert.IsTrue(clicked);
                            clicked = false;
                            menu.Close();
                            Assert.IsFalse(menu.Visible);
                            Assert.IsFalse(submenu.Visible);
                        }
                    }
                }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "Tray menu test timed out");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
