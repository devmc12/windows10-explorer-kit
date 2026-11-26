// Exercises the built permission service against owned NTFS fixtures and cleans them through held handles
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Win10ExplorerKit.Features.Permissions;
using Win10ExplorerKit.Windows.Permissions;

internal static class PermissionSmoke
{
    // Read owner, group and DACL without requesting audit privileges
    private const uint ReadSections = 7;
    // Replace only the fixture DACL and protect it during cleanup
    private const uint WriteSections = 0x80000004;
    // Retain read-control and DACL-write rights before any denial is applied
    private const uint RecoveryAccess = 0x60080;
    // Open directories without following a final reparse point
    private const uint OpenFlags = 0x02200000;
    private static readonly List<Fixture> Fixtures = new List<Fixture>();
    private static readonly List<string> Report = new List<string>();
    private static PermissionSession _session;
    private static string _root;
    private static string _user;
    private static int _checks;

    /// <summary>Loads the unchanged product assembly before entering native test or worker code</summary>
    /// <param name="args">Optional production worker arguments</param><returns>Zero after all checks and cleanup pass</returns>
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveProduct;
        return Run(args);
    }

    /// <summary>Resolves dependencies from the isolated copy without running the product entry point</summary>
    /// <param name="sender">Current application domain</param><param name="args">Missing assembly identity</param>
    /// <returns>The copied dependency, or null for an unrelated assembly</returns>
    private static Assembly ResolveProduct(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "product", name + (name == "Win10ExplorerKit" ? ".exe" : ".dll"));
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    /// <summary>Runs checks as an ordinary user and delegates recovery to the real elevated worker</summary>
    /// <param name="args">Optional production worker arguments</param><returns>Process result</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        if (args.Length == 3 && args[0] == "--permission-worker")
            return PermissionElevation.RunWorker(int.Parse(args[1]), args[2]);
        string workspace = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        Guid identifier;
        if (!Path.GetFileName(workspace).StartsWith("permission-smoke-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(workspace).Substring("permission-smoke-".Length), "N", out identifier)) return 2;
        _root = Path.Combine(workspace, "fixtures");
        int result = 0;
        bool created = false;
        try
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("Start this harness unelevated so access probes cannot bypass the DACL");
                _user = identity.User.Value;
            }
            if (Directory.Exists(_root) || File.Exists(_root)) throw new InvalidOperationException("Fixture root already exists");
            Directory.CreateDirectory(_root);
            created = true;
            _session = new PermissionSession();
            CheckFullOperations();
            CheckCustomOperations();
            CheckPrincipalSelection();
            CheckFolderIsolation();
            Record("PASS: " + _checks + " assertions; ordinary-user I/O and production elevated recovery");
        }
        catch (Exception error) { Record("FAIL: " + error.GetType().Name + ": " + error.Message); result = 1; }
        finally
        {
            try
            {
                if (_session != null) _session.CloseAsync().GetAwaiter().GetResult();
                foreach (Fixture fixture in Fixtures) fixture.Restore();
            }
            catch (Exception error) { Record("CLEANUP FAILED: " + error.Message); result = 1; }
            finally { foreach (Fixture fixture in Fixtures) fixture.Dispose(); }
            if (created)
            {
                try
                {
                    foreach (Fixture fixture in Fixtures.AsEnumerable().Reverse())
                    {
                        RequireOwned(fixture.Path);
                        if (fixture.Directory) System.IO.Directory.Delete(fixture.Path, false);
                        else File.Delete(fixture.Path);
                    }
                    Directory.Delete(_root, false);
                    Record("PASS: all created files and directories removed");
                }
                catch (Exception error) { Record("CLEANUP FAILED: " + error.Message); result = 1; }
            }
            Win10ExplorerKit.Diagnostics.AppLogger.FlushAsync().GetAwaiter().GetResult();
            File.WriteAllLines(Path.Combine(workspace, "report.txt"), Report);
        }
        return result;
    }

    /// <summary>Checks complete denial and granting with actual ordinary-user file I/O</summary>
    private static void CheckFullOperations()
    {
        Fixture file = Create("full control 文件.txt", false, null);
        Apply(file, "deny", 0, null);
        Expect(!CanOpen(file.Path, 0x80000000) && !CanOpen(file.Path, 0x40000000), "deny-all blocks reading and writing");
        var denied = PermissionAccess.Apply(file.Path, Request(file, "allow", 0, null), CancellationToken.None);
        Expect(denied.NeedsElevation, "denied target requests elevation instead of being treated as missing");
        Apply(file, "allow", 0, null);
        Expect(CanOpen(file.Path, 0x80000000) && CanOpen(file.Path, 0x40000000), "allow-all restores reading and writing");
        File.WriteAllText(file.Path, "restored");
        Expect(File.ReadAllText(file.Path) == "restored", "restored file supports real read/write");
        Fixture restrictedOwner = Create("restricted owner.txt", false, null);
        RawSecurityDescriptor descriptor = Read(restrictedOwner.Handle);
        descriptor.DiscretionaryAcl.InsertAce(descriptor.DiscretionaryAcl.Count, new CommonAce(AceFlags.None,
            AceQualifier.AccessAllowed, (int)FileSystemRights.ReadPermissions, new SecurityIdentifier("S-1-3-4"), false, null));
        Write(restrictedOwner.Handle, descriptor);
        Apply(restrictedOwner, "deny", 0, null);
        Expect(!CanOpen(restrictedOwner.Path, 0x40000), "OWNER RIGHTS denial removes implicit owner WRITE_DAC access");
        Apply(restrictedOwner, "allow", 0, null);
        Expect(CanOpen(restrictedOwner.Path, 0x80000000) && CanOpen(restrictedOwner.Path, 0x40000000), "recovery does not depend on implicit owner WRITE_DAC access");
        Record("PASS: file deny-all and grant-all");
    }

    /// <summary>Checks each custom category while comparing every unselected access bit</summary>
    private static void CheckCustomOperations()
    {
        foreach (FileSystemRights right in new[] { FileSystemRights.Read, FileSystemRights.Write, FileSystemRights.ExecuteFile,
            FileSystemRights.Delete, FileSystemRights.ChangePermissions, FileSystemRights.TakeOwnership })
        {
            Fixture file = Create("custom-" + right + ".txt", false, null);
            RawSecurityDescriptor before = Read(file.Handle);
            Apply(file, "custom-deny", (int)right, null);
            AssertUnchangedBits(before, Read(file.Handle), (int)right);
            if (right == FileSystemRights.Read)
                Expect(!CanOpen(file.Path, 0x80000000) && CanOpen(file.Path, 0x40000000), "read denial retains writing");
            if (right == FileSystemRights.Write)
                Expect(!CanOpen(file.Path, 0x40000000) && CanOpen(file.Path, 0x80000000), "write denial retains reading");
            if (right == FileSystemRights.ExecuteFile) Expect(!CanOpen(file.Path, 0x20000000), "execute denial blocks executable access");
            if (right == FileSystemRights.Delete) Expect(!CanOpen(file.Path, 0x10000), "delete denial blocks a DELETE handle");
            before = Read(file.Handle);
            Apply(file, "custom-allow", (int)right, null);
            AssertUnchangedBits(before, Read(file.Handle), AllowedMask((int)right));
            Expect(CanOpen(file.Path, 0x80000000) && CanOpen(file.Path, 0x40000000), "custom grant leaves ordinary I/O working");
        }
        foreach (FileSystemRights right in new[] { FileSystemRights.Read, FileSystemRights.Write, FileSystemRights.ExecuteFile })
        {
            Fixture file = Create("grant-after-deny-" + right + ".txt", false, null);
            Apply(file, "deny", 0, null);
            RawSecurityDescriptor before = Read(file.Handle);
            Apply(file, "custom-allow", (int)right, null);
            AssertUnchangedBits(before, Read(file.Handle), AllowedMask((int)right));
            uint access = right == FileSystemRights.Read ? 0x80000000u : right == FileSystemRights.Write ? 0x40000000u : 0x20000000u;
            Expect(CanOpen(file.Path, access), "custom grant restores selected I/O after deny-all: " + right);
            Expect(!CanOpen(file.Path, 0x10000), "unselected deletion remains denied");
            if (right == FileSystemRights.Read)
            {
                Expect(File.ReadAllText(file.Path) == "fixture", "custom read grant supports actual reads");
                Expect(!CanOpen(file.Path, 0x40000000), "read grant does not grant writing");
            }
            if (right == FileSystemRights.Write)
            {
                File.WriteAllText(file.Path, "written");
                Expect(!CanOpen(file.Path, 0x80000000), "write grant does not grant reading");
            }
        }
        Record("PASS: all six custom categories, unrelated masks and normal file I/O");
    }

    /// <summary>Checks a selected identity without changing another identity's access rules</summary>
    private static void CheckPrincipalSelection()
    {
        Fixture file = Create("selected principal.txt", false, null);
        RawSecurityDescriptor before = Read(file.Handle);
        Apply(file, "custom-deny", (int)FileSystemRights.Write, new[] { _user });
        Expect(Project(before, ~0, "S-1-5-18") == Project(Read(file.Handle), ~0, "S-1-5-18"), "unselected SYSTEM rules are unchanged");
        Expect(!CanOpen(file.Path, 0x40000000) && CanOpen(file.Path, 0x80000000), "selected user's write denial is effective");
        Apply(file, "custom-allow", (int)FileSystemRights.Write, new[] { _user });
        Expect(CanOpen(file.Path, 0x40000000), "selected user's writing is restored");
        Expect(Project(before, ~0, "S-1-5-18") == Project(Read(file.Handle), ~0, "S-1-5-18"), "unselected SYSTEM rules remain unchanged after grant");
        Record("PASS: selected and unselected principals");
    }

    /// <summary>Verifies that operations on a folder do not rewrite existing child security descriptors</summary>
    private static void CheckFolderIsolation()
    {
        Fixture folder = Create("folder 中文", true, null);
        Fixture child = Create("child.txt", false, folder);
        Fixture nested = Create("nested", true, folder);
        Fixture descendant = Create("grandchild.txt", false, nested);
        Fixture[] children = { child, nested, descendant };
        string[] snapshots = children.Select(item => Read(item.Handle).GetSddlForm(AccessControlSections.All)).ToArray();
        foreach (string operation in new[] { "custom-deny", "custom-allow", "deny", "allow" })
        {
            RawSecurityDescriptor before = Read(folder.Handle);
            int mask = operation.StartsWith("custom-", StringComparison.Ordinal) ? (int)FileSystemRights.Write : 0;
            Apply(folder, operation, mask, null);
            if (mask != 0) AssertUnchangedBits(before, Read(folder.Handle), operation == "custom-allow" ? AllowedMask(mask) : mask);
            for (int index = 0; index < children.Length; index++)
                Expect(snapshots[index] == Read(children[index].Handle).GetSddlForm(AccessControlSections.All), "child descriptor unchanged after " + operation);
            Expect(File.ReadAllText(child.Path) == "fixture" && File.ReadAllText(descendant.Path) == "fixture", "child reads remain available after " + operation);
        }
        Record("PASS: folder, child file, nested directory and grandchild isolation");
    }

    /// <summary>Builds a fixed operation request for a fixture that this harness created</summary>
    /// <param name="fixture">Owned target</param><param name="operation">Supported operation</param><param name="mask">Custom rights</param>
    /// <param name="principals">Optional existing identity selection</param><returns>Product request</returns>
    private static PermissionRequest Request(Fixture fixture, string operation, int mask, string[] principals)
    {
        RequireOwned(fixture.Path);
        return new PermissionRequest { Operation = operation, Mask = mask, PrincipalSids = principals,
            Paths = new[] { fixture.Path }, ProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
    }

    /// <summary>Invokes the product service, including its authenticated elevated helper when required</summary>
    /// <param name="fixture">Owned target</param><param name="operation">Supported operation</param><param name="mask">Custom rights</param>
    /// <param name="principals">Optional identity selection</param>
    private static void Apply(Fixture fixture, string operation, int mask, string[] principals)
    {
        RawSecurityDescriptor before = Read(fixture.Handle);
        PermissionResult result = _session.ProcessAsync(fixture.Path, Request(fixture, operation, mask, principals), CancellationToken.None).GetAwaiter().GetResult();
        Expect(result.Status == "success", operation + " returned " + result.Status + "/" + result.Reason + "/" + result.ErrorCode);
        RawSecurityDescriptor after = Read(fixture.Handle);
        Expect(before.Owner == after.Owner && before.Group == after.Group, "owner and primary group remain unchanged");
        Expect(PermissionRules.GetPrincipals(before).SequenceEqual(PermissionRules.GetPrincipals(after)), "existing SID set remains unchanged");
    }

    /// <summary>Creates a new fixture and captures a cleanup handle before changing its DACL</summary>
    /// <param name="name">Single fixture name</param><param name="directory">Whether to create a directory</param>
    /// <param name="parent">Optional owned fixture parent</param><returns>Owned fixture</returns>
    private static Fixture Create(string name, bool directory, Fixture parent)
    {
        string path = Path.Combine(parent == null ? _root : parent.Path, name);
        RequireOwned(path);
        if (File.Exists(path) || Directory.Exists(path)) throw new InvalidOperationException("Refusing to reuse an existing target");
        if (directory) Directory.CreateDirectory(path);
        else using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream)) writer.Write("fixture");
        var fixture = new Fixture { Path = path, Directory = directory, Handle = Open(path, RecoveryAccess) };
        fixture.Original = Read(fixture.Handle);
        Fixtures.Add(fixture);
        if (parent == null)
        {
            RawSecurityDescriptor baseline = Read(fixture.Handle);
            var acl = new RawAcl(GenericAcl.AclRevision, 2);
            AceFlags flags = directory ? AceFlags.ObjectInherit | AceFlags.ContainerInherit : AceFlags.None;
            foreach (string sid in new[] { _user, "S-1-5-18" })
                acl.InsertAce(acl.Count, new CommonAce(flags, AceQualifier.AccessAllowed, (int)FileSystemRights.FullControl, new SecurityIdentifier(sid), false, null));
            baseline.DiscretionaryAcl = acl;
            baseline.SetFlags(baseline.ControlFlags | ControlFlags.DiscretionaryAclProtected);
            Write(fixture.Handle, baseline);
        }
        return fixture;
    }

    /// <summary>Rejects every path outside the exact newly created fixture root</summary>
    /// <param name="path">Candidate target</param>
    private static void RequireOwned(string path)
    {
        if (!Path.GetFullPath(path).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Target is outside the fixture root");
    }

    /// <summary>Checks retained access bits independently of the product's transformation algorithm</summary>
    /// <param name="before">Original descriptor</param><param name="after">Written descriptor</param><param name="changed">Selected bits and I/O prerequisites</param>
    private static void AssertUnchangedBits(RawSecurityDescriptor before, RawSecurityDescriptor after, int changed)
    {
        Expect(Project(before, ~changed, null) == Project(after, ~changed, null), "unselected access bits retain their original qualifier, flags and identity");
    }

    /// <summary>Projects retained rules into a stable representation for comparison</summary>
    /// <param name="descriptor">Native descriptor</param><param name="mask">Bits to retain</param><param name="sid">Optional identity filter</param>
    /// <returns>Projected rules, retaining relative order</returns>
    private static string Project(RawSecurityDescriptor descriptor, int mask, string sid)
    {
        return string.Join("|", descriptor.DiscretionaryAcl.Cast<CommonAce>().Where(ace => (sid == null || ace.SecurityIdentifier.Value == sid) && (ace.AccessMask & mask) != 0)
            .Select(ace => ace.SecurityIdentifier.Value + ":" + ace.AceQualifier + ":" + (ace.AceFlags & ~AceFlags.Inherited) + ":" + (ace.AccessMask & mask)));
    }

    /// <summary>Includes the documented Windows I/O prerequisites for a custom grant</summary>
    /// <param name="mask">Selected rights</param><returns>Rights permitted to change</returns>
    private static int AllowedMask(int mask)
    {
        if ((mask & (int)(FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.ExecuteFile)) != 0)
            mask |= (int)(FileSystemRights.Synchronize | FileSystemRights.ReadPermissions);
        if ((mask & (int)FileSystemRights.ExecuteFile) != 0) mask |= (int)FileSystemRights.ReadAttributes;
        return mask;
    }

    /// <summary>Opens a native handle without impersonation or enabled backup privileges</summary>
    /// <param name="path">Fixture path</param><param name="access">Desired rights</param><returns>Owned handle</returns>
    private static SafeFileHandle Open(string path, uint access)
    {
        var handle = PermissionNative.Open(path, access, 3, IntPtr.Zero, 3, OpenFlags, IntPtr.Zero);
        if (handle.IsInvalid) { int code = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(code); }
        return handle;
    }

    /// <summary>Uses the operating system to evaluate actual access for the ordinary user</summary>
    /// <param name="path">Fixture path</param><param name="access">Native requested access</param><returns>Whether the open succeeds</returns>
    private static bool CanOpen(string path, uint access)
    {
        using (var handle = PermissionNative.Open(path, access, 3, IntPtr.Zero, 3, OpenFlags, IntPtr.Zero))
        {
            if (!handle.IsInvalid) return true;
            int error = Marshal.GetLastWin32Error();
            if (error != 5) throw new Win32Exception(error);
            return false;
        }
    }

    /// <summary>Reads security through the recovery handle even after access is denied</summary>
    /// <param name="handle">Preauthorized fixture handle</param><returns>Current descriptor</returns>
    private static RawSecurityDescriptor Read(SafeFileHandle handle)
    {
        uint needed;
        PermissionNative.GetKernelObjectSecurity(handle, ReadSections, null, 0, out needed);
        if (needed == 0 || needed > 1048576) throw new Win32Exception(Marshal.GetLastWin32Error());
        var bytes = new byte[needed];
        if (!PermissionNative.GetKernelObjectSecurity(handle, ReadSections, bytes, needed, out needed)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new RawSecurityDescriptor(bytes, 0);
    }

    /// <summary>Sets only the fixture DACL through its preauthorized cleanup handle</summary>
    /// <param name="handle">Owned fixture handle</param><param name="descriptor">Fixture baseline or cleanup descriptor</param>
    private static void Write(SafeFileHandle handle, RawSecurityDescriptor descriptor)
    {
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        if (!SetKernelObjectSecurity(handle, WriteSections, bytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>Counts checks and stops before release when an expectation fails</summary>
    /// <param name="condition">Observed result</param><param name="message">Non-sensitive diagnostic</param>
    private static void Expect(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>Writes a compact report without fixture paths or identity values</summary>
    /// <param name="message">Result text</param>
    private static void Record(string message) { Report.Add(message); Console.WriteLine(message); }

    /// <summary>Updates a preauthorized fixture handle without propagating child ACLs</summary>
    /// <param name="handle">Owned handle</param><param name="sections">DACL flags</param><param name="descriptor">Binary descriptor</param><returns>Native success</returns>
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint sections, byte[] descriptor);

    private sealed class Fixture : IDisposable
    {
        internal string Path;
        internal bool Directory;
        internal SafeFileHandle Handle;
        internal RawSecurityDescriptor Original;

        /// <summary>Restores the fixture's initial access through the handle acquired before testing</summary>
        internal void Restore() { RequireOwned(Path); Write(Handle, Original); }
        /// <summary>Releases the fixture lock after recovery</summary>
        public void Dispose() { Handle.Dispose(); }
    }
}
