// Validates targets and writes only their individual access control lists
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Threading;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Win10ExplorerKit.Features.Permissions;

namespace Win10ExplorerKit.Windows.Permissions
{
    internal static class PermissionAccess
    {
        // Read the DACL and ownership fields, never audit rules
        private const uint ReadSections = 7;
        // Protect and replace only the DACL without changing owner or primary group
        private const uint WriteSections = 0x80000004;
        // READ_CONTROL, WRITE_DAC and FILE_READ_ATTRIBUTES for verification after denying access
        private const uint TargetAccess = 0x00060080;
        // READ_CONTROL and FILE_READ_ATTRIBUTES allow discovery without requesting WRITE_DAC
        private const uint InspectionAccess = 0x00020080;
        // FILE_FLAG_BACKUP_SEMANTICS and FILE_FLAG_OPEN_REPARSE_POINT
        private const uint OpenFlags = 0x02200000;
        // Hold ancestor names stable while reading and writing a target
        private const uint AttributeAccess = 0x80;
        // Reject oversized or corrupted descriptors before allocation
        private const uint MaximumDescriptorBytes = 1024 * 1024;

        /// <summary>Enables only the privileges required to recover existing denied DACLs</summary>
        internal static void EnableRecoveryPrivileges()
        {
            if (!PermissionNative.OpenProcessToken(new IntPtr(-1), 0x28, out SafeAccessTokenHandle token))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using (token)
            foreach (string name in new[] { "SeBackupPrivilege", "SeRestorePrivilege" })
            {
                if (!PermissionNative.LookupPrivilegeValue(null, name, out long luid))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var state = new PermissionNative.TokenPrivilege { Count = 1, Luid = luid, Attributes = 2 };
                if (!PermissionNative.AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                int error = Marshal.GetLastWin32Error();
                if (error != 0) throw new Win32Exception(error);
            }
        }

        /// <summary>Updates one object and verifies through the original handle without traversing children</summary>
        /// <param name="path">Requested path</param><param name="request">Validated operation</param><param name="token">Cancellation before mutation</param>
        /// <returns>Sanitized result with elevation requirements separated from other failures</returns>
        internal static PermissionResult Apply(string path, PermissionRequest request, CancellationToken token)
            => Process(path, request, token, false);

        /// <summary>Reads existing principals without changing access rules or requesting write access</summary>
        /// <param name="path">Requested path</param><param name="request">Inspection scope</param><param name="token">Cancellation</param>
        /// <returns>Existing SID values or a sanitized discovery failure</returns>
        internal static PermissionResult Inspect(string path, PermissionRequest request, CancellationToken token)
            => Process(path, request, token, true);

        /// <summary>Applies identical path checks to read-only discovery and individual ACL mutations</summary>
        /// <param name="path">Requested path</param><param name="request">Operation</param><param name="token">Cancellation</param>
        /// <param name="inspect">Whether native write access is forbidden</param><returns>Sanitized operation result</returns>
        private static PermissionResult Process(string path, PermissionRequest request, CancellationToken token, bool inspect)
        {
            var handles = new List<SafeFileHandle>();
            bool written = false;
            try
            {
                if (inspect ? !request.IsInspection() : !request.IsValid()) return PermissionResult.Create(path, "skipped", "permissions.unsupported");
                token.ThrowIfCancellationRequested();
                string target = ValidatePath(path, request.ProfileRoot);
                var ancestors = new Stack<string>();
                string parent = Path.GetDirectoryName(target);
                while (!string.IsNullOrEmpty(parent)) { ancestors.Push(parent); parent = Path.GetDirectoryName(parent); }
                while (ancestors.Count > 0) handles.Add(OpenChecked(ancestors.Pop(), AttributeAccess));
                token.ThrowIfCancellationRequested();
                var file = OpenChecked(target, inspect ? InspectionAccess : TargetAccess);
                handles.Add(file);
                var resolved = new StringBuilder(32768);
                uint length = PermissionNative.GetFinalPathNameByHandle(file, resolved, (uint)resolved.Capacity, 0);
                if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (length >= resolved.Capacity) throw new NotSupportedException();
                // Resolve aliases before applying protected-path checks a second time
                string actualPath = resolved.ToString();
                if (actualPath.StartsWith(@"\\?\", StringComparison.Ordinal)) actualPath = actualPath.Substring(4);
                ValidatePath(actualPath, request.ProfileRoot);
                RawSecurityDescriptor before = Read(file);
                if (inspect)
                {
                    var result = PermissionResult.Create(path, "success", "permissions.principalsLoaded");
                    result.PrincipalSids = PermissionRules.GetPrincipals(before);
                    return result;
                }
                RawSecurityDescriptor after = PermissionRules.Transform(before, request);
                byte[] bytes = new byte[after.BinaryLength];
                after.GetBinaryForm(bytes, 0);
                token.ThrowIfCancellationRequested();
                // Keep the backup-authorized handle and avoid automatic child ACL propagation
                if (!PermissionNative.SetKernelObjectSecurity(file, WriteSections, bytes))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                written = true;
                if (!PermissionRules.Matches(after, Read(file)))
                    return PermissionResult.Create(path, "failed", "permissions.verifyFailed");
                return PermissionResult.Completed(path, request);
            }
            catch (OperationCanceledException) { return PermissionResult.Create(path, "canceled", "permissions.canceled"); }
            catch (PermissionRejectedException ex) { return PermissionResult.Create(path, "skipped", ex.Reason); }
            catch (NotSupportedException) { return PermissionResult.Create(path, "skipped", "permissions.unsupported"); }
            catch (ArgumentException) { return PermissionResult.Create(path, "skipped", "permissions.invalidPath"); }
            catch (Win32Exception ex)
            {
                string reason = ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3 ? "permissions.notFound" :
                    ex.NativeErrorCode == 5 || ex.NativeErrorCode == 1314 ? "permissions.accessDenied" :
                    ex.NativeErrorCode == 32 ? "permissions.inUse" : "permissions.nativeError";
                var result = PermissionResult.Create(path, "failed", written ? "permissions.verifyFailed" : reason, ex.NativeErrorCode);
                // Never repeat a write merely because post-write verification failed
                result.NeedsElevation = !written && (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 1314);
                AppLogger.Write("Permission operation failed, Win32 code " + ex.NativeErrorCode, ex);
                return result;
            }
            catch (Exception ex)
            {
                AppLogger.Write("Permission operation failed", ex);
                return PermissionResult.Create(path, "failed", "permissions.failed");
            }
            finally { for (int index = handles.Count - 1; index >= 0; index--) handles[index].Dispose(); }
        }

        /// <summary>Normalizes local targets and rejects protected roots before opening them</summary>
        /// <param name="path">Input path</param><param name="profile">Initiating account profile root</param>
        /// <returns>Validated absolute NTFS path</returns>
        internal static string ValidatePath(string path, string profile)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || !char.IsLetter(path[0]) ||
                path[1] != ':' || path[2] != '\\' || path.IndexOf(':', 2) >= 0)
                throw new ArgumentException(nameof(path));
            string target = Path.GetFullPath(path).TrimEnd('\\');
            if (target.Length <= 2) throw new PermissionRejectedException("permissions.protected");
            foreach (string part in target.Substring(3).Split('\\'))
                if (part.EndsWith(".") || part.EndsWith(" ")) throw new NotSupportedException();
            foreach (string protectedPath in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), profile, AppPaths.DataDirectory })
            {
                if (string.IsNullOrWhiteSpace(protectedPath)) continue;
                string root = Path.GetFullPath(protectedPath).TrimEnd('\\');
                if (Within(root, target) || (root.Equals(AppPaths.DataDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && Within(target, root)))
                    throw new PermissionRejectedException("permissions.protected");
            }
            var drive = new DriveInfo(Path.GetPathRoot(target));
            if ((drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) ||
                !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) throw new PermissionRejectedException("permissions.fileSystem");
            return target;
        }

        /// <summary>Compares canonical path boundaries rather than string prefixes alone</summary>
        /// <param name="child">Candidate path</param><param name="parent">Ancestor path</param><returns>Containment including equality</returns>
        private static bool Within(string child, string parent) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);

        /// <summary>Locks an object's name and rejects reparse points using handle metadata</summary>
        /// <param name="path">Canonical path</param><param name="access">Required access</param><returns>Owned stable handle</returns>
        private static SafeFileHandle OpenChecked(string path, uint access)
        {
            // OPEN_EXISTING with read/write sharing excludes rename and deletion while the handle is held
            var file = PermissionNative.Open(path, access, 3, IntPtr.Zero, 3, OpenFlags, IntPtr.Zero);
            if (file.IsInvalid) { int error = Marshal.GetLastWin32Error(); file.Dispose(); throw new Win32Exception(error); }
            try
            {
                if (!PermissionNative.GetFileInformationByHandle(file, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new PermissionRejectedException("permissions.link");
                // A hard-linked file can alias an otherwise protected application file
                if ((info.Attributes & (uint)FileAttributes.Directory) == 0 && info.Links > 1) throw new PermissionRejectedException("permissions.link");
                return file;
            }
            catch { file.Dispose(); throw; }
        }

        /// <summary>Reads a bounded descriptor through the already-authorized handle</summary>
        /// <param name="file">Owned handle with READ_CONTROL</param><returns>Owner, group and DACL</returns>
        private static RawSecurityDescriptor Read(SafeFileHandle file)
        {
            PermissionNative.GetKernelObjectSecurity(file, ReadSections, null, 0, out uint needed);
            if (needed == 0 || needed > MaximumDescriptorBytes) throw new Win32Exception(Marshal.GetLastWin32Error());
            var bytes = new byte[needed];
            if (!PermissionNative.GetKernelObjectSecurity(file, ReadSections, bytes, needed, out needed))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new RawSecurityDescriptor(bytes, 0);
        }
    }
}
