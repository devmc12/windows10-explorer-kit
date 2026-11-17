using System.Drawing;
using System.Windows.Forms;

namespace Win10ExplorerKit.UI.Forms.Settings
{
    internal sealed partial class SettingsForm
    {
        private Label languageLabel;
        private ComboBox language;
        private CheckBox quotePaths;
        private CheckBox startAtLogin;
        private Button save;
        private Button cancel;

        /// <summary>
        /// Defines layout separately from settings behavior and translated text
        /// </summary>
        private void InitializeComponent()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10F);
            ClientSize = new Size(440, 186);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            languageLabel = new Label { Location = new Point(20, 19), Size = new Size(130, 26), TabIndex = 0 };
            language = new ComboBox { Location = new Point(160, 16), Size = new Size(260, 28), Font = new Font("Segoe UI", 9F), DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 1 };
            quotePaths = new CheckBox { Location = new Point(20, 56), Size = new Size(400, 26), TabIndex = 2 };
            startAtLogin = new CheckBox { Location = new Point(20, 88), Size = new Size(400, 26), TabIndex = 3 };
            save = new Button { Location = new Point(222, 136), Size = new Size(94, 32), TabIndex = 4 };
            cancel = new Button { Location = new Point(326, 136), Size = new Size(94, 32), TabIndex = 5, DialogResult = DialogResult.Cancel };
            AcceptButton = save;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { languageLabel, language, quotePaths, startAtLogin, save, cancel });
        }
    }
}
