// Connects a permission batch to an authenticated elevated worker
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;
using Win10ExplorerKit.Windows.Permissions;

namespace Win10ExplorerKit.Features.Permissions
{
    internal sealed class PermissionElevation
    {
        // One MiB bounds every private worker request and response
        private const int MaximumFrameBytes = 1024 * 1024;
        // Allow time for one user-controlled UAC prompt without blocking the UI
        private const int ConnectTimeoutMilliseconds = 120000;
        private NamedPipeServerStream _pipe;
        private Process _process;

        /// <summary>Starts a single authenticated elevated worker for this batch</summary>
        /// <param name="token">Cancellation before any ACL request is sent</param><returns>Connected worker session</returns>
        internal static async Task<PermissionElevation> StartAsync(CancellationToken token)
        {
            var session = new PermissionElevation();
            string nonce = Guid.NewGuid().ToString("N");
            string name = "Win10ExplorerKit.Permissions." + nonce;
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            using (var identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            try
            {
                session._pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 4096, 4096, security);
                int parent;
                using (var process = Process.GetCurrentProcess()) parent = process.Id;
                session._process = await Task.Run(() => Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath,
                    "--permission-worker " + parent + " " + nonce) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }));
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(ConnectTimeoutMilliseconds);
                    using (deadline.Token.Register(() => session._pipe.Dispose()))
                        await session._pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
                }
                if (!GetNamedPipeClientProcessId(session._pipe.SafePipeHandle, out uint peer) || peer != session._process.Id)
                    throw new UnauthorizedAccessException();
                return session;
            }
            catch { await session.CloseAsync().ConfigureAwait(false); throw; }
        }

        /// <summary>Runs one ACL operation and waits for its write to finish even when batch cancellation is requested</summary>
        /// <param name="request">Single-path permission request</param><returns>Verified or failed result</returns>
        internal async Task<PermissionResult> ApplyAsync(PermissionRequest request)
        {
            await WriteAsync(_pipe, request).ConfigureAwait(false);
            return await ReadAsync<PermissionResult>(_pipe).ConfigureAwait(false);
        }

        /// <summary>Closes the channel and waits until the helper has released its native resources</summary>
        /// <returns>Completion after the helper exits</returns>
        internal async Task CloseAsync()
        {
            _pipe?.Dispose();
            if (_process != null)
            {
                try { await Task.Run(() => _process.WaitForExit()).ConfigureAwait(false); }
                finally { _process.Dispose(); _process = null; }
            }
        }

        /// <summary>Runs the headless elevated endpoint before application initialization</summary>
        /// <param name="parentId">Expected initiating process</param><param name="nonce">Random endpoint identifier</param>
        /// <returns>Process exit code</returns>
        internal static int RunWorker(int parentId, string nonce)
        {
            try
            {
                if (!Guid.TryParseExact(nonce, "N", out _)) return 2;
                using (var identity = WindowsIdentity.GetCurrent())
                    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 2;
                using (var parent = Process.GetProcessById(parentId))
                {
                    if (!string.Equals(parent.MainModule.FileName, AppPaths.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return 2;
                    using (var pipe = new NamedPipeClientStream(".", "Win10ExplorerKit.Permissions." + nonce, PipeDirection.InOut, PipeOptions.Asynchronous))
                    {
                        pipe.Connect(15000);
                        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != parentId) return 2;
                        PermissionAccess.EnableRecoveryPrivileges();
                        while (!parent.HasExited)
                        {
                            var request = ReadAsync<PermissionRequest>(pipe).GetAwaiter().GetResult();
                            if (request == null || (!request.IsValid() && !request.IsInspection()) || request.Paths == null || request.Paths.Length != 1 ||
                                request.Paths[0] == null || request.Paths[0].Length > 32767 ||
                                string.IsNullOrWhiteSpace(request.ProfileRoot)) return 2;
                            PermissionResult result = request.IsInspection()
                                ? PermissionAccess.Inspect(request.Paths[0], request, CancellationToken.None)
                                : PermissionAccess.Apply(request.Paths[0], request, CancellationToken.None);
                            result.NeedsElevation = false;
                            WriteAsync(pipe, result).GetAwaiter().GetResult();
                        }
                    }
                }
                return 0;
            }
            catch (EndOfStreamException) { return 0; }
            catch (IOException) { return 0; }
            catch (Exception ex) { AppLogger.Write("Permission worker failed", ex); return 2; }
        }

        /// <summary>Writes one length-delimited JSON frame with a hard allocation limit</summary>
        /// <param name="pipe">Connected private channel</param><param name="value">Typed payload</param><returns>Write completion</returns>
        private static async Task WriteAsync(Stream pipe, object value)
        {
            byte[] body = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value));
            if (body.Length > MaximumFrameBytes) throw new InvalidDataException();
            byte[] size = BitConverter.GetBytes(body.Length);
            await pipe.WriteAsync(size, 0, size.Length).ConfigureAwait(false);
            await pipe.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }

        /// <summary>Reads a typed bounded payload</summary>
        /// <typeparam name="T">Expected DTO</typeparam><param name="pipe">Connected channel</param><returns>Decoded payload</returns>
        private static async Task<T> ReadAsync<T>(Stream pipe)
        {
            byte[] header = await ReadBytesAsync(pipe, 4).ConfigureAwait(false);
            int count = BitConverter.ToInt32(header, 0);
            if (count <= 0 || count > MaximumFrameBytes) throw new InvalidDataException();
            return new JavaScriptSerializer().Deserialize<T>(Encoding.UTF8.GetString(await ReadBytesAsync(pipe, count).ConfigureAwait(false)));
        }

        /// <summary>Handles partial reads and peer disconnects</summary>
        /// <param name="pipe">Connected channel</param><param name="count">Validated byte count</param><returns>Exact frame segment</returns>
        private static async Task<byte[]> ReadBytesAsync(Stream pipe, int count)
        {
            var bytes = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await pipe.ReadAsync(bytes, offset, count - offset).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return bytes;
        }

        /// <summary>Identifies the connected elevated client</summary>
        /// <param name="pipe">Server handle</param><param name="process">Client PID</param><returns>Success</returns>
        [DllImport("kernel32.dll")]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint process);
        /// <summary>Identifies the initiating application rather than trusting a pipe name alone</summary>
        /// <param name="pipe">Client handle</param><param name="process">Server PID</param><returns>Success</returns>
        [DllImport("kernel32.dll")]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint process);
    }
}
