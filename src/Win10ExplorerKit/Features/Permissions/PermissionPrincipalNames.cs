// Resolves display labels without allowing account lookup delays to hold up the dialog
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Win10ExplorerKit.Features.Permissions
{
    internal static class PermissionPrincipalNames
    {
        // Fall back to stable SID labels when Windows account lookup takes longer than this interval
        private const int LookupTimeoutMilliseconds = 1500;
        private static readonly SemaphoreSlim LookupGate = new SemaphoreSlim(1, 1);

        /// <summary>Attempts one bounded account-name lookup with SID fallback and no persisted identity data</summary>
        /// <param name="sids">Validated existing SID values</param><param name="token">Dialog cancellation</param>
        /// <returns>Display labels keyed by their original SID values</returns>
        internal static async Task<Dictionary<string, string>> ResolveAsync(string[] sids, CancellationToken token)
        {
            var fallback = sids.ToDictionary(sid => sid, sid => sid, StringComparer.Ordinal);
            if (sids.Length == 0 || !LookupGate.Wait(0)) return fallback;
            var lookup = Task.Run(() =>
            {
                try
                {
                    var identities = new IdentityReferenceCollection();
                    foreach (string sid in sids) identities.Add(new SecurityIdentifier(sid));
                    var names = identities.Translate(typeof(NTAccount), false);
                    return sids.Select((sid, index) => new { sid, name = names[index].Value })
                        .ToDictionary(item => item.sid, item => item.name, StringComparer.Ordinal);
                }
                catch (Exception) { return fallback; }
                finally { LookupGate.Release(); }
            });
            if (await Task.WhenAny(lookup, Task.Delay(LookupTimeoutMilliseconds, token)).ConfigureAwait(false) == lookup)
                return await lookup.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return fallback;
        }
    }
}
