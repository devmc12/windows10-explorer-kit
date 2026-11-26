// Transforms access rules while preserving the existing principal set
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Win10ExplorerKit.Features.Permissions
{
    internal static class PermissionRules
    {
        /// <summary>Transforms only ordinary access ACEs while retaining the existing principal set</summary>
        /// <param name="before">Original descriptor</param><param name="request">Validated operation</param>
        /// <returns>A protected descriptor without modifying owner, group or audit data</returns>
        internal static RawSecurityDescriptor Transform(RawSecurityDescriptor before, PermissionRequest request)
        {
            if (!request.IsValid()) throw new ArgumentException(nameof(request));
            var principals = GetPrincipals(before);
            var acl = before.DiscretionaryAcl;
            bool custom = request.Operation.StartsWith("custom-", StringComparison.Ordinal);
            var selected = new HashSet<string>(custom && request.PrincipalSids != null ? request.PrincipalSids : principals, StringComparer.Ordinal);
            if (!principals.Any(selected.Contains)) throw new PermissionRejectedException("permissions.noMatchingPrincipals");
            bool deny = request.Operation.EndsWith("deny", StringComparison.Ordinal);
            int mask = custom ? request.Mask : (int)FileSystemRights.FullControl;
            var entries = new List<CommonAce>();
            if (custom)
            {
                foreach (CommonAce ace in acl)
                {
                    if ((ace.AccessMask & ~(int)FileSystemRights.FullControl) != 0)
                        throw new PermissionRejectedException("permissions.unsupported");
                    bool matches = selected.Contains(ace.SecurityIdentifier.Value);
                    int remaining = matches ? ace.AccessMask & ~mask : ace.AccessMask;
                    if (remaining != 0 || !matches)
                        entries.Add(new CommonAce(ace.AceFlags & ~AceFlags.Inherited, ace.AceQualifier,
                            remaining, ace.SecurityIdentifier, false, null));
                }
                EnsureOrderCanBePreserved(entries);
            }
            foreach (string sid in principals.Where(selected.Contains))
                entries.Add(new CommonAce(AceFlags.None, deny ? AceQualifier.AccessDenied : AceQualifier.AccessAllowed,
                    mask, new SecurityIdentifier(sid), false, null));
            var afterAcl = new RawAcl(acl.Revision, entries.Count);
            foreach (var entry in entries.OrderBy(ace => ace.AceQualifier == AceQualifier.AccessDenied ? 0 : 1))
                afterAcl.InsertAce(afterAcl.Count, entry);
            var bytes = new byte[before.BinaryLength];
            before.GetBinaryForm(bytes, 0);
            var after = new RawSecurityDescriptor(bytes, 0) { DiscretionaryAcl = afterAcl };
            after.SetFlags((after.ControlFlags | ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected) &
                ~(ControlFlags.DiscretionaryAclAutoInherited | ControlFlags.DiscretionaryAclAutoInheritRequired));
            return after;
        }

        /// <summary>Rejects canonicalization that could turn a retained allow into a denial for overlapping principals</summary>
        /// <param name="entries">Retained rules in their original evaluation order</param>
        private static void EnsureOrderCanBePreserved(IEnumerable<CommonAce> entries)
        {
            int earlierAllows = 0;
            foreach (CommonAce ace in entries)
            {
                if (ace.AceQualifier == AceQualifier.AccessAllowed) earlierAllows |= ace.AccessMask;
                else if ((earlierAllows & ace.AccessMask) != 0)
                    throw new PermissionRejectedException("permissions.ruleOrder");
            }
        }

        /// <summary>Finds distinct existing principals without accepting unsupported ACE types</summary>
        /// <param name="descriptor">Current owner, group and DACL</param><returns>Canonical SID values in stable order</returns>
        internal static string[] GetPrincipals(RawSecurityDescriptor descriptor)
        {
            var acl = descriptor.DiscretionaryAcl;
            if (acl == null || acl.Count == 0) throw new NotSupportedException();
            var principals = new Dictionary<string, SecurityIdentifier>(StringComparer.Ordinal);
            foreach (GenericAce entry in acl)
            {
                if (!(entry is CommonAce ace) || ace.IsCallback ||
                    (ace.AceQualifier != AceQualifier.AccessAllowed && ace.AceQualifier != AceQualifier.AccessDenied))
                    throw new NotSupportedException();
                principals[ace.SecurityIdentifier.Value] = ace.SecurityIdentifier;
            }
            if (principals.Count > PermissionRequest.MaximumPrincipals) throw new PermissionRejectedException("permissions.principalLimit");
            return principals.Keys.OrderBy(sid => sid, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Compares written access rules and retained identity fields without requiring SACL access</summary>
        /// <param name="expected">Requested descriptor</param><param name="actual">Descriptor reread from the original handle</param>
        /// <returns>Whether the exact DACL and ownership fields match</returns>
        internal static bool Matches(RawSecurityDescriptor expected, RawSecurityDescriptor actual)
        {
            if (!Equals(expected.Owner, actual.Owner) || !Equals(expected.Group, actual.Group) ||
                !actual.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected) || actual.DiscretionaryAcl == null)
                return false;
            // Compare rules independently of Windows-maintained descriptor bookkeeping flags
            byte[] desired = new byte[expected.DiscretionaryAcl.BinaryLength];
            byte[] stored = new byte[actual.DiscretionaryAcl.BinaryLength];
            expected.DiscretionaryAcl.GetBinaryForm(desired, 0);
            actual.DiscretionaryAcl.GetBinaryForm(stored, 0);
            return desired.SequenceEqual(stored);
        }
    }
}
