namespace OpenLoop.App
{
    internal sealed partial class RuleForm : Form
    {
        private readonly Func<string, string, CancellationToken, Task<string>> _optimize;

        public RuleForm(Func<string, string, CancellationToken, Task<string>> optimize = null)
        {
            _optimize = optimize;
            InitializeComponent();
            Icon = UiIcons.App;
            _btnOptimize.Visible = optimize != null;
        }

        public string RuleName => _txtName.Text.Trim();
        public string RuleText => _txtRule.Text.Trim();

        private async void OnOptimize(object sender, EventArgs e)
        {
            if (_optimize == null) return;
            if (RuleText.Length == 0)
            {
                AntdUI.Notification.warn(this, "规则为空", "先写一条规则，再让 AI 优化。",
                    AntdUI.TAlignFrom.TR, null, 4);
                _txtRule.Focus();
                return;
            }

            _btnOptimize.Enabled = false;
            _btnSave.Enabled = false;
            _lblHint.Text = "正在优化…";
            try
            {
                string improved = await _optimize(RuleName, RuleText, CancellationToken.None);
                if (IsDisposed) return;
                if (string.IsNullOrWhiteSpace(improved))
                {
                    _lblHint.Text = "模型没有返回规则，保持原文。";
                    return;
                }
                _txtRule.Text = improved;
                _lblHint.Text = "已优化，请检查后登记（不会自动保存）。";
            }
            catch (Exception ex)
            {
                if (!IsDisposed) _lblHint.Text = "优化失败：" + ex.Message;
            }
            finally
            {
                if (!IsDisposed)
                {
                    _btnOptimize.Enabled = true;
                    _btnSave.Enabled = true;
                }
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (RuleText.Length == 0)
            {
                AntdUI.Notification.warn(this, "规则为空", "请填写规则正文。",
                    AntdUI.TAlignFrom.TR, null, 4);
                _txtRule.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }

        private void OnCancel(object sender, EventArgs e) => DialogResult = DialogResult.Cancel;
    }
}
