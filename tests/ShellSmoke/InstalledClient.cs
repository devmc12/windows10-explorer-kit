using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Win10ExplorerKit.Windows.Interop;

internal static class InstalledClient
{
    /// <summary>Invokes the installed menu without prestarting or replacing its COM server</summary>
    /// <param name="args">Selection manifest, expected result count and registered host path</param>
    /// <returns>Zero when a real hash window completes the selected files</returns>
    [STAThread]
    private static int Main(string[] args)
    {
        IShellItemArray selection = null;
        IntPtr[] pidls = null;
        try
        {
            string[] paths = File.ReadAllLines(args[0]);
            int expected = int.Parse(args[1]);
            string hostPath = Path.GetFullPath(args[2]);
            pidls = new IntPtr[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                uint attributes;
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], IntPtr.Zero, out pidls[i], 0, out attributes));
            }
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out selection));
            var elapsed = Stopwatch.StartNew();
            MenuClient.Invoke(selection, "Win10ExplorerKit.Hash");
            while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
            {
                Application.DoEvents();
                bool finished = false;
                foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(hostPath)))
                using (process)
                {
                    var imagePath = new StringBuilder(1024);
                    int capacity = imagePath.Capacity;
                    if (!QueryFullProcessImageName(process.Handle, 0, imagePath, ref capacity) ||
                        !string.Equals(imagePath.ToString(), hostPath, StringComparison.OrdinalIgnoreCase)) continue;
                    IntPtr window = process.MainWindowHandle;
                    if (window == IntPtr.Zero) continue;
                    EnumChildWindows(window, delegate(IntPtr child, IntPtr parameter)
                    {
                        var text = new StringBuilder(512);
                        GetWindowText(child, text, text.Capacity);
                        if (text.ToString().Contains(expected + "/" + expected)) finished = true;
                        return !finished;
                    }, IntPtr.Zero);
                    if (finished)
                    {
                        Console.WriteLine("Native menu, " + (IntPtr.Size * 8) + "-bit client: " + expected + " completed in " + elapsed.Elapsed.TotalSeconds.ToString("F2") + " s");
                        PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                        if (!process.WaitForExit(10000)) throw new TimeoutException("Hash host did not exit after closing");
                        return 0;
                    }
                }
                Thread.Sleep(30);
            }
            throw new TimeoutException("Installed hash window did not finish the selection");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (selection != null) Marshal.ReleaseComObject(selection);
            if (pidls != null) foreach (IntPtr pidl in pidls) if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>Reads the process image across the x86/x64 boundary</summary>
    /// <param name="process">Process handle</param><param name="flags">Path format</param>
    /// <param name="path">Output path</param><param name="size">Buffer size</param><returns>Success</returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);

    /// <summary>Visits one native child window</summary>
    /// <param name="window">Window handle</param><param name="parameter">Caller data</param><returns>Continue enumeration</returns>
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    /// <summary>Enumerates child controls for the localized completion summary</summary>
    /// <param name="parent">Hash window</param><param name="callback">Visitor</param><param name="parameter">Caller data</param><returns>Success</returns>
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr parameter);
    /// <summary>Reads a control's caption</summary>
    /// <param name="window">Control</param><param name="text">Output</param><param name="count">Capacity</param><returns>Copied character count</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    /// <summary>Requests normal window closure</summary>
    /// <param name="window">Window</param><param name="message">Message</param><param name="wParam">Parameter</param><param name="lParam">Parameter</param><returns>Success</returns>
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    /// <summary>Creates a native filesystem item ID</summary>
    /// <param name="name">Path</param><param name="context">Optional bind context</param><param name="pidl">Owned result</param><param name="mask">Attribute mask</param><param name="attributes">Result attributes</param><returns>HRESULT</returns>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr pidl, uint mask, out uint attributes);
    /// <summary>Creates the complete native selection</summary>
    /// <param name="count">Item count</param><param name="pidls">Items</param><param name="array">Owned array</param><returns>HRESULT</returns>
    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromIDLists(uint count, [In] IntPtr[] pidls, out IShellItemArray array);
}
