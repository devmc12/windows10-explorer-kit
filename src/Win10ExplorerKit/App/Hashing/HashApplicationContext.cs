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
            _lifetime.Tick += LifetimeTick; _lifetime.Start();
        }
        /// <summary>Tracks an explicit COM lock</summary><param name="locked">Acquire or release</param>
        internal void ServerLock(bool locked) { _locks = Math.Max(0, _locks + (locked ? 1 : -1)); _lastActivity = DateTime.UtcNow; }
        /// <summary>Retains a selection and returns promptly to Explorer</summary><param name="selection">Shell selection</param>
        internal void Accept(IShellItemArray selection)
        {
            if (_stopping) throw new COMException("Hash host is closing", unchecked((int)0x80010108));
            IntPtr pointer = Marshal.GetIUnknownForObject(selection); _requests++;
            try
            {
                _dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await ReceiveAsync((IShellItemArray)Marshal.GetObjectForIUnknown(pointer)); }
                    finally { Marshal.Release(pointer); _requests--; _lastActivity = DateTime.UtcNow; }
                }));
            }
            catch { Marshal.Release(pointer); _requests--; throw; }
        }
        /// <summary>Enumerates COM objects only on their STA and forwards plain path batches</summary>
        /// <param name="selection">Owned selection reference</param><returns>Completion after transfer</returns>
        private async Task ReceiveAsync(IShellItemArray selection)
        {
            try
            {
                int epoch = await _forward(new ActivationRequest { Action = "begin-hash" }, true);
                if (epoch < 0) return;
                selection.GetCount(out uint count);
                var batch = new List<string>(); int characters = 0;
                for (uint index = 0; index < count && !_stopping; index++)
                {
                    IShellItem item = null; IntPtr name = IntPtr.Zero; string path = null;
                    try
                    {
                        selection.GetItemAt(index, out item); item.GetDisplayName(FileSystemPath, out name);
                        path = Marshal.PtrToStringUni(name);
                    }
                    catch (COMException ex) { AppLogger.Write("Hash selection unavailable", ex); }
                    finally { if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name); if (item != null) Marshal.ReleaseComObject(item); }
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        if (batch.Count > 0 && characters + path.Length > BatchCharacters)
                        {
                            if (await ForwardAsync(batch, epoch) < 0) return;
                            batch.Clear(); characters = 0;
                        }
                        batch.Add(path); characters += path.Length;
                    }
                    if (batch.Count >= SelectionBatchSize)
                    {
                        if (await ForwardAsync(batch, epoch) < 0) return;
                        batch.Clear(); characters = 0;
                    }
                    // Yield periodically even when a batch contains unavailable shell items
                    if ((index + 1) % SelectionBatchSize == 0) await Task.Delay(1);
                }
                if (!_stopping && batch.Count > 0) await ForwardAsync(batch, epoch);
            }
            catch (Exception ex)
            {
                AppLogger.Write("Hash selection forwarding failed", ex);
                if (!_stopping) await UI.Services.DialogService.ShowAsync(TranslationService.T("hash.selectionError"));
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
                if (_requests == 0) _dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }
        /// <summary>Releases COM publication and the dispatcher timer</summary>
        public void Dispose()
        {
            _lifetime.Stop(); _cancel.Cancel(); _cancel.Dispose();
            if (_cookie != 0) { HashComNative.CoRevokeClassObject(_cookie); _cookie = 0; }
        }
    }
}
