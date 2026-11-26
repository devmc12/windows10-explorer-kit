using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Win10ExplorerKit.App.Activation
{
    internal sealed class ActivationRequest
    {
        public string Action { get; set; }
        public string[] Paths { get; set; }
        public int Epoch { get; set; }
        public string BatchId { get; set; }
        public string Operation { get; set; }
        public int Mask { get; set; }
        public int Sequence { get; set; }
        public int Total { get; set; }
    }
    internal sealed class ActivationChannel : IDisposable
    {
        // Bound untrusted IPC allocations to one MiB per frame
        private const int MaximumFrameBytes = 1024 * 1024;
        // Bound a silent or stalled peer to fifteen seconds
        private const int TimeoutMilliseconds = 15000;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Func<ActivationRequest, CancellationToken, Task<int>> _receive;
        private NamedPipeServerStream _pipe;
        private readonly Task _listener;
        private int _disposed;
        private Task _cleanup = Task.CompletedTask;
        // Stable endpoint identity for this process's Windows user and login session
        private static readonly string PipeName = CreatePipeName();
        internal Task Completion => _cleanup;
        /// <summary>Reads the user and session once and closes their temporary native handles</summary>
        /// <returns>User-scoped pipe name</returns>
        private static string CreatePipeName()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            using (Process process = Process.GetCurrentProcess())
                return "Win10ExplorerKit." + identity.User.Value + "." + process.SessionId;
        }
        /// <summary>Starts a user-restricted activation listener</summary>
        /// <param name="receive">Dispatcher-side request handler</param>
        internal ActivationChannel(Func<ActivationRequest, CancellationToken, Task<int>> receive)
        { _receive = receive; _listener = Task.Run(ListenAsync); }
        /// <summary>Accepts framed requests and acknowledges only after dispatch succeeds</summary>
        /// <returns>Completion when shutdown closes the listener</returns>
        private async Task ListenAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                        security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous, 65536, 65536, security))
                    {
                        Interlocked.Exchange(ref _pipe, pipe);
                        await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                        {
                            timeout.CancelAfter(TimeoutMilliseconds);
                            using (timeout.Token.Register(() => pipe.Dispose()))
                            {
                                byte[] header = await ReadAsync(pipe, 4, timeout.Token).ConfigureAwait(false);
                                int length = BitConverter.ToInt32(header, 0);
                                if (length <= 0 || length > MaximumFrameBytes) throw new InvalidDataException("Invalid activation frame");
                                byte[] payload = await ReadAsync(pipe, length, timeout.Token).ConfigureAwait(false);
                                var request = new JavaScriptSerializer().Deserialize<ActivationRequest>(Encoding.UTF8.GetString(payload));
                                Validate(request);
                                Task<int> received = _receive(request, timeout.Token);
                                if (await Task.WhenAny(received, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false) != received)
                                {
                                    _ = ObserveExpiredAsync(received);
                                    throw new TimeoutException("Activation dispatch expired");
                                }
                                int result = await received.ConfigureAwait(false);
                                byte[] reply = BitConverter.GetBytes(result);
                                await pipe.WriteAsync(reply, 0, reply.Length, timeout.Token).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!_stop.IsCancellationRequested)
                    {
                        AppLogger.Write("Activation request failed", ex);
                        // Avoid a tight loop if a pipe cannot be recreated temporarily
                        await Task.Delay(250).ConfigureAwait(false);
                    }
                }
                finally { Interlocked.Exchange(ref _pipe, null); }
            }
        }
        /// <summary>Observes dispatch failures after a disconnected client has stopped waiting</summary>
        /// <param name="received">Outstanding dispatch</param><returns>Completion after dispatch finishes</returns>
        private static async Task ObserveExpiredAsync(Task received)
        {
            try { await received.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Write("Activation dispatch expired", ex); }
        }
        /// <summary>Rejects unsupported commands and oversized selections before dispatch</summary>
        /// <param name="request">Deserialized request</param>
        private static void Validate(ActivationRequest request)
        {
            if (request != null && (request.Action == "begin-permissions" || request.Action == "append-permissions" || request.Action == "end-permissions"))
            {
                if (!Guid.TryParseExact(request.BatchId, "N", out _) || request.Sequence < 0)
                    throw new InvalidDataException("Invalid permission batch");
                if (request.Action == "append-permissions") ValidatePaths(request.Paths);
                return;
            }
            if (request == null || (request.Action != "hash" && request.Action != "settings" && request.Action != "folders" && request.Action != "begin-hash" && request.Action != "append-hash"))
                throw new InvalidDataException("Invalid activation command");
            if (request.Action == "append-hash")
            {
                ValidatePaths(request.Paths);
            }
        }
        /// <summary>Validates bounded plain paths without accessing the filesystem</summary>
        /// <param name="paths">Selection payload</param>
        private static void ValidatePaths(string[] paths)
        {
            if (paths == null || paths.Length > 128) throw new InvalidDataException("Invalid selection size");
            foreach (string path in paths)
                if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || !Path.IsPathRooted(path)) throw new InvalidDataException("Invalid selection path");
        }
        /// <summary>Sends one bounded frame, starting the main application when needed</summary>
        /// <param name="request">Page or selection request</param>
        /// <param name="start">Whether a missing main process may be started</param>
        /// <param name="cancellation">Host shutdown cancellation</param>
        /// <returns>The UI's acknowledgement or selection epoch</returns>
        internal static async Task<int> SendAsync(ActivationRequest request, bool start = true, CancellationToken cancellation = default)
        {
            Validate(request);
            byte[] body = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(request));
            if (body.Length > MaximumFrameBytes) throw new InvalidDataException("Selection frame too large");
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try { await pipe.ConnectAsync(250, cancellation).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    if (start) using (Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, "--hotkey") { UseShellExecute = false })) { }
                    await pipe.ConnectAsync(TimeoutMilliseconds, cancellation).ConfigureAwait(false);
                }
                // Transfer foreground permission from the user-initiated shell invocation
                if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint processId)) AllowSetForegroundWindow(processId);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeoutMilliseconds);
                    using (timeout.Token.Register(() => pipe.Dispose()))
                    {
                        byte[] header = BitConverter.GetBytes(body.Length);
                        await pipe.WriteAsync(header, 0, header.Length, timeout.Token).ConfigureAwait(false);
                        await pipe.WriteAsync(body, 0, body.Length, timeout.Token).ConfigureAwait(false);
                        return BitConverter.ToInt32(await ReadAsync(pipe, 4, timeout.Token).ConfigureAwait(false), 0);
                    }
                }
            }
        }
        /// <summary>Reads an exact bounded frame despite partial pipe reads</summary>
        /// <param name="stream">Connected peer</param><param name="length">Required bytes</param><param name="token">Deadline</param>
        /// <returns>Complete frame</returns>
        private static async Task<byte[]> ReadAsync(Stream stream, int length, CancellationToken token)
        {
            byte[] bytes = new byte[length]; int offset = 0;
            while (offset < length)
            {
                int count = await stream.ReadAsync(bytes, offset, length - offset, token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
            return bytes;
        }
        /// <summary>Stops accepting new selections and releases pending pipe operations</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            Interlocked.Exchange(ref _pipe, null)?.Dispose();
            _cleanup = CompleteStopAsync();
        }
        /// <summary>Releases cancellation resources only after the listener has stopped using them</summary>
        /// <returns>Completion after listener teardown</returns>
        private async Task CompleteStopAsync()
        {
            try { await _listener.ConfigureAwait(false); }
            finally { _stop.Dispose(); }
        }

        /// <summary>Obtains the connected local server identity</summary><param name="pipe">Connected pipe</param><param name="processId">Server process</param><returns>Whether the process was found</returns>
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
        /// <summary>Lets the receiving UI activate its window</summary><param name="processId">Connected server</param><returns>Whether permission was granted</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(uint processId);
    }
}
