// Runs serial permission operations with cancellation and optional elevation
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Win10ExplorerKit.Windows.Permissions;

namespace Win10ExplorerKit.Features.Permissions
{
    internal static class PermissionService
    {
        /// <summary>Processes one batch serially with at most one elevation prompt and item-level cancellation</summary>
        /// <param name="request">Immutable operation snapshot</param><param name="report">Thread-safe result delivery</param>
        /// <param name="token">Stops work before the next mutation</param><param name="session">Optional discovery session owned by the caller</param>
        /// <returns>Completion after owned helper teardown</returns>
        internal static async Task RunAsync(PermissionRequest request, Action<PermissionResult> report, CancellationToken token,
            PermissionSession session = null)
        {
            bool ownsSession = session == null;
            session = session ?? new PermissionSession();
            try
            {
                foreach (string path in request.Paths)
                {
                    report(await session.ProcessAsync(path, request, token).ConfigureAwait(false));
                }
            }
            finally { if (ownsSession) await session.CloseAsync().ConfigureAwait(false); }
        }
    }

    internal sealed class PermissionSession
    {
        private PermissionElevation _elevated;
        private bool _elevationAttempted;
        private string _elevationFailure = "permissions.elevationFailed";

        /// <summary>Serializes discovery and writes through one optional elevation session</summary>
        /// <param name="path">Single target</param><param name="request">Fixed operation and principal filter</param>
        /// <param name="token">Cancellation between native operations</param><returns>Sanitized result</returns>
        internal async Task<PermissionResult> ProcessAsync(string path, PermissionRequest request, CancellationToken token)
        {
            if (token.IsCancellationRequested) return PermissionResult.Create(path, "canceled", "permissions.canceled");
            PermissionResult result = await Task.Run(() => request.IsInspection()
                ? PermissionAccess.Inspect(path, request, token) : PermissionAccess.Apply(path, request, token)).ConfigureAwait(false);
            if (!result.NeedsElevation) return result;
            if (!_elevationAttempted && !token.IsCancellationRequested)
            {
                _elevationAttempted = true;
                try { _elevated = await PermissionElevation.StartAsync(token).ConfigureAwait(false); }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { _elevationFailure = "permissions.canceled"; }
                catch (OperationCanceledException) { _elevationFailure = "permissions.canceled"; }
                catch (Exception ex) { AppLogger.Write("Permission elevation failed", ex); }
            }
            if (_elevated == null || token.IsCancellationRequested)
                return PermissionResult.Create(path, token.IsCancellationRequested || _elevationFailure == "permissions.canceled" ? "canceled" : "failed",
                    token.IsCancellationRequested ? "permissions.canceled" : _elevationFailure);
            try
            {
                return await _elevated.ApplyAsync(new PermissionRequest { Operation = request.Operation, Mask = request.Mask,
                    PrincipalSids = request.PrincipalSids, ProfileRoot = request.ProfileRoot, Paths = new[] { path } }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Write("Permission worker communication failed", ex);
                _elevationFailure = "permissions.workerFailed";
                await CloseAsync().ConfigureAwait(false);
                return PermissionResult.Create(path, "failed", _elevationFailure);
            }
        }

        /// <summary>Releases a discovery or mutation worker after its current operation has finished</summary>
        /// <returns>Completion after the helper exits</returns>
        internal async Task CloseAsync()
        {
            if (_elevated == null) return;
            var worker = _elevated;
            _elevated = null;
            await worker.CloseAsync().ConfigureAwait(false);
        }
    }
}
