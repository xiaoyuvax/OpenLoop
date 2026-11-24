namespace OpenLoop.App
{
    /// <summary>单个模型的属性页；返回值交给设置页写进逐模型覆盖。</summary>
    public partial class ModelPropsForm : Form
    {
        /// <summary>下拉里的常用窗口；第一项 0 表示自动。</summary>
        private static readonly (string Label, int Value)[] Choices =
        [
            (Localization.T("ModelCtxAutoChoice"), 0),
            ("32K", 32768),
            ("64K", 65536),
            ("128K", 131072),
            ("200K", 200000),
            ("256K", 262144),
            ("512K", 524288),
            ("1M", 1000000)
        ];

        private string _autoText = "";

        public ModelPropsForm() => InitializeComponent();

        /// <summary>模型 id、显示名称、已保存的上下文覆盖；autoLimit 是当前自动解析出来的值。</summary>
        public void Initialize(string modelId, string displayName, int contextLimit, int autoLimit)
        {
            _txtId.Text = modelId ?? "";
            _txtName.Text = displayName ?? "";
            _autoText = autoLimit > 0 ? autoLimit.ToString("N0") : Localization.T("Unknown");

            foreach (var (label, value) in Choices)
                _selCtx.Items.Add(new AntdUI.SelectItem(label) { Tag = value });
            int index = Array.FindIndex(Choices, c => c.Value == contextLimit);
            _selCtx.SelectedIndex = index >= 0 ? index : 0;
            _selCtx.SelectedIndexChanged += (_, _) => UpdateHint();
            UpdateHint();
        }

        public string ModelId => _txtId.Text.Trim();

        public string DisplayName => _txtName.Text.Trim();

        public int ContextLimit => _selCtx.SelectedValue is int value ? value : 0;

        private void UpdateHint()
        {
            _lblHint.Text = ContextLimit > 0
                ? string.Format(Localization.T("ModelCtxFixed"), ContextLimit.ToString("N0"), _autoText)
                : string.Format(Localization.T("ModelCtxAutoHint"), _autoText);
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (ModelId.Length == 0)
            {
                AntdUI.Notification.warn(this, Localization.T("MissingModelId"), Localization.T("MissingModelIdBody"), AntdUI.TAlignFrom.TR, null, 4);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        private void OnCancel(object sender, EventArgs e) => DialogResult = DialogResult.Cancel;
    }
}
