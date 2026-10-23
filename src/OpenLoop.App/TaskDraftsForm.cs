using System.Text;
using System.Text.Json;
using OpenLoop.Core.Store;

namespace OpenLoop.App
{
    /// <summary>Loop 起草的考核题：人在这里确认、编辑或丢弃；确认后才写入 eval/tasks。</summary>
    internal sealed partial class TaskDraftsForm : Form
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _workspace;
        private readonly Func<EvalTask, CancellationToken, Task<string>> _smoke;
        private List<TaskDraft> _drafts = [];
        private bool _editing;
        private bool _refreshing;

        public TaskDraftsForm(string workspace, Func<EvalTask, CancellationToken, Task<string>> smoke = null)
        {
            _workspace = workspace;
            _smoke = smoke;
            InitializeComponent();
            Icon = UiIcons.App;
            _chkSmoke.Visible = smoke != null;
            RefreshList();
        }

        public event Action Changed;

        private TaskDraft Selected => (_lstDrafts.SelectedItem as DraftItem)?.Draft;

        private void RefreshList(string selectId = null)
        {
            _refreshing = true;
            try
            {
                _drafts = TaskDrafts.Load(_workspace)
                    .OrderBy(d => d.Status == TaskDrafts.StatusDraft ? 0 : 1)
                    .ThenByDescending(d => d.CreatedAt, StringComparer.Ordinal)
                    .ToList();
                _lstDrafts.Items.Clear();
                foreach (var draft in _drafts)
                    _lstDrafts.Items.Add(new DraftItem(draft));

                int index = 0;
                if (selectId != null)
                {
                    for (int i = 0; i < _lstDrafts.Items.Count; i++)
                        if (((DraftItem)_lstDrafts.Items[i]).Draft.Id == selectId) { index = i; break; }
                }
                if (_lstDrafts.Items.Count > 0) _lstDrafts.SelectedIndex = index;
            }
            finally
            {
                _refreshing = false;
            }

            _editing = false;
            _txtPreview.ReadOnly = true;
            _btnSave.Visible = false;
            UpdateButtons();
            ShowDraft(Selected);
        }

        private void OnDraftSelected(object sender, EventArgs e)
        {
            if (_refreshing) return;
            _editing = false;
            _txtPreview.ReadOnly = true;
            _btnSave.Visible = false;
            UpdateButtons();
            ShowDraft(Selected);
        }

        private void ShowDraft(TaskDraft draft)
        {
            _lblStatus.Text = "";
            if (draft == null)
            {
                _txtPreview.Text = _drafts.Count == 0
                    ? Localization.T("DraftsEmpty")
                    : Localization.T("PickDraft");
                return;
            }

            if (_editing)
            {
                _txtPreview.Text = JsonSerializer.Serialize(draft.Task, Json);
                return;
            }

            _txtPreview.Text = Format(draft);
        }

        private static string Format(TaskDraft draft)
        {
            var sb = new StringBuilder();
            sb.AppendLine("状态: " + draft.Status);
            sb.AppendLine("草稿: " + draft.Id);
            sb.AppendLine("题目 id: " + draft.Task.Id);
            sb.AppendLine("问题: " + (draft.Task.Problem ?? ""));
            sb.AppendLine("理由: " + (draft.Reason ?? ""));
            sb.AppendLine("来源: " + (draft.Source?.Summary ?? "—"));
            sb.AppendLine("创建: " + (draft.CreatedAt ?? ""));
            if (!string.IsNullOrWhiteSpace(draft.DecidedAt))
                sb.AppendLine($"决定: {draft.Status} · {draft.DecidedBy} · {draft.DecidedAt}");
            sb.AppendLine();
            sb.AppendLine("提示词:");
            sb.AppendLine(draft.Task.Prompt ?? "");
            sb.AppendLine();

            sb.AppendLine("起始文件:");
            if (draft.Task.Seed is { Count: > 0 })
            {
                foreach (var pair in draft.Task.Seed)
                    sb.Append("  ").Append(pair.Key).Append("  (").Append((pair.Value ?? "").Length).AppendLine(" 字符)");
            }
            else
            {
                sb.AppendLine("  （无）");
            }
            sb.AppendLine();

            sb.AppendLine("判定条件:");
            foreach (var check in draft.Task.Checks ?? [])
            {
                sb.Append("  - ").Append(check.Type);
                if (!string.IsNullOrWhiteSpace(check.Path)) sb.Append("  ").Append(check.Path);
                if (!string.IsNullOrWhiteSpace(check.Text)) sb.Append("  「").Append(check.Text).Append('」');
                if (check.Args is { Count: > 0 }) sb.Append("  [").Append(string.Join(" ", check.Args)).Append(']');
                if (check.Type == "command") sb.Append("  expectExit=").Append(check.ExpectExit);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private void UpdateButtons()
        {
            bool canDecide = Selected != null && Selected.Status == TaskDrafts.StatusDraft && !_editing;
            _btnAccept.Enabled = canDecide;
            _btnEdit.Enabled = canDecide;
            _btnDiscard.Enabled = canDecide;
        }

        private bool SaveEdit()
        {
            var draft = Selected;
            if (draft == null) return false;

            EvalTask task;
            try
            {
                task = JsonSerializer.Deserialize<EvalTask>(_txtPreview.Text, Json);
            }
            catch (JsonException ex)
            {
                _lblStatus.Text = "JSON 解析失败：" + ex.Message;
                return false;
            }

            try
            {
                TaskDrafts.Update(_workspace, draft.Id, task);
                _editing = false;
                _txtPreview.ReadOnly = true;
                _btnSave.Visible = false;
                RefreshList(draft.Id);
                _lblStatus.Text = "已保存修改。";
                return true;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = ex.Message;
                return false;
            }
        }

        private void OnEdit(object sender, EventArgs e)
        {
            var draft = Selected;
            if (draft == null) return;
            _editing = true;
            _txtPreview.ReadOnly = false;
            _btnSave.Visible = true;
            UpdateButtons();
            ShowDraft(draft);
            _lblStatus.Text = "正在编辑 JSON；保存后回到预览。";
            _txtPreview.Focus();
        }

        private void OnSaveEdit(object sender, EventArgs e) => SaveEdit();

        private async void OnAccept(object sender, EventArgs e)
        {
            if (_editing && !SaveEdit()) return;

            var draft = Selected;
            if (draft == null) return;
            try
            {
                string path = TaskDrafts.Accept(_workspace, draft.Id);
                _lblStatus.Text = Localization.T("DraftAccepted") + " " + Path.GetFileName(path);
                Changed?.Invoke();
                RefreshList(draft.Id);

                if (_chkSmoke.Checked && _smoke != null)
                {
                    _btnAccept.Enabled = false;
                    _lblStatus.Text = Localization.T("BaselineRunning");
                    try
                    {
                        string result = await _smoke(draft.Task, CancellationToken.None);
                        if (!IsDisposed) _lblStatus.Text = result;
                    }
                    catch (Exception ex)
                    {
                        if (!IsDisposed) _lblStatus.Text = "基线试跑失败：" + ex.Message;
                    }
                    finally
                    {
                        if (!IsDisposed) UpdateButtons();
                    }
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = ex.Message;
            }
        }

        private void OnDiscard(object sender, EventArgs e)
        {
            var draft = Selected;
            if (draft == null) return;
            if (MessageBox.Show(this, "丢弃草稿「" + draft.Task.Id + "」？不会删除已有题集。", "丢弃草稿",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                TaskDrafts.Reject(_workspace, draft.Id);
                Changed?.Invoke();
                RefreshList();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = ex.Message;
            }
        }

        private void OnClose(object sender, EventArgs e) => Close();

        private sealed class DraftItem
        {
            public DraftItem(TaskDraft draft) => Draft = draft;

            public TaskDraft Draft { get; }

            public override string ToString()
            {
                string mark = Draft.Status switch
                {
                    TaskDrafts.StatusDraft => "待确认",
                    TaskDrafts.StatusAccepted => "已采纳",
                    _ => "已丢弃"
                };
                string title = Draft.Task?.Problem ?? Draft.Task?.Id ?? Draft.Id;
                if (title.Length > 18) title = title[..18] + "…";
                return mark + " · " + title;
            }
        }
    }
}
