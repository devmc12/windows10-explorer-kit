// Declares native security descriptor, handle and privilege APIs
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Win10ExplorerKit.Windows.Permissions
{
    internal static class PermissionNative
    {
        /// <summary>Opens a file or directory without following its final reparse point</summary>
        /// <param name="path">Local path</param><param name="access">Required access mask</param><param name="share">Allowed sharing</param>
        /// <param name="security">Null security attributes</param><param name="creation">Open disposition</param><param name="flags">Backup and reparse flags</param>
        /// <param name="template">Null template</param><returns>Owned file handle</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        internal static extern SafeFileHandle Open(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        /// <summary>Reads identity, attributes and volume information from the opened object</summary>
        /// <param name="file">Owned handle</param><param name="info">Native metadata</param><returns>Success</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);

        /// <summary>Resolves short-name aliases through the held object handle</summary>
        /// <param name="file">Stable handle</param><param name="path">Destination</param><param name="size">Character capacity</param>
        /// <param name="flags">Zero for normalized DOS path</param><returns>Path length or zero on failure</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
        internal static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileInformation
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        /// <summary>Reads the security descriptor using rights acquired before applying a deny rule</summary>
        /// <param name="file">Owned handle</param><param name="sections">Security sections</param><param name="buffer">Optional destination</param>
        /// <param name="size">Buffer capacity</param><param name="needed">Required size</param><returns>Success</returns>
        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool GetKernelObjectSecurity(SafeFileHandle file, uint sections, byte[] buffer, uint size, out uint needed);

        /// <summary>Writes only the named object's DACL without automatic child propagation</summary>
        /// <param name="path">Validated stable path</param><param name="sections">Protected DACL flags</param><param name="descriptor">Binary descriptor</param>
        /// <returns>Success</returns>
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetFileSecurityW")]
        internal static extern bool SetFileSecurity(string path, uint sections, byte[] descriptor);

        /// <summary>Opens the current process token for temporary privilege adjustment</summary>
        /// <param name="process">Process pseudo handle</param><param name="access">Token access</param><param name="token">Owned token</param><returns>Success</returns>
        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

        /// <summary>Resolves a well-known privilege name on this machine</summary>
        /// <param name="system">Null for local system</param><param name="name">Privilege name</param><param name="luid">Privilege identity</param><returns>Success</returns>
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool LookupPrivilegeValue(string system, string name, out long luid);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct TokenPrivilege { internal uint Count; internal long Luid; internal uint Attributes; }

        /// <summary>Enables one privilege held by the elevated process token</summary>
        /// <param name="token">Owned token</param><param name="disable">False to use requested state</param><param name="state">Requested privilege</param>
        /// <param name="length">Zero without previous state</param><param name="previous">Null previous state</param><param name="required">Null required size</param>
        /// <returns>API success, with ERROR_NOT_ALL_ASSIGNED checked separately</returns>
        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disable, ref TokenPrivilege state, uint length, IntPtr previous, IntPtr required);
    }
}
