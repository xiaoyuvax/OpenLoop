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
                AppNotify.warn(this, Localization.T("RuleEmpty"), Localization.T("RuleEmptyOptimizeBody"),
                    AntdUI.TAlignFrom.TR, null, 4);
                _txtRule.Focus();
                return;
            }

            _btnOptimize.Enabled = false;
            _btnSave.Enabled = false;
            _lblHint.Text = Localization.T("RuleOptimizing");
            try
            {
                string improved = await _optimize(RuleName, RuleText, CancellationToken.None);
                if (IsDisposed) return;
                if (string.IsNullOrWhiteSpace(improved))
                {
                    _lblHint.Text = Localization.T("RuleOptimizeEmpty");
                    return;
                }
                _txtRule.Text = improved;
                _lblHint.Text = Localization.T("RuleOptimized");
            }
            catch (Exception ex)
            {
                if (!IsDisposed) _lblHint.Text = Localization.T("RuleOptimizeFailed") + ex.Message;
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
                AppNotify.warn(this, Localization.T("RuleEmpty"), Localization.T("RuleEmptySaveBody"),
                    AntdUI.TAlignFrom.TR, null, 4);
                _txtRule.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }

        private void OnCancel(object sender, EventArgs e) => DialogResult = DialogResult.Cancel;
    }
}
