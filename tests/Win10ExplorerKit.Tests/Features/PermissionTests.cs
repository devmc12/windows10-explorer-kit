// Covers ACL transformation and selection transactions without filesystem permission changes
using System;
using System.Linq;
using System.Security.AccessControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.App.Activation;
using Win10ExplorerKit.Features.Permissions;

namespace Win10ExplorerKit.Tests.Features
{
    [TestClass]
    public sealed class PermissionTests
    {
        /// <summary>Verifies deny and grant operations preserve identities without inheritable new rules</summary>
        [TestMethod]
        public void FullOperationsRetainPrincipalsAndOwnership()
        {
            var original = new RawSecurityDescriptor("O:BAG:BAD:AI(A;OICI;FR;;;BU)(A;;FA;;;BA)");
            foreach (string operation in new[] { "deny", "allow" })
            {
                var after = PermissionRules.Transform(original, new PermissionRequest { Operation = operation });
                Assert.AreEqual(original.Owner, after.Owner);
                Assert.AreEqual(original.Group, after.Group);
                CollectionAssert.AreEquivalent(original.DiscretionaryAcl.Cast<CommonAce>().Select(ace => ace.SecurityIdentifier.Value).ToArray(),
                    after.DiscretionaryAcl.Cast<CommonAce>().Select(ace => ace.SecurityIdentifier.Value).ToArray());
                foreach (CommonAce ace in after.DiscretionaryAcl)
                {
                    Assert.AreEqual((int)FileSystemRights.FullControl, ace.AccessMask);
                    Assert.AreEqual(AceFlags.None, ace.AceFlags);
                    Assert.AreEqual(operation == "deny" ? AceQualifier.AccessDenied : AceQualifier.AccessAllowed, ace.AceQualifier);
                }
                Assert.IsTrue(after.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected));
            }
        }

        /// <summary>Removes conflicting selected bits while preserving unrelated rules</summary>
        [TestMethod]
        public void CustomAllowKeepsUnselectedDenials()
        {
            var original = new RawSecurityDescriptor("O:BAG:BAD:P(D;;FA;;;BU)(A;;FR;;;BA)");
            int mask = (int)FileSystemRights.Write;
            var after = PermissionRules.Transform(original, new PermissionRequest { Operation = "custom-allow", Mask = mask });
            var denied = after.DiscretionaryAcl.Cast<CommonAce>().Where(ace => ace.AceQualifier == AceQualifier.AccessDenied).ToArray();
            Assert.AreEqual(1, denied.Length);
            Assert.AreEqual((int)FileSystemRights.FullControl & ~mask, denied[0].AccessMask);
            Assert.AreEqual(0, denied[0].AccessMask & mask);
            foreach (string sid in original.DiscretionaryAcl.Cast<CommonAce>().Select(ace => ace.SecurityIdentifier.Value))
                Assert.IsTrue(after.DiscretionaryAcl.Cast<CommonAce>().Any(ace => ace.SecurityIdentifier.Value == sid &&
                    ace.AceQualifier == AceQualifier.AccessAllowed && ace.AccessMask == mask));
        }

        /// <summary>Changes only selected principals and retains unselected ACE flags and access masks</summary>
        [TestMethod]
        public void CustomSelectionLeavesOtherPrincipalsUnchanged()
        {
            var original = new RawSecurityDescriptor("O:BAG:BAD:AI(D;;FA;;;BU)(A;OICIID;FR;;;BA)(A;;FR;;;SY)");
            var before = original.DiscretionaryAcl.Cast<CommonAce>().ToArray();
            string selected = before[0].SecurityIdentifier.Value;
            int mask = (int)FileSystemRights.Write;
            var after = PermissionRules.Transform(original, new PermissionRequest
                { Operation = "custom-allow", Mask = mask, PrincipalSids = new[] { selected } });
            foreach (var retained in before.Skip(1))
            {
                var actual = after.DiscretionaryAcl.Cast<CommonAce>().Single(ace => ace.SecurityIdentifier == retained.SecurityIdentifier);
                Assert.AreEqual(retained.AccessMask, actual.AccessMask);
                Assert.AreEqual(retained.AceQualifier, actual.AceQualifier);
                Assert.AreEqual(retained.AceFlags & ~AceFlags.Inherited, actual.AceFlags);
            }
            Assert.IsTrue(after.DiscretionaryAcl.Cast<CommonAce>().Any(ace => ace.SecurityIdentifier.Value == selected &&
                ace.AceQualifier == AceQualifier.AccessAllowed && ace.AccessMask == mask));
            CollectionAssert.AreEqual(PermissionRules.GetPrincipals(original), PermissionRules.GetPrincipals(after));
            Assert.AreEqual(original.Owner, after.Owner);
            Assert.AreEqual(original.Group, after.Group);
        }

        /// <summary>Filters a multi-target union without adding principals missing from the current object</summary>
        [TestMethod]
        public void PrincipalUnionNeverAddsAccounts()
        {
            var original = new RawSecurityDescriptor("O:BAG:BAD:(A;;FR;;;BU)(A;;FR;;;BA)");
            string[] existing = PermissionRules.GetPrincipals(original);
            var after = PermissionRules.Transform(original, new PermissionRequest { Operation = "custom-deny",
                Mask = (int)FileSystemRights.Delete, PrincipalSids = existing.Concat(new[] { "S-1-5-18" }).ToArray() });
            CollectionAssert.AreEqual(existing, PermissionRules.GetPrincipals(after));
            Assert.ThrowsException<PermissionRejectedException>(() => PermissionRules.Transform(original,
                new PermissionRequest { Operation = "custom-deny", Mask = (int)FileSystemRights.Delete, PrincipalSids = new[] { "S-1-5-18" } }));
        }

        /// <summary>Matches previous all-principal behavior when the dialog's entire list is selected</summary>
        [TestMethod]
        public void SelectingAllMatchesTheExistingCustomOperation()
        {
            var original = new RawSecurityDescriptor("O:BAG:BAD:(D;;FW;;;BU)(A;;FR;;;BU)(A;;FA;;;BA)");
            foreach (string operation in new[] { "custom-deny", "custom-allow" })
            {
                var request = new PermissionRequest { Operation = operation, Mask = (int)FileSystemRights.Read };
                var expected = PermissionRules.Transform(original, request);
                request.PrincipalSids = PermissionRules.GetPrincipals(original);
                Assert.IsTrue(PermissionRules.Matches(expected, PermissionRules.Transform(original, request)));
            }
        }

        /// <summary>Retains an unselected zero-mask ACE rather than dropping its principal</summary>
        [TestMethod]
        public void UnselectedZeroMaskPrincipalIsRetained()
        {
            var original = new RawSecurityDescriptor("O:BAG:BAD:(A;;FR;;;BU)(A;;FR;;;BA)");
            ((CommonAce)original.DiscretionaryAcl[1]).AccessMask = 0;
            var after = PermissionRules.Transform(original, new PermissionRequest { Operation = "custom-deny",
                Mask = (int)FileSystemRights.Delete,
                PrincipalSids = new[] { ((CommonAce)original.DiscretionaryAcl[0]).SecurityIdentifier.Value } });
            CollectionAssert.AreEqual(PermissionRules.GetPrincipals(original), PermissionRules.GetPrincipals(after));
        }

        /// <summary>Rejects empty, invalid or duplicate identity filters before native access</summary>
        [TestMethod]
        public void InvalidPrincipalSelectionsAreRejected()
        {
            foreach (string[] selection in new[] { Array.Empty<string>(), new[] { "not-a-sid" }, new[] { (string)null }, new[] { "S-1-5-18", "S-1-5-18" } })
                Assert.IsFalse(new PermissionRequest { Operation = "custom-allow", Mask = (int)FileSystemRights.Read, PrincipalSids = selection }.IsValid());
            Assert.IsFalse(new PermissionRequest { Operation = "allow", PrincipalSids = new[] { "S-1-5-18" } }.IsValid());
            Assert.IsFalse(new PermissionRequest { Operation = "inspect" }.IsValid());
            Assert.IsTrue(new PermissionRequest { Operation = "inspect" }.IsInspection());
        }

        /// <summary>Does not fabricate principals for empty or special descriptors</summary>
        [TestMethod]
        public void UnsupportedRulesAreRejected()
        {
            Assert.ThrowsException<NotSupportedException>(() => PermissionRules.Transform(
                new RawSecurityDescriptor("O:BAG:BAD:"), new PermissionRequest { Operation = "deny" }));
            var special = new RawSecurityDescriptor("O:BAG:BAD:(OA;;FR;11111111-1111-1111-1111-111111111111;;BU)");
            Assert.ThrowsException<NotSupportedException>(() => PermissionRules.Transform(special, new PermissionRequest { Operation = "allow" }));
        }

        /// <summary>Does not run partial selections and suppresses acknowledgement replays</summary>
        [TestMethod]
        public void BatchCommitsOnceAfterAllPathsArrive()
        {
            int calls = 0;
            var inbox = new PermissionInbox(request => { calls++; Assert.AreEqual(2, request.Paths.Length); return true; });
            string id = Guid.NewGuid().ToString("N");
            Assert.AreEqual(0, inbox.Receive(new ActivationRequest { Action = "begin-permissions", BatchId = id, Total = 2, Operation = "deny" }));
            Assert.AreEqual(0, inbox.Receive(new ActivationRequest { Action = "append-permissions", BatchId = id, Sequence = 0, Paths = new[] { @"C:\Example\a.txt" } }));
            Assert.AreEqual(0, calls);
            Assert.AreEqual(0, inbox.Receive(new ActivationRequest { Action = "append-permissions", BatchId = id, Sequence = 1, Paths = new[] { @"C:\Example\b.txt" } }));
            var end = new ActivationRequest { Action = "end-permissions", BatchId = id, Sequence = 2 };
            Assert.AreEqual(0, inbox.Receive(end));
            Assert.AreEqual(0, inbox.Receive(end));
            Assert.AreEqual(1, calls);
        }

        /// <summary>Aborts out-of-order and incomplete selections without touching files</summary>
        [TestMethod]
        public void InvalidBatchNeverExecutes()
        {
            int calls = 0;
            var inbox = new PermissionInbox(request => { calls++; return true; });
            string id = Guid.NewGuid().ToString("N");
            inbox.Receive(new ActivationRequest { Action = "begin-permissions", BatchId = id, Total = 1, Operation = "allow" });
            Assert.AreEqual(-1, inbox.Receive(new ActivationRequest { Action = "append-permissions", BatchId = id, Sequence = 1, Paths = new[] { @"C:\Example\a.txt" } }));
            Assert.AreEqual(-1, inbox.Receive(new ActivationRequest { Action = "end-permissions", BatchId = id }));
            Assert.AreEqual(0, calls);
        }

        /// <summary>Accepts ordinary Windows bookkeeping changes without hiding changed access rules</summary>
        [TestMethod]
        public void VerificationChecksRulesAndOwnership()
        {
            var desired = PermissionRules.Transform(new RawSecurityDescriptor("O:BAG:BAD:(A;;FR;;;BU)"), new PermissionRequest { Operation = "deny" });
            byte[] bytes = new byte[desired.BinaryLength]; desired.GetBinaryForm(bytes, 0);
            var actual = new RawSecurityDescriptor(bytes, 0);
            actual.SetFlags(actual.ControlFlags | ControlFlags.DiscretionaryAclAutoInherited);
            Assert.IsTrue(PermissionRules.Matches(desired, actual));
            ((CommonAce)actual.DiscretionaryAcl[0]).AccessMask = (int)FileSystemRights.Read;
            Assert.IsFalse(PermissionRules.Matches(desired, actual));
        }
    }
}
