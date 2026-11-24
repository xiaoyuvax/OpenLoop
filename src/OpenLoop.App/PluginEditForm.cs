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
            Text = Localization.T(_creating ? "PluginAddTitle" : "PluginEditTitle");
            _lblNote.Text = _creating
                ? string.Format(Localization.T("PluginNoteCreate"), PluginStore.RootFor(workspace) + Path.DirectorySeparatorChar + "<id>")
                : string.Format(Localization.T("PluginNoteEdit"), _item.Models, "{root}");
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
                AntdUI.Notification.warn(this, Localization.T("MissingPluginId"), Localization.T("MissingPluginIdBody"), AntdUI.TAlignFrom.TR, null, 4);
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
                AntdUI.Notification.error(this, Localization.T("SaveFailed"), ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                return;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
