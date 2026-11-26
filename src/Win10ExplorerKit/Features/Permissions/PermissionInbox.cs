// Accepts complete shell selections and prevents duplicate permission execution
using System;
using System.Collections.Generic;
using System.Linq;
using Win10ExplorerKit.App.Activation;

namespace Win10ExplorerKit.Features.Permissions
{
    internal sealed class PermissionInbox
    {
        // Bound accumulated shell selections independently of the per-frame IPC limit
        internal const int MaximumItems = 10000;
        // Bound queued incomplete selections and idle lifetime
        private const int MaximumBatches = 16;
        private const int ExpirySeconds = 60;
        // Never evict executed IDs within a process; reject new transactions at the bounded session limit
        private const int MaximumCompletions = 65536;
        // Bound retained UTF-16 path data across frames in each selection
        private const int MaximumCharacters = 4000000;
        private readonly Dictionary<string, Batch> _pending = new Dictionary<string, Batch>();
        private readonly HashSet<string> _completed = new HashSet<string>();
        private readonly Func<PermissionRequest, bool> _accept;

        private sealed class Batch
        {
            internal ActivationRequest Header;
            internal readonly List<string> Paths = new List<string>();
            internal int Sequence;
            internal int Characters;
            internal DateTime Expires;
        }

        /// <summary>Creates a dispatcher-owned transaction receiver</summary>
        /// <param name="accept">Accepts complete immutable selections into the bounded UI queue</param>
        internal PermissionInbox(Func<PermissionRequest, bool> accept) => _accept = accept;

        /// <summary>Expires partial selections without starting any operation</summary>
        internal void Sweep()
        {
            if (_pending.Count == 0) return;
            foreach (string id in _pending.Where(pair => pair.Value.Expires <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
                _pending.Remove(id);
        }

        /// <summary>Processes a transaction frame, acknowledging only complete accepted work</summary>
        /// <param name="request">Validated activation frame</param><returns>Zero for accepted frames, minus one for rejection</returns>
        internal int Receive(ActivationRequest request)
        {
            Sweep();
            if (_completed.Contains(request.BatchId)) return 0;
            if (request.Action == "begin-permissions")
            {
                if (_pending.ContainsKey(request.BatchId)) return -1;
                if (_pending.Count >= MaximumBatches || _completed.Count + _pending.Count >= MaximumCompletions ||
                    request.Total < 1 || request.Total > MaximumItems) return -1;
                var operation = new PermissionRequest { Operation = request.Operation, Mask = request.Mask };
                if (request.Operation != "manage" && !operation.IsValid()) return -1;
                _pending.Add(request.BatchId, new Batch { Header = request, Expires = DateTime.UtcNow.AddSeconds(ExpirySeconds) });
                return 0;
            }
            if (!_pending.TryGetValue(request.BatchId, out Batch batch)) return -1;
            if (request.Action == "append-permissions")
            {
                // Abort the entire selection on ordering or size errors instead of executing a prefix
                if (request.Sequence != batch.Sequence || request.Paths == null || batch.Paths.Count + request.Paths.Length > batch.Header.Total ||
                    batch.Characters + request.Paths.Sum(path => path.Length) > MaximumCharacters)
                { _pending.Remove(request.BatchId); return -1; }
                batch.Paths.AddRange(request.Paths);
                batch.Characters += request.Paths.Sum(path => path.Length);
                batch.Sequence++;
                batch.Expires = DateTime.UtcNow.AddSeconds(ExpirySeconds);
                return 0;
            }
            _pending.Remove(request.BatchId);
            if (request.Action != "end-permissions" || request.Sequence != batch.Sequence || batch.Paths.Count != batch.Header.Total) return -1;
            var command = new PermissionRequest { Operation = batch.Header.Operation, Mask = batch.Header.Mask,
                Paths = batch.Paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            if (!_accept(command)) return -1;
            _completed.Add(request.BatchId);
            return 0;
        }
    }
}
