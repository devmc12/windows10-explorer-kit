using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Diagnostics;

namespace Win10ExplorerKit.Tests.Diagnostics
{
    [TestClass]
    public class AppLoggerTests
    {
        /// <summary>Checks append and rotation at local midnight without restarting</summary>
        [TestMethod]
        public void RotatesAtLocalMidnight()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Win10ExplorerKit.Tests", Guid.NewGuid().ToString("N"));
            var now = new DateTime(2026, 1, 1, 23, 59, 59);
            AppLogger.Write(directory, now, "first", null);
            AppLogger.Write(directory, now, "second", null);
            AppLogger.Write(directory, now.AddSeconds(1), "third", null);
            Assert.AreEqual(2, File.ReadAllLines(Path.Combine(directory, "2026-01-01.log")).Length);
            StringAssert.Contains(File.ReadAllText(Path.Combine(directory, "2026-01-02.log")), "third");
        }

        /// <summary>
        /// Ensures exception payloads never enter the diagnostic representation
        /// </summary>
        [TestMethod]
        public void ExceptionDetailsExcludeMessagesPathsAndCustomData()
        {
            var inner = new IOException("private-inner-message");
            var exception = new FileNotFoundException("private-error-message", @"C:\Example\private-document.txt", inner);
            exception.Data["private-key"] = "private-value";

            string detail = AppLogger.DescribeException(exception);

            Assert.AreEqual("System.IO.FileNotFoundException (HRESULT 0x" + exception.HResult.ToString("X8") + ")", detail);
            Assert.IsFalse(detail.Contains("private-"));
            Assert.IsFalse(detail.Contains(@"C:\Example"));
        }
    }
}
