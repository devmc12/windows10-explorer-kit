using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Configuration;
using Win10ExplorerKit.Features.FileHash;

namespace Win10ExplorerKit.Tests.Features
{
    [TestClass]
    public class FileHashTests
    {
        private string _directory;

        /// <summary>Creates a test-owned filesystem fixture</summary>
        [TestInitialize]
        public void Initialize()
        {
            _directory = Path.Combine(Path.GetTempPath(), "Win10ExplorerKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        /// <summary>Removes the fixture after all workers released their streams</summary>
        [TestCleanup]
        public void Cleanup() => Directory.Delete(_directory, true);

        /// <summary>Compares algorithm results with published abc test vectors</summary>
        /// <param name="algorithm">Algorithm under test</param><param name="expected">Known digest</param>
        [DataTestMethod]
        [DataRow("MD5", "900150983cd24fb0d6963f7d28e17f72")]
        [DataRow("SHA-1", "a9993e364706816aba3e25717850c26c9cd0d89d")]
        [DataRow("SHA-256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
        [DataRow("SHA-512", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
        public void MatchesKnownVectors(string algorithm, string expected)
        {
            string path = Path.Combine(_directory, "中文 file.txt");
            File.WriteAllText(path, "abc", new System.Text.UTF8Encoding(false));
            var row = new HashItem(path);
            FileHasher.Compute(row, algorithm, new byte[FileHasher.BufferSize], CancellationToken.None);
            Assert.AreEqual(expected, row.Value);
            Assert.AreEqual(HashState.Complete, row.State);
        }

        /// <summary>Verifies first-level discovery, overlap deduplication, hidden files and empty directories</summary>
        [TestMethod]
        public async Task ExpandsOnlyTheFirstLevel()
        {
            string nested = Path.Combine(_directory, "child");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "excluded.txt"), "nested");
            string visible = Path.Combine(_directory, "visible.txt");
            string hidden = Path.Combine(_directory, "hidden.txt");
            File.WriteAllText(visible, "abc");
            File.WriteAllText(hidden, "");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            Directory.CreateDirectory(Path.Combine(_directory, "empty"));
            var queue = new HashQueue("SHA-256", 2);
            try
            {
                queue.Add(new[] { _directory, visible, visible.ToUpperInvariant(), Path.Combine(_directory, "empty") });
                await WaitIdle(queue);
                HashItem[] rows = queue.NewItems.ToArray();
                Assert.AreEqual(3, rows.Length);
                Assert.AreEqual(2, rows.Count(r => r.State == HashState.Complete));
                Assert.AreEqual(1, rows.Count(r => r.State == HashState.Empty));
                Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", rows.Single(r => r.Path == hidden).Value);
            }
            finally { await queue.StopAsync(); queue.Dispose(); }
        }

        /// <summary>Processes a large selection with every supported worker count</summary>
        /// <param name="parallelism">Maximum concurrent file readers</param>
        [DataTestMethod]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(4)]
        public async Task ProcessesLargeBatches(int parallelism)
        {
            var files = new List<string>();
            for (int i = 0; i < 1000; i++)
            {
                string path = Path.Combine(_directory, i + ".txt");
                File.WriteAllText(path, "abc");
                files.Add(path);
            }
            var queue = new HashQueue("MD5", parallelism);
            try
            {
                queue.Add(files.ToArray());
                await WaitIdle(queue);
                Assert.AreEqual(1000, queue.NewItems.Count);
                Assert.IsTrue(queue.NewItems.All(r => r.State == HashState.Complete &&
                    r.Value == "900150983cd24fb0d6963f7d28e17f72"));
                queue.Add(files.ToArray());
                await WaitIdle(queue);
                Assert.AreEqual(1000, queue.NewItems.Count);
            }
            finally { await queue.StopAsync(); queue.Dispose(); }
        }

        /// <summary>A locked or missing file does not stop the remaining files</summary>
        [TestMethod]
        public async Task ReportsIndividualErrors()
        {
            string locked = Path.Combine(_directory, "locked.txt");
            string available = Path.Combine(_directory, "available.txt");
            File.WriteAllText(available, "abc");
            using (File.Open(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                var queue = new HashQueue("MD5", 2);
                try
                {
                    queue.Add(new[] { locked, Path.Combine(_directory, "missing.txt"), available });
                    await WaitIdle(queue);
                    Assert.AreEqual(2, queue.NewItems.Count(r => r.State == HashState.Failed));
                    Assert.AreEqual("hash.locked", queue.NewItems.Single(r => r.Path == locked).Error);
                    Assert.AreEqual(HashState.Complete, queue.NewItems.Single(r => r.Path == available).State);
                }
                finally { await queue.StopAsync(); queue.Dispose(); }
            }
        }

        /// <summary>Cancellation releases file handles and bounded producer waits</summary>
        [TestMethod]
        public async Task CancellationReleasesFiles()
        {
            string path = Path.Combine(_directory, "large.bin");
            using (var stream = File.Create(path)) stream.SetLength(64 * 1024 * 1024);
            var queue = new HashQueue("SHA-512", 2);
            queue.Add(new[] { path });
            await queue.StopAsync();
            Assert.IsFalse(queue.IsBusy);
            Assert.IsFalse(queue.NewItems.Any(r => r.State == HashState.Reading || r.State == HashState.Waiting));
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            queue.Dispose();
        }

        /// <summary>Hash choices preserve preferences owned by other feature windows</summary>
        [TestMethod]
        public void HashPreferencesPreserveOtherSettings()
        {
            string path = Path.Combine(_directory, "Config.ini");
            var settings = new SettingsService(path, "zh-CN");
            settings.SetHashOptions("SHA-512", 4);
            settings.SetQuotePaths(false);
            settings = new SettingsService(path, "en");
            Assert.AreEqual("SHA-512", settings.HashAlgorithm);
            Assert.AreEqual(4, settings.HashParallelism);
            Assert.AreEqual("zh-CN", settings.Language);
            Assert.IsFalse(settings.QuotePaths);
        }

        /// <summary>Waits for actual work completion with a bounded test deadline</summary>
        /// <param name="queue">Queue receiving the fixture files</param>
        /// <returns>Completion when the queue becomes idle</returns>
        private static async Task WaitIdle(HashQueue queue)
        {
            var elapsed = Stopwatch.StartNew();
            while (queue.IsBusy && elapsed.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(10);
            Assert.IsFalse(queue.IsBusy, "Hash queue did not become idle");
        }
    }
}
