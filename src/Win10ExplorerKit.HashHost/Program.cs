using System;

namespace Win10ExplorerKit.HashHost
{
    internal static class Program
    {
        /// <summary>Starts the shared hash implementation in an explicitly targeted COM host</summary>
        /// <returns>The application's exit code</returns>
        [STAThread]
        private static int Main()
        {
            return Win10ExplorerKit.Program.Main(new[] { "--hash-server" });
        }
    }
}
