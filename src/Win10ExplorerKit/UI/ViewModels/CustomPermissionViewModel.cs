// Holds a custom permission dialog's transient principal and rights selections
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using Win10ExplorerKit.Features.Permissions;

namespace Win10ExplorerKit.UI.ViewModels
{
    internal sealed class PermissionChoice : ObservableObject
    {
        private readonly Action _changed;
        private bool _selected;
        public string Label { get; }
        public string Sid { get; }
        public int Mask { get; }
        public bool IsSelected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; Changed(); _changed(); }
        }

        /// <summary>Creates one independently selectable principal or permission</summary>
        /// <param name="label">Localized or resolved display label</param><param name="changed">Selection callback</param>
        /// <param name="sid">Optional existing principal</param><param name="mask">Optional permission bits</param>
        /// <param name="selected">Initial check state</param>
        internal PermissionChoice(string label, Action changed, string sid = null, int mask = 0, bool selected = false)
        { Label = label; _changed = changed; Sid = sid; Mask = mask; _selected = selected; }
    }

    internal sealed class CustomPermissionViewModel : ObservableObject
    {
        private bool _loading = true, _selectingAll, _deny = false;
        private string[] _eligiblePaths = Array.Empty<string>();
        private string _status = TranslationService.T("permissions.loadingPrincipals");
        public PermissionChoice[] Principals { get; private set; } = Array.Empty<PermissionChoice>();
        public PermissionChoice[] Rights { get; }
        public bool IsDeny { get => _deny; set { if (value) SelectOperation(true); } }
        public bool IsAllow { get => !_deny; set { if (value) SelectOperation(false); } }
        public bool IsLoading => _loading;
        public bool HasPrincipals => !_loading && Principals.Length > 0;
        public bool CanApply => HasPrincipals && Principals.Any(item => item.IsSelected) && Rights.Any(item => item.IsSelected);
        public string Status => _status;
        public bool HasStatus => !string.IsNullOrEmpty(_status);
        public string SelectionLabel => TranslationService.T("permissions.principalsSelected", Principals.Count(item => item.IsSelected), Principals.Length);
        public bool? AllSelected
        {
            get => Principals.Length == 0 || Principals.All(item => !item.IsSelected) ? false :
                Principals.All(item => item.IsSelected) ? true : (bool?)null;
            set
            {
                _selectingAll = true;
                foreach (var principal in Principals) principal.IsSelected = value != false;
                _selectingAll = false;
                RefreshSelection();
            }
        }

        public bool? AllRightsSelected
        {
            get => Rights.All(item => !item.IsSelected) ? false : Rights.All(item => item.IsSelected) ? true : (bool?)null;
            set
            {
                _selectingAll = true;
                foreach (var right in Rights) right.IsSelected = value != false;
                _selectingAll = false;
                RefreshSelection();
            }
        }

        /// <summary>Keeps the two operation radio buttons mutually exclusive</summary>
        /// <param name="deny">Whether selected permissions should be denied</param>
        private void SelectOperation(bool deny)
        {
            if (_deny == deny) return;
            _deny = deny;
            Changed(nameof(IsDeny)); Changed(nameof(IsAllow));
        }

        /// <summary>Starts with no selected rights and an explicit allow operation</summary>
        internal CustomPermissionViewModel()
        {
            var rights = new[] { FileSystemRights.Read, FileSystemRights.Write, FileSystemRights.ExecuteFile,
                FileSystemRights.Delete, FileSystemRights.ChangePermissions, FileSystemRights.TakeOwnership };
            string[] keys = { "read", "write", "execute", "delete", "change", "owner" };
            Rights = rights.Select((right, index) => new PermissionChoice(TranslationService.T("permissions." + keys[index]),
                RefreshSelection, mask: (int)right)).ToArray();
        }

        /// <summary>Reads each target in the background and combines only its existing principals</summary>
        /// <param name="paths">Original target snapshot</param><param name="session">Shared optional elevated worker</param>
        /// <param name="report">Shows discovery failures on their original rows</param><param name="token">Dialog cancellation</param>
        /// <returns>Completion after discovery or cancellation without any ACL writes</returns>
        internal async Task LoadAsync(string[] paths, PermissionSession session, Action<PermissionResult> report, CancellationToken token)
        {
            try
            {
                var sids = new HashSet<string>(StringComparer.Ordinal);
                var eligible = new List<string>();
                var request = new PermissionRequest { Operation = "inspect", ProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
                foreach (string path in paths)
                {
                    token.ThrowIfCancellationRequested();
                    var result = await session.ProcessAsync(path, request, token);
                    token.ThrowIfCancellationRequested();
                    if (result.Status != "success") { report(result); continue; }
                    sids.UnionWith(result.PrincipalSids);
                    if (sids.Count > PermissionRequest.MaximumPrincipals) throw new PermissionRejectedException("permissions.principalLimit");
                    eligible.Add(path);
                }
                var labels = await PermissionPrincipalNames.ResolveAsync(sids.ToArray(), token);
                token.ThrowIfCancellationRequested();
                Principals = labels.OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new PermissionChoice(pair.Value, RefreshSelection, sid: pair.Key, selected: true)).ToArray();
                _eligiblePaths = eligible.ToArray();
                _status = eligible.Count == 0 ? TranslationService.T("permissions.noPrincipals") : eligible.Count < paths.Length
                    ? TranslationService.T("permissions.partialPrincipals", eligible.Count, paths.Length)
                    : string.Empty;
            }
            catch (OperationCanceledException) { }
            catch (PermissionRejectedException ex) { _status = TranslationService.T(ex.Reason); }
            catch (Exception ex) { AppLogger.Write("Permission principal discovery failed", ex); _status = TranslationService.T("permissions.failed"); }
            finally
            {
                _loading = false;
                Changed(nameof(IsLoading)); Changed(nameof(HasPrincipals)); Changed(nameof(Principals)); Changed(nameof(Status)); Changed(nameof(HasStatus));
                RefreshSelection();
            }
        }

        /// <summary>Captures explicit identities so later ACL changes cannot silently widen the selected scope</summary>
        /// <returns>Validated custom request or no operation when the selection is incomplete</returns>
        internal PermissionRequest CreateRequest() => !CanApply ? null : new PermissionRequest
        {
            Operation = IsDeny ? "custom-deny" : "custom-allow",
            Mask = Rights.Where(item => item.IsSelected).Aggregate(0, (mask, item) => mask | item.Mask),
            PrincipalSids = Principals.Where(item => item.IsSelected).Select(item => item.Sid).ToArray(),
            Paths = (string[])_eligiblePaths.Clone(),
            ProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        /// <summary>Refreshes validation and the tri-state select-all control without repeated bulk calculations</summary>
        private void RefreshSelection()
        {
            if (_selectingAll) return;
            Changed(nameof(AllSelected)); Changed(nameof(AllRightsSelected)); Changed(nameof(SelectionLabel)); Changed(nameof(CanApply));
        }
    }
}
