using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Win10ExplorerKit.App;
using Win10ExplorerKit.App.Hashing;
using Win10ExplorerKit.Configuration;
using Win10ExplorerKit.Features.FileHash;
using Win10ExplorerKit.UI.Forms.Hash;
using Win10ExplorerKit.Windows.Interop;

internal static class SmokeHost
{
    /// <summary>Runs an isolated COM server or a native Shell selection client</summary>
    /// <param name="args">Mode, temporary class ID, workspace and optional selection manifest</param>
    /// <returns>Zero on success</returns>
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Guid clsid = new Guid(args[1]);
            string directory = args[2];
            string role = "HashSmoke" + clsid.ToString("N");
            if (args[0] == "stop") return SingleInstance.RequestStop(role) ? 0 : 1;
            Marshal.ThrowExceptionForHR(HashComNative.OleInitialize(IntPtr.Zero));
            try
            {
                if (args[0] == "server") Server(clsid, directory, role);
                else Client(clsid, directory, args[3], int.Parse(args[4]), args[0] == "menu");
            }
            finally { HashComNative.OleUninitialize(); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    /// <summary>Hosts the production context with isolated preferences and activation identity</summary>
    /// <param name="clsid">Temporary COM identity</param>
    /// <param name="directory">Temporary data and report directory</param>
    /// <param name="role">Temporary single-instance role</param>
    private static void Server(Guid clsid, string directory, string role)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var owner = new SingleInstance(role))
        using (var context = new HashApplicationContext(new SettingsService(Path.Combine(directory, "Config.ini"), "en"), owner, clsid))
        using (var timer = new System.Windows.Forms.Timer { Interval = 150 })
        {
            string previous = null;
            timer.Tick += delegate
            {
                var form = (HashForm)typeof(HashApplicationContext).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context);
                if (form == null || form.IsDisposed) return;
                int complete = form.Results.Count(row => row.State == HashState.Complete);
                int failed = form.Results.Count(row => row.State == HashState.Failed);
                string state = string.Join(",", Process.GetCurrentProcess().Id, form.Results.Count, complete, failed, form.IsBusy);
                if (state == previous) return;
                previous = state;
                File.WriteAllText(Path.Combine(directory, "state.txt"), state);
                if (!form.IsBusy && complete > 0)
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(directory, "window.png"));
                    }
            };
            timer.Start();
            Application.Run(context);
        }
    }

    /// <summary>Sends a complete Shell array and pumps the caller STA until the server finishes</summary>
    /// <param name="clsid">Temporary COM identity</param>
    /// <param name="directory">Report directory</param>
    /// <param name="manifest">One absolute path per line</param>
    /// <param name="expected">Expected deduplicated completed result count</param>
    /// <param name="menu">Whether to invoke the native context menu instead of COM directly</param>
    private static void Client(Guid clsid, string directory, string manifest, int expected, bool menu)
    {
        string[] paths = File.ReadAllLines(manifest);
        IntPtr[] pidls = new IntPtr[paths.Length];
        IShellItemArray selection = null;
        object command = null;
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                uint attributes;
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], IntPtr.Zero, out pidls[i], 0, out attributes));
            }
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out selection));
            if (menu) MenuClient.Invoke(selection, "ExplorerKitHashSmoke" + clsid.ToString("N"));
            else
            {
                command = Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true));
                ((IHashObjectWithSelection)command).SetSelection(selection);
                ((IHashExecuteCommand)command).Execute();
            }
            DateTime deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                string report = Path.Combine(directory, "state.txt");
                try
                {
                    if (File.Exists(report))
                    {
                        string[] fields = File.ReadAllText(report).Split(',');
                        if (fields.Length == 5 && fields[1] == expected.ToString() && fields[2] == expected.ToString() && fields[3] == "0" && fields[4] == "False")
                        {
                            Console.WriteLine("Client " + (IntPtr.Size * 8) + "-bit: " + expected + " results; server PID " + fields[0]);
                            return;
                        }
                    }
                }
                catch (IOException) { }
                Thread.Sleep(20);
            }
            throw new TimeoutException("COM selection did not produce the expected results");
        }
        finally
        {
            if (command != null) Marshal.ReleaseComObject(command);
            if (selection != null) Marshal.ReleaseComObject(selection);
            foreach (IntPtr pidl in pidls) if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>Creates a native item ID list</summary>
    /// <param name="name">Absolute path</param><param name="context">Optional context</param>
    /// <param name="pidl">Owned ID list</param><param name="mask">Attribute mask</param>
    /// <param name="attributes">Attributes</param><returns>HRESULT</returns>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr pidl, uint mask, out uint attributes);

    /// <summary>Creates the native array marshaled by Explorer</summary>
    /// <param name="count">Item count</param><param name="pidls">ID lists</param>
    /// <param name="array">Owned array</param><returns>HRESULT</returns>
    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count, [In] IntPtr[] pidls, out IShellItemArray array);
}
