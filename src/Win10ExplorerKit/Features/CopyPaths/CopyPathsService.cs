using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Win10ExplorerKit.Features.CopyPaths
{
    internal static class CopyPathService
    {
        // Session-local ownership key for shell item aggregation
        private const string MutexName = @"Local\Win10ExplorerKit.Aggregator";
        // Named pipe accepting paths from sibling shell invocations
        private const string PipeName = "Win10ExplorerKit.AggregatorPipe";
        // Maximum collection duration in milliseconds for per-item shell launches
        private const int CollectionWindowMilliseconds = 750;

        /// <summary>
        /// Copies a shell selection, aggregating separate item invocations when needed
        /// </summary>
        /// <param name="inputPaths">Paths supplied by Explorer</param>
        /// <param name="plain">Whether to omit surrounding double quotes</param>
        /// <returns>True if the clipboard write or forwarding succeeded</returns>
        public static bool CopyFromShell(IEnumerable<string> inputPaths, bool plain)
        {
            var paths = PathFormatter.NormalizeAndDeduplicate(inputPaths);
            if (paths.Count == 0)
            {
                return false;
            }

            // If the shell passed all selected items in one invocation, copy immediately
            if (paths.Count > 1)
            {
                return PutOnClipboard(paths, plain);
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    if (TrySendToOwner(paths))
                    {
                        return true;
                    }

                    return PutOnClipboard(paths, plain);
                }

                // The owner waits briefly because Explorer can launch one process per item
                var collected = new List<string>(paths);
                var seen = new HashSet<string>(collected, StringComparer.OrdinalIgnoreCase);
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(CollectionWindowMilliseconds);

                while (DateTime.UtcNow < deadline)
                {
                    int remaining = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                    using (var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        // BeginWaitForConnection requires an asynchronous pipe handle
                        PipeOptions.Asynchronous))
                    {
                        IAsyncResult wait = server.BeginWaitForConnection(null, null);
                        // Close timed-out handles before completing the pending async operation
                        using (WaitHandle waitHandle = wait.AsyncWaitHandle)
                        {
                            if (!waitHandle.WaitOne(remaining))
                            {
                                server.Close();
                                try
                                {
                                    server.EndWaitForConnection(wait);
                                }
                                catch (ObjectDisposedException)
                                {
                                }
                                catch (IOException)
                                {
                                }

                                break;
                            }

                            server.EndWaitForConnection(wait);
                        }

                        using (var reader = new StreamReader(server, Encoding.UTF8))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                AddPath(line, collected, seen);
                            }
                        }
                    }
                }

                bool result = PutOnClipboard(collected, plain);
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // The mutex is disposed below. A failed release does not change the
                    // clipboard result and can happen if the process is being torn down
                }

                return result;
            }
        }

        /// <summary>
        /// Writes deduplicated paths as Unicode lines with bounded clipboard retries
        /// </summary>
        /// <param name="inputPaths">Paths to normalize and copy</param>
        /// <param name="plain">Whether to omit surrounding double quotes</param>
        /// <returns>True if the clipboard accepted the text</returns>
        public static bool PutOnClipboard(IEnumerable<string> inputPaths, bool plain)
        {
            string text = PathFormatter.Format(inputPaths, plain);
            return text.Length > 0 && ClipboardService.SetText(text);
        }

        /// <summary>
        /// Forwards item paths to the process owning the collection window
        /// </summary>
        /// <param name="paths">Paths belonging to this shell invocation</param>
        /// <returns>True if the owner accepted the connection and payload</returns>
        private static bool TrySendToOwner(IList<string> paths)
        {
            for (int attempt = 0; attempt < 14; attempt++)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        client.Connect(80);
                        using (var writer = new StreamWriter(client, Encoding.UTF8))
                        {
                            foreach (string path in paths)
                            {
                                writer.WriteLine(path);
                            }

                            writer.Flush();
                        }
                    }

                    return true;
                }
                catch (TimeoutException)
                {
                    Thread.Sleep(25);
                }
                catch (IOException)
                {
                    Thread.Sleep(25);
                }
            }

            return false;
        }

        /// <summary>
        /// Adds a received path while keeping aggregate order and uniqueness
        /// </summary>
        /// <param name="raw">Path received from another process</param>
        /// <param name="collected">Ordered aggregate owned by the collector</param>
        /// <param name="seen">Case-insensitive set shared by the collector</param>
        private static void AddPath(string raw, IList<string> collected, ISet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            string path = raw.Trim();
            try
            {
                path = Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }

            if (seen.Add(path))
            {
                collected.Add(path);
            }
        }
    }
}
