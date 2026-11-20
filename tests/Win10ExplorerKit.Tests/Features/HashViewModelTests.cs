using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Configuration;
using Win10ExplorerKit.UI.ViewModels;
namespace Win10ExplorerKit.Tests.Features
{
    [TestClass]
    public sealed class HashViewModelTests
    {
        /// <summary>Exercises shared result lifetime, duplicate drops, copying and algorithm changes</summary>
        /// <returns>Completion of the isolated STA dispatcher</returns>
        [TestMethod]
        public Task QueuesSelectionsAndCopiesCompletedResults() => Run(async (model, directory, copied) =>
        {
            string file = Path.Combine(directory, "first.txt");
            string folder = Path.Combine(directory, "folder");
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(file, "abc");
            File.WriteAllText(Path.Combine(folder, "child.txt"), "def");
            File.WriteAllText(Path.Combine(folder, "nested", "excluded.txt"), "excluded");
            model.AddPaths(new[] { file, folder, file });
            await WaitFor(() => !model.IsBusy && model.Rows.Count == 2);
            Assert.IsTrue(model.Rows.All(row => row.CanCopy));
            model.Copy(model.Rows[0]);
            Assert.AreEqual(model.Rows[0].Value, copied());
            model.Copy();
            Assert.AreEqual(2, copied().Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length);
            await model.ChangeAsync(false, "MD5");
            await WaitFor(() => !model.IsBusy && model.Rows.Count == 2);
            Assert.IsTrue(model.Rows.All(row => row.Value.Length == 32));
            int epoch = model.InputEpoch;
            await model.ChangeAsync(true);
            Assert.IsTrue(model.InputEpoch > epoch);
            Assert.AreEqual(0, model.Rows.Count);
            model.AddPaths(new[] { file });
            await WaitFor(() => !model.IsBusy && model.Rows.Count == 1);
        });
        /// <summary>Preserves independent preferences and unknown INI entries across theme changes</summary>
        [TestMethod]
        public void ThemeDefaultsAndPersistencePreserveOtherSettings()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerKitTheme-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "Config.ini");
                File.WriteAllText(path, "[General]\nLanguage=en\n[Copy]\nQuotePaths=true\n[Custom]\nValue=retained\n");
                var settings = new SettingsService(path, "en");
                Assert.AreEqual("System", settings.Theme);
                settings.SetTheme("Dark");
                Assert.AreEqual("Dark", new SettingsService(path, "en").Theme);
                StringAssert.Contains(File.ReadAllText(path), "Value=retained");
                Assert.ThrowsException<ArgumentException>(() => settings.SetTheme("invalid"));
            }
            finally { Directory.Delete(directory, true); }
        }
        /// <summary>Runs worker interactions without touching the real clipboard, settings or shell</summary>
        /// <param name="action">Scenario</param><returns>STA completion</returns>
        private static Task Run(Func<HashViewModel, string, Func<string>, Task> action)
        {
            var completion = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "ExplorerKitWpf-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                dispatcher.BeginInvoke(new Action(async () =>
                {
                    HashViewModel model = null;
                    Exception failure = null;
                    try
                    {
                        string copied = null;
                        model = new HashViewModel(new SettingsService(Path.Combine(directory, "Config.ini"), "en"), text => { }, text => { copied = text; return true; });
                        await action(model, directory, () => copied);
                    }
                    catch (Exception ex) { failure = ex; }
                    finally
                    {
                        try { if (model != null) await model.CloseAsync(); Directory.Delete(directory, true); }
                        catch (Exception ex) { if (failure == null) failure = ex; }
                        if (failure == null) completion.TrySetResult(true); else completion.TrySetException(failure);
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                }));
                Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
            return completion.Task;
        }
        /// <summary>Waits for worker publication while allowing dispatcher updates</summary>
        /// <param name="condition">Completion predicate</param><returns>A bounded wait</returns>
        private static async Task WaitFor(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition()) { if (DateTime.UtcNow > deadline) Assert.Fail("Hash queue did not complete"); await Task.Delay(30); }
        }
    }
}
