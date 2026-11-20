using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Win10ExplorerKit.UI.Forms.Hash;

namespace Win10ExplorerKit.App.Hashing
{
    internal sealed class HashApplicationContext : ApplicationContext
    {
        // Shell filesystem display-name identifier from SIGDN
        private const uint FileSystemPath = 0x80058000;
        // A selection is extracted in small UI batches to keep COM and WinForms responsive
        private const int SelectionBatchSize = 128;
        // Idle activation grace in milliseconds for a caller that never executes its command
        private const int ActivationGraceMilliseconds = 30000;
        private readonly Control _dispatcher = new Control();
        private readonly Timer _lifetime = new Timer { Interval = 200 };
        private readonly SingleInstance _instance;
        private readonly SettingsService _settings;
        private readonly HashClassFactory _factory;
        private readonly DateTime _started = DateTime.UtcNow;
        private HashForm _form;
        private uint _cookie;
        private int _requests;
        private int _locks;
        private bool _stopping;

        /// <summary>Publishes an out-of-process command factory on the current STA</summary>
        /// <param name="settings">Preferences loaded before accepting Shell commands</param>
        /// <param name="instance">Hash process ownership and shutdown event</param>
        /// <param name="classId">Optional isolated class identity for integration tests</param>
        internal HashApplicationContext(SettingsService settings, SingleInstance instance, Guid? classId = null)
        {
            _settings = settings;
            _instance = instance;
            _dispatcher.CreateControl();
            _factory = new HashClassFactory(this);
            IntPtr pointer = Marshal.GetIUnknownForObject(_factory);
            Guid clsid = classId ?? new Guid(HashShellRegistration.ClassId);
            // CLSCTX_LOCAL_SERVER and REGCLS_MULTIPLEUSE share one window across activations
            try { Marshal.ThrowExceptionForHR(HashComNative.CoRegisterClassObject(ref clsid, pointer, 4, 1, out _cookie)); }
            finally { Marshal.Release(pointer); }
            AppLogger.Write("Hash command factory registered", null);
            _lifetime.Tick += LifetimeTick;
            _lifetime.Start();
        }

        /// <summary>Tracks an explicit COM server lock</summary><param name="locked">Acquire or release</param>
        internal void ServerLock(bool locked) { _locks = Math.Max(0, _locks + (locked ? 1 : -1)); }

        /// <summary>Takes an independent reference and returns promptly to Explorer</summary>
        /// <param name="selection">Complete selection from IObjectWithSelection</param>
        internal void Accept(IShellItemArray selection)
        {
            if (_stopping) throw new COMException("Hash host is closing", unchecked((int)0x80010108));
            IntPtr pointer = Marshal.GetIUnknownForObject(selection);
            _requests++;
            try
            {
                _dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await ReceiveAsync((IShellItemArray)Marshal.GetObjectForIUnknown(pointer)); }
                    finally { Marshal.Release(pointer); _requests--; }
                }));
            }
            catch { Marshal.Release(pointer); _requests--; throw; }
        }

        /// <summary>Converts a selection on its STA and hands only paths to background workers</summary>
        /// <param name="selection">Owned selection reference</param>
        /// <returns>Completion after the selection is consumed or canceled</returns>
        private async Task ReceiveAsync(IShellItemArray selection)
        {
            HashForm form = null;
            try
            {
                if (_stopping) return;
                if (_form == null || _form.IsDisposed)
                {
                    _form = new HashForm(_settings);
                    _form.FormClosed += (sender, args) => { if (_requests == 0) BeginStop(); };
                }
                form = _form;
                form.Show();
                if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
                form.Activate();
                form.Receiving(true);
                int epoch = form.InputEpoch;
                selection.GetCount(out uint count);
                var batch = new List<string>(SelectionBatchSize);
                for (uint index = 0; index < count && !_stopping && !form.IsDisposed && form.InputEpoch == epoch; index++)
                {
                    IShellItem item = null;
                    IntPtr name = IntPtr.Zero;
                    try
                    {
                        selection.GetItemAt(index, out item);
                        item.GetDisplayName(FileSystemPath, out name);
                        string path = Marshal.PtrToStringUni(name);
                        if (!string.IsNullOrWhiteSpace(path)) batch.Add(path);
                    }
                    catch (COMException ex) { AppLogger.Write("Hash selection item unavailable", ex); }
                    finally
                    {
                        if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name);
                        if (item != null) Marshal.ReleaseComObject(item);
                    }
                    if ((index + 1) % SelectionBatchSize == 0)
                    {
                        form.AddPaths(batch.ToArray());
                        batch.Clear();
                        await Task.Delay(1);
                    }
                }
                if (!form.IsDisposed && form.InputEpoch == epoch) form.AddPaths(batch.ToArray());
            }
            catch (Exception ex)
            {
                AppLogger.Write("Hash selection failed", ex);
                if (form != null && !form.IsDisposed)
                    MessageBox.Show(form, TranslationService.T("hash.selectionError"), ProductInfo.Name);
            }
            finally
            {
                if (form != null && !form.IsDisposed) form.Receiving(false);
                Marshal.ReleaseComObject(selection);
            }
        }

        /// <summary>Honors installer shutdown and retires a closed or unused server</summary>
        /// <param name="sender">Lifetime timer</param><param name="args">Tick arguments</param>
        private void LifetimeTick(object sender, EventArgs args)
        {
            if (_instance.StopRequested()) BeginStop();
            if (_form != null && _form.IsDisposed && _requests == 0) BeginStop();
            if (_form == null && _requests == 0 && _locks == 0 &&
                (DateTime.UtcNow - _started).TotalMilliseconds >= ActivationGraceMilliseconds) BeginStop();
            if (_stopping && _requests == 0 && (_form == null || _form.IsDisposed)) ExitThread();
        }

        /// <summary>Revokes activation before waiting for the current readers to stop</summary>
        private void BeginStop()
        {
            if (_stopping) return;
            _stopping = true;
            if (_cookie != 0) { HashComNative.CoRevokeClassObject(_cookie); _cookie = 0; }
            _form?.Close();
        }

        /// <summary>Releases COM publication and the invisible UI dispatcher</summary>
        /// <param name="disposing">Whether managed resources are being released</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lifetime.Dispose();
                if (_cookie != 0) HashComNative.CoRevokeClassObject(_cookie);
                _dispatcher.Dispose();
                _form?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
