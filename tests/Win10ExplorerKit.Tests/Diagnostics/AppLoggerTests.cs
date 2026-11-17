using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Diagnostics;

namespace Win10ExplorerKit.Tests.Diagnostics
{
    [TestClass]
    public class AppLoggerTests
    {
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
