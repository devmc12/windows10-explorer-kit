using System;
using System.Windows.Forms;
using Win10ExplorerKit.Windows.Startup;

namespace Win10ExplorerKit.UI.Forms.Settings
{
    internal sealed partial class SettingsForm : Form
    {
        private readonly SettingsService _settings;
        private readonly StartupRegistration _startup = new StartupRegistration(Application.ExecutablePath);

        /// <summary>
        /// Creates a settings editor without taking ownership of application lifetime
        /// </summary>
        /// <param name="settings">Shared validated configuration service</param>
        public SettingsForm(SettingsService settings)
        {
            _settings = settings;
            InitializeComponent();
            foreach (string code in TranslationService.AvailableLanguages)
                language.Items.Add(new LanguageChoice { Code = code, Name = TranslationService.LanguageName(code) });
            save.Click += Save;
            cancel.Click += (sender, e) => Close();
            _settings.Changed += Reload;
            Reload(this, EventArgs.Empty);
        }

        /// <summary>
        /// Refreshes both translated controls and committed settings when another UI changes them
        /// </summary>
        /// <param name="sender">Event source</param>
        /// <param name="args">Event arguments</param>
        private void Reload(object sender, EventArgs args)
        {
            Text = ProductInfo.Name + " — " + TranslationService.T("settings.title");
            languageLabel.Text = TranslationService.T("settings.language");
            quotePaths.Text = TranslationService.T("settings.quotePaths");
            startAtLogin.Text = TranslationService.T("settings.startAtLogin");
            startAtLogin.Checked = _startup.IsEnabled();
            save.Text = TranslationService.T("settings.save");
            cancel.Text = TranslationService.T("settings.cancel");
            quotePaths.Checked = _settings.QuotePaths;
            foreach (LanguageChoice choice in language.Items)
                if (choice.Code == _settings.Language) language.SelectedItem = choice;
        }

        /// <summary>
        /// Commits one validated snapshot before notifying the tray and closing the dialog
        /// </summary>
        /// <param name="sender">Save button</param>
        /// <param name="args">Click event arguments</param>
        private void Save(object sender, EventArgs args)
        {
            var choice = language.SelectedItem as LanguageChoice;
            if (choice == null) return;
            // Capture the checkbox before saving preferences raises a UI reload event
            bool enableStartup = startAtLogin.Checked;
            try
            {
                _settings.Save(new AppOptions { Language = choice.Code, QuotePaths = quotePaths.Checked });
                _startup.SetEnabled(enableStartup);
                Close();
            }
            catch (Exception ex) { AppLogger.Write("Settings save failed", ex); MessageBox.Show(this, TranslationService.T("error.settings"), ProductInfo.Name); }
        }

        /// <summary>
        /// Releases shared-service subscriptions when the settings window is closed
        /// </summary>
        /// <param name="disposing">Whether managed controls should be released</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) _settings.Changed -= Reload;
            base.Dispose(disposing);
        }

        private sealed class LanguageChoice
        {
            public string Code { get; set; }
            public string Name { get; set; }
            /// <summary>
            /// Supplies the locale's native display name to the combo box
            /// </summary>
            /// <returns>Native language label</returns>
            public override string ToString() { return Name; }
        }
    }
}
