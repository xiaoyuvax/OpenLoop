namespace OpenLoop.App
{
    partial class TaskDraftsForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private ListBox _lstDrafts;
        private TableLayoutPanel _detail;
        private TableLayoutPanel _header;
        private AntdUI.Label _lblTitle;
        private AntdUI.Label _lblStatus;
        private TextBox _txtPreview;
        private CheckBox _chkSmoke;
        private FlowLayoutPanel _buttons;
        private AntdUI.Button _btnAccept;
        private AntdUI.Button _btnEdit;
        private AntdUI.Button _btnSave;
        private AntdUI.Button _btnDiscard;
        private AntdUI.Button _btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _lstDrafts = new ListBox();
            _detail = new TableLayoutPanel();
            _header = new TableLayoutPanel();
            _lblTitle = new AntdUI.Label();
            _lblStatus = new AntdUI.Label();
            _txtPreview = new TextBox();
            _chkSmoke = new CheckBox();
            _buttons = new FlowLayoutPanel();
            _btnAccept = new AntdUI.Button();
            _btnEdit = new AntdUI.Button();
            _btnSave = new AntdUI.Button();
            _btnDiscard = new AntdUI.Button();
            _btnClose = new AntdUI.Button();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.RowCount = 1;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.BackColor = UiStyle.Surface;
            _root.Padding = new Padding(12, 10, 12, 8);

            _lstDrafts.Dock = DockStyle.Fill;
            _lstDrafts.IntegralHeight = false;
            _lstDrafts.Font = new Font("Segoe UI", 9F);
            _lstDrafts.Margin = new Padding(0, 0, 10, 0);
            _lstDrafts.SelectedIndexChanged += OnDraftSelected;

            _detail.Dock = DockStyle.Fill;
            _detail.ColumnCount = 1;
            _detail.RowCount = 4;
            _detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            _detail.Margin = new Padding(0);

            _header.Dock = DockStyle.Fill;
            _header.ColumnCount = 1;
            _header.RowCount = 2;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _header.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            _header.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            _header.Margin = new Padding(0);

            _lblTitle.Dock = DockStyle.Fill;
            _lblTitle.Text = Localization.T("TaskDrafts");
            _lblTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _lblTitle.ForeColor = UiStyle.Text;
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.Margin = new Padding(0);

            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.AutoEllipsis = true;
            _lblStatus.Font = new Font("Segoe UI", 8.5F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Margin = new Padding(0);

            _header.Controls.Add(_lblTitle, 0, 0);
            _header.Controls.Add(_lblStatus, 0, 1);

            _txtPreview.Dock = DockStyle.Fill;
            _txtPreview.Multiline = true;
            _txtPreview.ReadOnly = true;
            _txtPreview.ScrollBars = ScrollBars.Both;
            _txtPreview.WordWrap = false;
            _txtPreview.Font = new Font("Consolas", 9.5F);
            _txtPreview.BackColor = UiStyle.Page;
            _txtPreview.ForeColor = UiStyle.Text;
            _txtPreview.BorderStyle = BorderStyle.FixedSingle;
            _txtPreview.Margin = new Padding(0, 4, 0, 6);

            _chkSmoke.AutoSize = true;
            _chkSmoke.Text = Localization.T("BaselineSmoke");
            _chkSmoke.Font = new Font("Segoe UI", 9F);
            _chkSmoke.ForeColor = UiStyle.Muted;
            _chkSmoke.Margin = new Padding(0, 6, 0, 0);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnClose.Text = Localization.T("Close");
            _btnClose.Radius = 8;
            _btnClose.Size = new Size(80, 32);
            _btnClose.Margin = new Padding(8, 6, 0, 0);
            _btnClose.Click += OnClose;

            _btnDiscard.Text = Localization.T("Discard");
            _btnDiscard.Radius = 8;
            _btnDiscard.Size = new Size(80, 32);
            _btnDiscard.Margin = new Padding(8, 6, 0, 0);
            _btnDiscard.Click += OnDiscard;

            _btnEdit.Text = Localization.T("EditDraft");
            _btnEdit.Radius = 8;
            _btnEdit.Size = new Size(80, 32);
            _btnEdit.Margin = new Padding(8, 6, 0, 0);
            _btnEdit.Click += OnEdit;

            _btnSave.Text = Localization.T("SaveDraft");
            _btnSave.Radius = 8;
            _btnSave.Size = new Size(96, 32);
            _btnSave.Margin = new Padding(8, 6, 0, 0);
            _btnSave.Visible = false;
            _btnSave.Click += OnSaveEdit;

            _btnAccept.Text = Localization.T("Accept");
            _btnAccept.Type = AntdUI.TTypeMini.Primary;
            _btnAccept.Radius = 8;
            _btnAccept.Size = new Size(80, 32);
            _btnAccept.Margin = new Padding(8, 6, 0, 0);
            _btnAccept.Click += OnAccept;

            _buttons.Controls.Add(_btnClose);
            _buttons.Controls.Add(_btnDiscard);
            _buttons.Controls.Add(_btnSave);
            _buttons.Controls.Add(_btnEdit);
            _buttons.Controls.Add(_btnAccept);

            _detail.Controls.Add(_header, 0, 0);
            _detail.Controls.Add(_txtPreview, 0, 1);
            _detail.Controls.Add(_chkSmoke, 0, 2);
            _detail.Controls.Add(_buttons, 0, 3);

            _root.Controls.Add(_lstDrafts, 0, 0);
            _root.Controls.Add(_detail, 1, 0);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            ClientSize = new Size(980, 640);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = Localization.T("TaskDrafts");
            Controls.Add(_root);

            ResumeLayout(false);
        }
    }
}
