using OpenLoop.Service.Plugins;

namespace OpenLoop.App
{
    /// <summary>
    /// provider 型插件的最小编辑器：只动 contributions.providers[0] 的
    /// id/name/baseUrl/modelsUrl/modelPrefix 与同目录 key.txt，清单里别的字段（工具、MCP、models）原样写回。
    /// 读写都走 <see cref="PluginStore"/>，与 Web 设置页同一份规则。
    /// </summary>
    internal sealed partial class PluginEditForm : Form
    {
        private readonly string _workspace;
        private readonly PluginStore.Item _item;
        private readonly bool _creating;

        private PluginEditForm(string workspace, string dir)
        {
            _workspace = workspace;
            _creating = string.IsNullOrWhiteSpace(dir);
            _item = _creating ? new PluginStore.Item() : PluginStore.ReadRaw(dir);

            InitializeComponent();
            Text = _creating ? "添加插件" : "编辑插件";
            _lblNote.Text = _creating
                ? $"会新建目录：{PluginStore.RootFor(workspace)}{Path.DirectorySeparatorChar}<id>。" +
                  "清单只写 provider 贡献，模型清单靠 ModelsUrl 自动发现。"
                : $"清单声明了 {_item.Models} 个模型，本页不改 models；密钥留空表示不动现有 key.txt。" +
                  "「{root}」写进清单时会展开成清单所在目录。";
            Fill();
        }

        public static PluginEditForm Edit(string workspace, string dir) => new(workspace, dir);

        public static PluginEditForm Create(string workspace) => new(workspace, null);

        private void Fill()
        {
            _txtId.Text = _item.Id ?? "";
            _txtName.Text = _item.Name ?? "";
            _txtUrl.Text = _item.BaseUrl ?? "";
            _txtModelsUrl.Text = _item.ModelsUrl ?? "";
            _txtPrefix.Text = _item.ModelPrefix ?? "";
        }

        private void OnCancel(object sender, EventArgs e) => DialogResult = DialogResult.Cancel;

        private void OnSave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtId.Text))
            {
                AntdUI.Notification.warn(this, "缺少插件 id", "id 不能为空。", AntdUI.TAlignFrom.TR, null, 4);
                return;
            }

            _item.Id = _txtId.Text.Trim();
            _item.Name = _txtName.Text.Trim();
            _item.BaseUrl = _txtUrl.Text.Trim();
            _item.ModelsUrl = _txtModelsUrl.Text.Trim();
            _item.ModelPrefix = _txtPrefix.Text.Trim();
            _item.Key = _txtKey.Text.Trim();

            try
            {
                PluginStore.Save(_workspace, _item);
            }
            catch (Exception ex)
            {
                AntdUI.Notification.error(this, "保存失败", ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                return;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
