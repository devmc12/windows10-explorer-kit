using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Features.CopyPaths;

namespace Win10ExplorerKit.Tests.Features
{
    [TestClass]
    public class CopyPathsTests
    {
        /// <summary>
        /// Verifies path formatting without touching the system clipboard
        /// </summary>
        [TestMethod]
        public void FormatsUnicodeSpacesAndDuplicateSelections()
        {
            var paths = new[] { @"C:\Example\中文 file.txt", @"C:\Example\Folder", @"C:\EXAMPLE\FOLDER" };
            Assert.AreEqual("\"C:\\Example\\中文 file.txt\"\r\n\"C:\\Example\\Folder\"", PathFormatter.Format(paths, false));
            Assert.AreEqual("C:\\Example\\中文 file.txt\r\nC:\\Example\\Folder", PathFormatter.Format(paths, true));
        }

        /// <summary>
        /// Guards AnyCPU metadata against architecture regressions
        /// </summary>
        [TestMethod]
        public void ApplicationIsAnyCpu()
        {
            var assembly = typeof(PathFormatter).Assembly;
            PortableExecutableKinds kind;
            ImageFileMachine machine;
            assembly.ManifestModule.GetPEKind(out kind, out machine);
            Assert.AreEqual(ImageFileMachine.I386, machine);
            Assert.AreEqual(0, (int)(kind & PortableExecutableKinds.Required32Bit));
            Assert.AreNotEqual(0, (int)(kind & PortableExecutableKinds.ILOnly));
        }
    }
}
