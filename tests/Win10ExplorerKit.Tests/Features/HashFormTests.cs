using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Configuration;
using Win10ExplorerKit.Features.FileHash;
using Win10ExplorerKit.UI.Forms.Hash;

namespace Win10ExplorerKit.Tests.Features
{
    [TestClass]
    public sealed class HashFormTests
    {
        /// <summary>Exercises filesystem drops over child controls and copying through the displayed hash cell</summary>
        /// <returns>Completion of the isolated UI thread</returns>
        [TestMethod]
        public Task DropsFilesAndFoldersAndCopiesHashCells()
        {
            string copied = null;
            return RunWindow(async (form, directory) =>
            {
                var grid = Control<DataGridView>(form, "_grid");
                Assert.AreEqual(3, grid.Columns.Count);
                Assert.IsTrue(grid.AllowDrop);
                string file = Path.Combine(directory, "first.txt");
                string folder = Path.Combine(directory, "folder");
                Directory.CreateDirectory(Path.Combine(folder, "nested"));
                File.WriteAllText(file, "abc");
                File.WriteAllText(Path.Combine(folder, "child.txt"), "def");
                File.WriteAllText(Path.Combine(folder, "nested", "excluded.txt"), "excluded");
                var data = new DataObject(DataFormats.FileDrop, new[] { file, folder, file });
                var drop = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.None);
                RaiseControlEvent(grid, "OnDragEnter", drop);
                Assert.AreEqual(DragDropEffects.Copy, drop.Effect);
                RaiseControlEvent(grid, "OnDragDrop", drop);
                await WaitFor(() => !form.IsBusy && form.Results.Count == 2);
                Assert.IsTrue(form.Results.All(row => row.State == HashState.Complete));
                Assert.AreEqual(form.Results[0].Value, grid[2, 0].Value);
                var click = typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic);
                var cell = grid.GetCellDisplayRectangle(2, 0, false);
                click.Invoke(grid, new object[] { new MouseEventArgs(MouseButtons.Left, 1, cell.Left + 1, cell.Top + 1, 0) });
                Assert.IsNull(copied);
                var textBounds = grid[2, 0].GetContentBounds(0);
                click.Invoke(grid, new object[] { new MouseEventArgs(MouseButtons.Left, 1,
                    cell.Left + textBounds.Left + 1, cell.Top + textBounds.Top + textBounds.Height / 2, 0) });
                Assert.AreEqual(form.Results[0].Value, copied);
                var invalid = new DragEventArgs(new DataObject(DataFormats.Text, "not a file"), 0, 0, 0,
                    DragDropEffects.Copy, DragDropEffects.None);
                RaiseControlEvent(grid, "OnDragEnter", invalid);
                Assert.AreEqual(DragDropEffects.None, invalid.Effect);
                Control<ComboBox>(form, "_algorithm").SelectedItem = "MD5";
                await WaitFor(() => !form.IsBusy && form.Results.Count == 2);
                Assert.IsTrue(form.Results.All(row => row.Value.Length == 32));
                Control<Button>(form, "_clear").PerformClick();
                await WaitFor(() => !form.IsBusy && form.Results.Count == 0);
                RaiseControlEvent(form, "OnDragDrop", drop);
                await WaitFor(() => !form.IsBusy && form.Results.Count == 2);
            }, text => { copied = text; return true; });
        }

        /// <summary>Dispatches a standard drag event through the control's registered handlers</summary>
        /// <param name="control">Actual drop surface</param><param name="name">Protected event raiser</param>
        /// <param name="args">Explorer-style drag data</param>
        private static void RaiseControlEvent(Control control, string name, DragEventArgs args)
        {
            typeof(Control).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
        }

        /// <summary>Exercises round changes and append behavior on a real STA message loop</summary>
        /// <returns>Completion of the isolated UI thread</returns>
        [TestMethod]
        public Task ChangesAlgorithmAndAppendsWithoutMixingResults()
        {
            return RunWindow(async (form, directory) =>
            {
                string first = Path.Combine(directory, "first.txt");
                string second = Path.Combine(directory, "second.txt");
                File.WriteAllText(first, "abc");
                File.WriteAllText(second, "def");
                form.AddPaths(new[] { first });
                await WaitFor(() => !form.IsBusy && form.Results.Count == 1);
                Assert.AreEqual(64, form.Results[0].Value.Length);
                Control<ComboBox>(form, "_algorithm").SelectedItem = "MD5";
                await WaitFor(() => !form.IsBusy && form.Results.Count == 1);
                Assert.AreEqual("900150983cd24fb0d6963f7d28e17f72", form.Results[0].Value);
                form.AddPaths(new[] { first, second });
                await WaitFor(() => !form.IsBusy && form.Results.Count == 2);
                Assert.IsTrue(form.Results.All(row => row.Value.Length == 32));
                Control<Button>(form, "_clear").PerformClick();
                await WaitFor(() => !form.IsBusy && form.Results.Count == 0);
                form.AddPaths(new[] { first });
                await WaitFor(() => !form.IsBusy && form.Results.Count == 1);
                await form.CancelAsync();
            });
        }

        /// <summary>Checks that an empty or canceled window can close without reentrant Close calls</summary>
        /// <returns>Completion of the isolated UI thread</returns>
        [TestMethod]
        public Task ClosesAnEmptyWindow()
        {
            return RunWindow((form, directory) => Task.CompletedTask);
        }

        /// <summary>Waits for active readers to release files before closing the window</summary>
        /// <returns>Completion of the isolated UI thread</returns>
        [TestMethod]
        public Task ClosesDuringCalculation()
        {
            return RunWindow((form, directory) =>
            {
                string path = Path.Combine(directory, "large.bin");
                using (var stream = File.Create(path)) stream.SetLength(128L * 1024 * 1024);
                form.AddPaths(new[] { path });
                return Task.CompletedTask;
            });
        }

        /// <summary>Runs a form in an isolated directory and closes it before releasing the thread</summary>
        /// <param name="action">Assertions executed on the form's STA</param>
        /// <param name="copyText">Optional clipboard substitute</param>
        /// <returns>Completion task with assertion failures propagated</returns>
        private static Task RunWindow(Func<HashForm, string, Task> action, Func<string, bool> copyText = null)
        {
            var completion = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "ExplorerKitUiTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                Exception failure = null;
                try
                {
                    using (var form = new HashForm(new SettingsService(Path.Combine(directory, "Config.ini"), "en"), copyText))
                    using (var timeout = new System.Windows.Forms.Timer { Interval = 15000 })
                    {
                        timeout.Tick += (sender, args) =>
                        {
                            failure = new TimeoutException("Hash window did not finish or close");
                            form.Dispose();
                            Application.ExitThread();
                        };
                        form.Shown += async (sender, args) =>
                        {
                            try { await action(form, directory); }
                            catch (Exception ex) { failure = ex; }
                            finally { form.Close(); }
                        };
                        timeout.Start();
                        Application.Run(form);
                        Assert.IsTrue(form.IsDisposed);
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    try { Directory.Delete(directory, true); }
                    catch (Exception ex) { if (failure == null) failure = ex; }
                }
                if (failure == null) completion.SetResult(true);
                else completion.SetException(failure);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return completion.Task;
        }

        /// <summary>Waits while allowing the form message loop to handle result batches</summary>
        /// <param name="condition">Completion predicate</param>
        /// <returns>A bounded asynchronous wait</returns>
        private static async Task WaitFor(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("Hash window did not reach the expected state");
                await Task.Delay(30);
            }
        }

        /// <summary>Finds a private control for interaction tests without adding production accessors</summary>
        /// <typeparam name="T">Expected control type</typeparam>
        /// <param name="form">Window under test</param>
        /// <param name="name">Control field name</param>
        /// <returns>The requested control</returns>
        private static T Control<T>(HashForm form, string name) where T : Control
        {
            return (T)typeof(HashForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }
    }
}
