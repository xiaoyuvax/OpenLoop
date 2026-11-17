namespace OpenLoop.App
{
    partial class PuppetView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _header;
        private AntdUI.Label _lblTitle;
        private AntdUI.Label _lblStatus;
        private Panel _line;
        private Panel _body;
        private ChatFlow.ChatFlowControl _chat;
        private TableLayoutPanel _inputArea;
        private AntdUI.Input _txtInput;
        private TableLayoutPanel _toolbar;
        private AntdUI.Button _btnSend;
        private CommandPopup _cmdPopup;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _header = new TableLayoutPanel();
            _lblTitle = new AntdUI.Label();
            _lblStatus = new AntdUI.Label();
            _line = new Panel();
            _body = new Panel();
            _chat = new ChatFlow.ChatFlowControl();
            _inputArea = new TableLayoutPanel();
            _txtInput = new AntdUI.Input();
            _toolbar = new TableLayoutPanel();
            _btnSend = new AntdUI.Button();
            _cmdPopup = new CommandPopup();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.BackColor = UiStyle.Page;

            _header.Dock = DockStyle.Fill;
            _header.ColumnCount = 3;
            _header.RowCount = 1;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _header.BackColor = UiStyle.Surface;
            _header.Padding = new Padding(16, 0, 16, 0);

            _lblTitle.Dock = DockStyle.Fill;
            _lblTitle.Text = "Puppet";
            _lblTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _lblTitle.ForeColor = UiStyle.Puppet;
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.AutoEllipsis = true;
            _lblTitle.PrefixSvg = UiIcons.Of(UiIcons.List, UiStyle.Puppet);
            _lblTitle.IconGap = 6;
            _lblTitle.Margin = new Padding(0);

            _lblStatus.AutoSize = true;
            _lblStatus.Text = "";
            _lblStatus.Font = new Font("Segoe UI", 8.5F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.TextAlign = ContentAlignment.MiddleRight;
            _lblStatus.Margin = new Padding(8, 0, 0, 0);

            _header.Controls.Add(_lblTitle, 1, 0);
            _header.Controls.Add(_lblStatus, 2, 0);

            _line.Dock = DockStyle.Fill;
            _line.BackColor = UiStyle.Border;
            _line.Margin = new Padding(0);

            _body.Dock = DockStyle.Fill;
            _body.BackColor = UiStyle.Page;

            _chat.Dock = DockStyle.Fill;
            _chat.BackColor = UiStyle.Surface;
            _chat.ChatFont = new Font("Microsoft YaHei UI", 9F);

            _inputArea.Dock = DockStyle.Bottom;
            _inputArea.Height = 128;
            _inputArea.ColumnCount = 1;
            _inputArea.RowCount = 2;
            _inputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _inputArea.BackColor = UiStyle.Page;
            _inputArea.Padding = new Padding(16, 4, 16, 4);

            _txtInput.Name = "txtInput";
            _txtInput.Dock = DockStyle.Fill;
            _txtInput.Multiline = true;
            _txtInput.Radius = 10;
            _txtInput.BorderWidth = 1;
            _txtInput.BackColor = UiStyle.Surface;
            _txtInput.Font = new Font("Segoe UI", 10F);
            _txtInput.PlaceholderText = "以用户身份在留言板上发言；输入 / 呼出工具";

            _toolbar.Dock = DockStyle.Fill;
            _toolbar.ColumnCount = 1;
            _toolbar.RowCount = 1;
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _toolbar.Margin = new Padding(0, 4, 0, 0);

            _btnSend.Name = "btnSend";
            _btnSend.Text = Localization.T("Send");
            _btnSend.Type = AntdUI.TTypeMini.Primary;
            _btnSend.Radius = 8;
            _btnSend.Width = 96;
            _btnSend.Dock = DockStyle.Right;
            _btnSend.IconSvg = UiIcons.Of(UiIcons.Send, Color.White);
            _btnSend.Margin = new Padding(0);
            _toolbar.Controls.Add(_btnSend, 0, 0);

            _inputArea.Controls.Add(_txtInput, 0, 0);
            _inputArea.Controls.Add(_toolbar, 0, 1);

            _body.Controls.Add(_chat);
            _body.Controls.Add(_inputArea);
            _body.Controls.Add(_cmdPopup);

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(_line, 0, 1);
            _root.Controls.Add(_body, 0, 2);

            _cmdPopup.Name = "cmdPopup";
            _cmdPopup.Dock = DockStyle.None;
            _cmdPopup.Font = new Font("Microsoft YaHei UI", 9.5F);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            Controls.Add(_root);
            ResumeLayout(false);
        }
    }
}
