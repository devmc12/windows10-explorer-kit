// Defines transient permission requests and operation results
using System;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Win10ExplorerKit.Features.Permissions
{
    internal sealed class PermissionRejectedException : NotSupportedException
    {
        internal string Reason { get; }
        /// <summary>Carries a safe localized rejection reason without target data</summary><param name="reason">Locale key</param>
        internal PermissionRejectedException(string reason) => Reason = reason;
    }

    internal sealed class PermissionRequest
    {
        public string Operation { get; set; }
        public int Mask { get; set; }
        public string[] Paths { get; set; }
        public string[] PrincipalSids { get; set; }
        // Capture the initiating profile even when UAC uses a different administrator account
        public string ProfileRoot { get; set; }

        /// <summary>Rejects unsupported operations and masks before any native access</summary>
        /// <returns>Whether this request contains a supported ACL operation</returns>
        internal bool IsValid()
        {
            if (Operation == "deny" || Operation == "allow") return Mask == 0 && PrincipalSids == null;
            if ((Operation != "custom-deny" && Operation != "custom-allow") || Mask == 0 || (Mask & ~CustomMask) != 0)
                return false;
            if (PrincipalSids == null) return true;
            if (PrincipalSids.Length == 0 || PrincipalSids.Length > MaximumPrincipals) return false;
            try
            {
                return PrincipalSids.All(sid => sid != null && sid.Length <= MaximumSidCharacters &&
                    new SecurityIdentifier(sid).Value == sid) && PrincipalSids.Distinct(StringComparer.Ordinal).Count() == PrincipalSids.Length;
            }
            catch (ArgumentException) { return false; }
        }

        /// <summary>Recognizes the worker's read-only discovery request separately from mutation requests</summary>
        /// <returns>Whether only supported inspection fields are present</returns>
        internal bool IsInspection() => Operation == "inspect" && Mask == 0 && PrincipalSids == null;

        // Bound the union of selected principals and private worker payload sizes
        internal const int MaximumPrincipals = 4096;
        // Maximum canonical string length of a Windows security identifier
        private const int MaximumSidCharacters = 184;

        // User-selectable rights, excluding permissions that implicitly change children
        internal const int CustomMask = (int)(FileSystemRights.Read | FileSystemRights.Write |
            FileSystemRights.ExecuteFile | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership);
    }

    internal sealed class PermissionResult
    {
        public string Path { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public int ErrorCode { get; set; }
        public int Rights { get; set; }
        public bool NeedsElevation { get; set; }
        public string[] PrincipalSids { get; set; }

        /// <summary>Creates a plain result without exposing native exception messages</summary>
        /// <param name="path">Requested item</param><param name="status">Outcome category</param><param name="reason">Locale key</param>
        /// <param name="code">Optional native error</param><returns>Serializable result</returns>
        internal static PermissionResult Create(string path, string status, string reason, int code = 0) =>
            new PermissionResult { Path = path, Status = status, Reason = reason, ErrorCode = code };

        /// <summary>Describes the operation that was verified instead of reporting a generic success</summary>
        /// <param name="path">Processed target</param><param name="request">Applied operation and rights</param>
        /// <returns>Localized success metadata without a permission history</returns>
        internal static PermissionResult Completed(string path, PermissionRequest request)
        {
            string reason;
            switch (request.Operation)
            {
                case "deny": reason = "permissions.deniedAll"; break;
                case "allow": reason = "permissions.allowedAll"; break;
                case "custom-deny": reason = "permissions.deniedRights"; break;
                case "custom-allow": reason = "permissions.allowedRights"; break;
                default: throw new ArgumentException(nameof(request));
            }
            var result = Create(path, "success", reason);
            result.Rights = request.Mask;
            return result;
        }
    }
}
