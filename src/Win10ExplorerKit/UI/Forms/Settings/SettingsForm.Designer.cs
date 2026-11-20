using System.Drawing;
using System.Windows.Forms;

namespace Win10ExplorerKit.UI.Forms.Settings
{
    internal sealed partial class SettingsForm
    {
        private TabControl pages;
        private TabPage generalPage;
        private TabPage foldersPage;
        private CheckBox remember;
        private Label foldersLabel;
        private TextBox fixedFolders;
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
            ClientSize = new Size(468, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            pages = new TabControl { Location = new Point(12, 12), Size = new Size(444, 230),
                Font = new Font("Segoe UI", 9F), TabIndex = 0 };
            generalPage = new TabPage { UseVisualStyleBackColor = true, Font = Font };
            foldersPage = new TabPage { UseVisualStyleBackColor = true, Font = Font };
            pages.TabPages.AddRange(new[] { generalPage, foldersPage });
            remember = new CheckBox { Location = new Point(14, 10), Size = new Size(404, 28), TabIndex = 0 };
            foldersLabel = new Label { Location = new Point(14, 48), Size = new Size(404, 23), TabIndex = 1 };
            fixedFolders = new TextBox { Location = new Point(14, 76), Size = new Size(404, 108),
                Multiline = true, AcceptsReturn = true, WordWrap = false, ScrollBars = ScrollBars.Both, TabIndex = 2 };
            foldersPage.Controls.AddRange(new Control[] { remember, foldersLabel, fixedFolders });
            languageLabel = new Label { Location = new Point(20, 19), Size = new Size(130, 26), TabIndex = 0 };
            language = new ComboBox { Location = new Point(160, 16), Size = new Size(260, 28), Font = new Font("Segoe UI", 9F), DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 1 };
            quotePaths = new CheckBox { Location = new Point(20, 56), Size = new Size(400, 26), TabIndex = 2 };
            startAtLogin = new CheckBox { Location = new Point(20, 88), Size = new Size(400, 26), TabIndex = 3 };
            save = new Button { Location = new Point(258, 254), Size = new Size(94, 32), TabIndex = 4 };
            cancel = new Button { Location = new Point(362, 254), Size = new Size(94, 32), TabIndex = 5, DialogResult = DialogResult.Cancel };
            AcceptButton = save;
            CancelButton = cancel;
            generalPage.Controls.AddRange(new Control[] { languageLabel, language, quotePaths, startAtLogin });
            Controls.AddRange(new Control[] { pages, save, cancel });
        }
    }
}
