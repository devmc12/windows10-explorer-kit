using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Win10ExplorerKit.App.Activation;
namespace Win10ExplorerKit.App.Hashing
{
    internal sealed class HashApplicationContext : IDisposable
    {
        // Shell filesystem display-name identifier from SIGDN
        private const uint FileSystemPath = 0x80058000;
        // Bound path batches by both count and encoded payload size
        private const int SelectionBatchSize = 128;
        // UTF-8 uses at most three bytes per UTF-16 code unit for these paths
        private const int BatchCharacters = 200000;
        // Retain an unused activation briefly while Explorer acquires its command
        private const int ActivationGraceSeconds = 30;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly DispatcherTimer _lifetime = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        private readonly SingleInstance _instance;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly Func<ActivationRequest, bool, Task<int>> _forward;
        private readonly HashClassFactory _factory;
        private DateTime _lastActivity = DateTime.UtcNow;
        private uint _cookie;
        private readonly List<HashClassFactory> _permissionFactories = new List<HashClassFactory>();
        private readonly List<uint> _permissionCookies = new List<uint>();
        private int _requests;
        private int _locks;
        private bool _stopping;
        /// <summary>Publishes a lightweight COM selection forwarder on its owning STA</summary>
        /// <param name="settings">Loaded locale for failure feedback</param><param name="instance">Process ownership</param><param name="classId">Optional isolated test identity</param>
        /// <param name="forward">Optional isolated selection receiver for integration checks</param>
        internal HashApplicationContext(SettingsService settings, SingleInstance instance, Guid? classId = null, Func<ActivationRequest, bool, Task<int>> forward = null)
        {
            _instance = instance; _factory = new HashClassFactory(this);
            _forward = forward ?? ((request, start) => ActivationChannel.SendAsync(request, start, _cancel.Token));
            IntPtr pointer = Marshal.GetIUnknownForObject(_factory);
            Guid clsid = classId ?? new Guid(HashShellRegistration.ClassId);
            // Local-server multiple-use activation preserves complete multi-selections
            try { Marshal.ThrowExceptionForHR(HashComNative.CoRegisterClassObject(ref clsid, pointer, 4, 1, out _cookie)); }
            finally { Marshal.Release(pointer); }
            if (!classId.HasValue)
            {
                try
                {
                    for (int index = 0; index < PermissionShellRegistration.ClassIds.Length; index++)
                    {
                        var factory = new HashClassFactory(this, PermissionShellRegistration.Operations[index]);
                        _permissionFactories.Add(factory);
                        IntPtr entry = Marshal.GetIUnknownForObject(factory);
                        Guid id = new Guid(PermissionShellRegistration.ClassIds[index]);
                        try
                        {
                            Marshal.ThrowExceptionForHR(HashComNative.CoRegisterClassObject(ref id, entry, 4, 1, out uint cookie));
                            _permissionCookies.Add(cookie);
                        }
                        finally { Marshal.Release(entry); }
                    }
                }
                catch { RevokePermissions(); if (_cookie != 0) { HashComNative.CoRevokeClassObject(_cookie); _cookie = 0; } throw; }
            }
            _lifetime.Tick += LifetimeTick; _lifetime.Start();
        }
        /// <summary>Tracks an explicit COM lock</summary><param name="locked">Acquire or release</param>
        internal void ServerLock(bool locked) { _locks = Math.Max(0, _locks + (locked ? 1 : -1)); _lastActivity = DateTime.UtcNow; }
        /// <summary>Retains a selection and returns promptly to Explorer</summary><param name="selection">Shell selection</param>
        /// <param name="operation">Optional fixed permission operation</param>
        internal void Accept(IShellItemArray selection, string operation = null)
        {
            if (_stopping) throw new COMException("Hash host is closing", unchecked((int)0x80010108));
            IntPtr pointer = Marshal.GetIUnknownForObject(selection); _requests++;
            try
            {
                _dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await ReceiveAsync((IShellItemArray)Marshal.GetObjectForIUnknown(pointer), operation); }
                    finally { Marshal.Release(pointer); _requests--; _lastActivity = DateTime.UtcNow; }
                }));
            }
            catch { Marshal.Release(pointer); _requests--; throw; }
        }
        /// <summary>Enumerates COM objects only on their STA and forwards plain path batches</summary>
        /// <param name="selection">Owned selection reference</param><returns>Completion after transfer</returns>
        /// <param name="operation">Optional permission action</param>
        private async Task ReceiveAsync(IShellItemArray selection, string operation)
        {
            try
            {
                selection.GetCount(out uint count);
                if (count == 0) return;
                bool permissions = operation != null;
                if (permissions && count > Features.Permissions.PermissionInbox.MaximumItems) throw new InvalidOperationException();
                string batchId = Guid.NewGuid().ToString("N");
                int sequence = 0;
                int epoch = await _forward(permissions
                    ? new ActivationRequest { Action = "begin-permissions", BatchId = batchId, Operation = operation, Total = (int)count }
                    : new ActivationRequest { Action = "begin-hash" }, true);
                if (epoch < 0) return;
                Func<List<string>, Task<int>> forward = paths => permissions
                    ? _forward(new ActivationRequest { Action = "append-permissions", BatchId = batchId, Sequence = sequence++, Paths = paths.ToArray() }, false)
                    : ForwardAsync(paths, epoch);
                var batch = new List<string>(); int characters = 0;
                for (uint index = 0; index < count && !_stopping; index++)
                {
                    IShellItem item = null; IntPtr name = IntPtr.Zero; string path = null;
                    try
                    {
                        selection.GetItemAt(index, out item); item.GetDisplayName(FileSystemPath, out name);
                        path = Marshal.PtrToStringUni(name);
                    }
                    catch (COMException ex) { if (permissions) throw; AppLogger.Write("Hash selection unavailable", ex); }
                    finally { if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name); if (item != null) Marshal.ReleaseComObject(item); }
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        if (batch.Count > 0 && characters + path.Length > BatchCharacters)
                        {
                            if (await forward(batch) < 0) throw new InvalidOperationException();
                            batch.Clear(); characters = 0;
                        }
                        batch.Add(path); characters += path.Length;
                    }
                    if (batch.Count >= SelectionBatchSize)
                    {
                        if (await forward(batch) < 0) throw new InvalidOperationException();
                        batch.Clear(); characters = 0;
                    }
                    // Yield periodically even when a batch contains unavailable shell items
                    if ((index + 1) % SelectionBatchSize == 0) await Task.Delay(1);
                }
                if (!_stopping && batch.Count > 0 && await forward(batch) < 0) throw new InvalidOperationException();
                if (!_stopping && permissions && await _forward(new ActivationRequest {
                    Action = "end-permissions", BatchId = batchId, Sequence = sequence }, false) < 0)
                    throw new InvalidOperationException();
            }
            catch (Exception ex)
            {
                AppLogger.Write("Hash selection forwarding failed", ex);
                if (!_stopping) await UI.Services.DialogService.ShowAsync(TranslationService.T(operation == null ? "hash.selectionError" : "permissions.selectionError"));
            }
            finally { Marshal.ReleaseComObject(selection); }
        }
        /// <summary>Sends a batch without restarting an application that is shutting down</summary>
        /// <param name="paths">Selection batch</param><param name="epoch">Current UI round</param><returns>Acceptance status</returns>
        private Task<int> ForwardAsync(List<string> paths, int epoch) => _forward(
            new ActivationRequest { Action = "append-hash", Paths = paths.ToArray(), Epoch = epoch }, false);
        /// <summary>Retires idle servers and honors installer shutdown</summary><param name="sender">Timer</param><param name="args">Tick</param>
        private void LifetimeTick(object sender, EventArgs args)
        {
            if (_instance.StopRequested() || (_requests == 0 && _locks == 0 && (DateTime.UtcNow - _lastActivity).TotalSeconds >= ActivationGraceSeconds))
            {
                _stopping = true;
                _cancel.Cancel();
                if (System.Windows.Application.Current != null)
                    foreach (var window in System.Windows.Application.Current.Windows.Cast<System.Windows.Window>().ToArray()) window.Close();
                if (_cookie != 0) { HashComNative.CoRevokeClassObject(_cookie); _cookie = 0; }
                RevokePermissions();
                if (_requests == 0) _dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }
        /// <summary>Releases COM publication and the dispatcher timer</summary>
        public void Dispose()
        {
            _lifetime.Stop(); _cancel.Cancel(); _cancel.Dispose();
            if (_cookie != 0) { HashComNative.CoRevokeClassObject(_cookie); _cookie = 0; }
            RevokePermissions();
        }
        /// <summary>Revokes all permission factories before the forwarding apartment exits</summary>
        private void RevokePermissions()
        {
            foreach (uint cookie in _permissionCookies) HashComNative.CoRevokeClassObject(cookie);
            _permissionCookies.Clear(); _permissionFactories.Clear();
        }
    }
}
