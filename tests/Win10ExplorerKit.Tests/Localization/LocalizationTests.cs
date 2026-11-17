using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win10ExplorerKit.Localization;

namespace Win10ExplorerKit.Tests.Localization
{
    [TestClass]
    public class LocalizationTests
    {
        /// <summary>
        /// Checks native labels, fallback, missing keys and malformed format handling
        /// </summary>
        [TestMethod]
        public void ExternalLocaleFallsBackToEnglish()
        {
            TranslationService.UseLanguage("fr");
            Assert.AreEqual("Paramètres FR", TranslationService.T("tray.settings"));
            Assert.AreEqual("E&xit", TranslationService.T("tray.exit"));
            Assert.AreEqual("Setup failed: detail", TranslationService.T("error.setup", "detail"));
            Assert.AreEqual("[missing]", TranslationService.T("missing"));
            Assert.IsTrue(new List<string>(TranslationService.AvailableLanguages).Contains("fr"));
        }

        /// <summary>
        /// Ensures every English resource key has an explicit Chinese translation
        /// </summary>
        [TestMethod]
        public void ShippedLocalesHaveMatchingKeys()
        {
            var assembly = typeof(TranslationService).Assembly;
            using (var en = new System.IO.StreamReader(assembly.GetManifestResourceStream("locales.en.json")))
            using (var zh = new System.IO.StreamReader(assembly.GetManifestResourceStream("locales.zh-CN.json")))
                CollectionAssert.AreEquivalent(new List<string>(TranslationService.Parse(en.ReadToEnd()).Keys), new List<string>(TranslationService.Parse(zh.ReadToEnd()).Keys));
        }
    }
}
