using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;

namespace OpenLoop.App
{
    internal sealed partial class SettingsForm : Form
    {
        private readonly AppSettings _source;
        private bool _filling;

        public SettingsForm(AppSettings source)
        {
            _source = source ?? new AppSettings();
            InitializeComponent();
            Icon = UiIcons.App;
            _txtBaseUrl.Text = _source.BaseUrl;
            _txtKey.Text = AppSettings.Unprotect(_source.KeyProtected);
            FillModels(ModelCatalog.All.Select(m => m.Id), _source.Model);
        }

        public AppSettings Result { get; private set; }

        private string ResolveKey()
        {
            string key = _txtKey.Text.Trim();
            if (key.Length > 0) return key;
            return Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? "";
        }

        private void FillModels(IEnumerable<string> ids, string selected)
        {
            _filling = true;
            try
            {
                _cboModel.Items.Clear();
                foreach (string id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
                    _cboModel.Items.Add(new AntdUI.SelectItem(id) { Tag = id });

                if (!string.IsNullOrWhiteSpace(selected)
                    && !_cboModel.Items.Cast<AntdUI.SelectItem>().Any(i => (string)i.Tag == selected))
                    _cboModel.Items.Add(new AntdUI.SelectItem(selected) { Tag = selected });

                _cboModel.SelectedValue = selected;
            }
            finally
            {
                _filling = false;
            }
        }

        private async void OnFetchModels(object sender, EventArgs e)
        {
            _btnFetch.Enabled = false;
            _lblStatus.Text = "正在拉取免费模型…";
            try
            {
                var ids = await OpenRouterCatalog.FetchFreeModelsAsync(ResolveKey());
                FillModels(ids, SelectedModel());
                _lblStatus.Text = $"免费模型 {ids.Count} 个";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "拉取失败";
                AppLog.Error("拉取免费模型失败", ex);
                AntdUI.Notification.error(this, "拉取失败", ex.Message, AntdUI.TAlignFrom.TR, null, 5);
            }
            finally
            {
                _btnFetch.Enabled = true;
            }
        }

        private async void OnTestProvider(object sender, EventArgs e)
        {
            string baseUrl = _txtBaseUrl.Text.Trim();
            string model = SelectedModel();
            if (baseUrl.Length == 0 || string.IsNullOrWhiteSpace(model))
            {
                AntdUI.Notification.warn(this, "缺少配置", "接口地址与模型都要填。", AntdUI.TAlignFrom.TR, null, 4);
                return;
            }

            _btnTest.Enabled = false;
            _lblStatus.Text = "正在测试连接…";
            try
            {
                var reasoner = new OpenAiCompatibleReasoner(baseUrl, model, ResolveKey());
                var reply = await reasoner.CompleteAsync([new LlmMessage("user", "只回复两个字母：ok")]);
                _lblStatus.Text = "连接正常";
                AntdUI.Notification.success(this, "连接正常",
                    $"{model} → {reply.Content?.Trim()}", AntdUI.TAlignFrom.TR, null, 4);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "连接失败";
                AppLog.Error("测试连接失败", ex);
                AntdUI.Notification.error(this, "连接失败", ex.Message, AntdUI.TAlignFrom.TR, null, 6);
            }
            finally
            {
                _btnTest.Enabled = true;
            }
        }

        private string SelectedModel()
        {
            if (_filling) return _source.Model;
            return _cboModel.SelectedValue as string ?? _source.Model;
        }

        private void OnSave(object sender, EventArgs e)
        {
            string baseUrl = _txtBaseUrl.Text.Trim();
            string model = SelectedModel();
            if (baseUrl.Length == 0 || string.IsNullOrWhiteSpace(model))
            {
                AntdUI.Notification.warn(this, "缺少配置", "接口地址与模型都要填。", AntdUI.TAlignFrom.TR, null, 4);
                return;
            }

            Result = new AppSettings
            {
                BaseUrl = baseUrl,
                Model = model,
                KeyProtected = AppSettings.Protect(_txtKey.Text.Trim()),
                Workspace = _source.Workspace,
                ReasoningEffort = _source.ReasoningEffort,
                LoopCommand = _source.LoopCommand,
                OnlyFreeModels = _source.OnlyFreeModels
            };
            DialogResult = DialogResult.OK;
        }

        private void OnCancel(object sender, EventArgs e) => DialogResult = DialogResult.Cancel;
    }
}
