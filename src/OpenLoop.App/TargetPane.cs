using System.Security.Cryptography;
using System.Text;
using FastColoredTextBoxNS;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Store;
using Puppet.Core.AppAgent;

namespace OpenLoop.App
{
    /// <summary>
    /// TargetCentricSession 的目标对象：一个可修订、可呈现的东西。
    /// 现在只有 prompt / agents 两种文本目标；以后加 svg/gltf/web 只是换 Kind 与呈现控件。
    /// </summary>
    internal sealed class TargetSpec
    {
        internal const string PromptKind = PromptCatalog.SystemKind;
        internal const string AgentsKind = PromptCatalog.AgentsKind;

        /// <summary>prompt | agents | …（以后：svg / gltf / web）。</summary>
        internal string Kind { get; init; }
        internal string Title { get; init; }
        internal string Path { get; init; }

        /// <summary>「实际」形态（只读），没有就隐藏那一页。</summary>
        internal Func<string> Actual { get; init; }

        /// <summary>编辑的是候选包（A/B 评测模式）：面板顶部浅黄底提示，别让人以为已生效。</summary>
        internal bool Candidate { get; init; }

        /// <param name="seedSource">候选包的播种来源（正式文件可能不在本工作区，见 Loop）。</param>
        /// <param name="actualWorkspace">「实际」页去哪个工作区找 trace；null = 本工作区。</param>
        internal static TargetSpec Prompt(string workspace, string seedSource = null, string actualWorkspace = null)
        {
            bool eval = HarnessMode.IsEval(workspace);
            return new()
            {
                Kind = PromptCatalog.SystemKind,
                Title = eval ? "系统提示词模板（候选）" : "系统提示词模板",
                Path = PromptCatalog.EditingPath(workspace, PromptCatalog.SystemKind, seedSource),
                Actual = () => NewestTracePrompt(actualWorkspace ?? workspace),
                Candidate = eval
            };
        }

        /// <param name="seedSource">候选包的播种来源（Loop 的正式 AGENTS.md 在源码根，不在会话工作区）。</param>
        internal static TargetSpec Agents(string workspace, string seedSource = null)
        {
            bool eval = HarnessMode.IsEval(workspace);
            return new()
            {
                Kind = PromptCatalog.AgentsKind,
                Title = eval ? "AGENTS.md（候选）" : "AGENTS.md",
                Path = PromptCatalog.EditingPath(workspace, PromptCatalog.AgentsKind, seedSource),
                Candidate = eval
            };
        }

        /// <summary>最近一次运行实际注入的系统提示词（trace 里落的 system.md）。</summary>
        internal static string NewestTracePrompt(string workspace)
        {
            try
            {
                string dir = System.IO.Path.Combine(workspace, ".openloop", "traces");
                if (!Directory.Exists(dir)) return null;
                var latest = Directory.GetDirectories(dir)
                    .OrderByDescending(d => System.IO.Path.GetFileName(d), StringComparer.Ordinal)
                    .FirstOrDefault(d => File.Exists(System.IO.Path.Combine(d, "system.md")));
                return latest == null
                    ? null
                    : File.ReadAllText(System.IO.Path.Combine(latest, "system.md"), Encoding.UTF8);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 会话左栏的目标面板（TCF 的第一个实例）：文本驻留内存、agent 经 Puppet 改、用户确认后落盘。
    /// 用户仍不能直接打字（面板只读）；改动来源是模型经由本面板的动作，不是文件工具。
    /// 缓冲就是编辑器文本，不另存副本；折叠面板不丢内容。
    /// </summary>
    internal sealed partial class TargetPane : UserControl
    {
        private const string Clean = "clean";
        private const string Dirty = "dirty";
        private const string Pending = "pendingSave";
        private const string Failed = "failed";

        private TargetSpec _spec;
        private Action<string> _changed;
        private FileSystemWatcher _watcher;
        private System.Windows.Forms.Timer _debounce;
        private bool _actual;
        private Form _undocked;
        private string _session = "";
        private bool _expanded;

        private string _status = Clean;
        private string _lastError = "";
        private bool _diskChanged;
        private bool _bom;
        private bool _crlf = true;
        private string _baseline;
        private string _diskText = "";

        internal TargetPane()
        {
            InitializeComponent();
            // 面板被拆掉（切会话、关窗）时把 watcher 与防抖 Timer 一起收掉，否则后台还在跑
            Disposed += (_, _) => Shutdown();
            SizeChanged += (_, _) => FitBanner();
        }

        internal string Kind => _spec?.Kind ?? "";
        internal string Title => _spec?.Title ?? "";
        internal string Path => _spec?.Path ?? "";
        internal bool Candidate => _spec?.Candidate == true;
        internal bool IsDirty => _status != Clean;
        private string Buffer => _text.Text;

        /// <summary>用户点「关闭」：宿主收起目标模式（面板不自己拆，由会话决定布局）。</summary>
        internal event Action CloseRequested;

        /// <summary>
        /// 绑定/切换目标。同一目标（Kind+Path）只展开，不重读脏缓冲。
        /// 有未保存内容且目标变了：壳自己问用户（保存/放弃/取消），取消返回 false。
        /// </summary>
        internal bool Bind(TargetSpec spec, string session, Action<string> changed = null)
        {
            if (spec != null && SameTarget(spec, _spec))
            {
                _session = session ?? "";
                _changed = changed;
                Expand();
                return true;
            }
            if (!ResolveDirty("切换编辑目标")) return false;

            Shutdown();
            // 已 undock 的正文控件在独立窗体里：换目标/收起前先关窗，让它回到面板
            if (_undocked != null && !_undocked.IsDisposed) _undocked.Close();
            _spec = spec;
            _session = session ?? "";
            _changed = changed;
            _lblTitle.Text = spec?.Title ?? "";
            _segMode.Visible = spec?.Actual != null;
            _actual = false;
            _segMode.SelectIndex = 0;
            _banner.Visible = spec?.Candidate == true;
            if (_banner.Visible) ShowBanner();
            _tips.SetToolTip(_lblTitle, Path);
            _expanded = true;
            Watch();
            LoadFromDisk(register: true);
            return true;
        }

        /// <summary>折叠面板：内容与脏标志留在控件里，只停掉 watcher。</summary>
        internal void Collapse()
        {
            if (_undocked != null && !_undocked.IsDisposed) _undocked.Close();
            Shutdown();
            _expanded = false;
        }

        /// <summary>重新展开：干净就重读磁盘，脏就保留并标记「磁盘已变」。</summary>
        internal void Expand()
        {
            _expanded = true;
            Watch();
            Reload();
            UpdateSaveButton();
        }

        private static bool SameTarget(TargetSpec a, TargetSpec b)
            => b != null
               && string.Equals(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase)
               && string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);

        /// <summary>A/B 评测模式：改动只落候选包，用浅黄底提示，别让人以为已经生效。</summary>
        private void ShowBanner()
        {
            _banner.Text = "A/B 评测模式：改动只写入候选包，考核题更好才启用；正式文件与当前生效内容不变。";
            FitBanner();
        }

        /// <summary>面板是竖窄栏，提示按真实宽度折行量高度（与 HarnessView 同一做法）。</summary>
        private void FitBanner()
        {
            if (!_banner.Visible) return;
            int width = Math.Max(120, ClientSize.Width - _banner.Padding.Horizontal);
            int height = TextRenderer.MeasureText(
                _banner.Text, _banner.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height;
            _banner.Height = Math.Max(28, height + _banner.Padding.Vertical + 2);
        }

        /// <summary>watcher / 重新展开时调：脏缓冲不覆盖，只记「磁盘已变」。</summary>
        internal void Reload()
        {
            if (_spec == null || IsDisposed) return;
            if (_actual)
            {
                ShowBuffer(_spec.Actual?.Invoke() ?? "");
                return;
            }

            var raw = TryReadRaw(_spec.Path);
            if (raw == null) return;   // 正在被写：watcher 随后还会再来一次
            if (IsDirty)
            {
                _diskChanged = !SameHash(raw.Value.Hash, _baseline);
                UpdateSaveButton();
                return;
            }

            string before = _diskText;
            _bom = raw.Value.Bom;
            _crlf = raw.Value.Crlf;
            _baseline = raw.Value.Hash;
            _diskText = raw.Value.Text;
            _diskChanged = false;
            _status = Clean;
            _lastError = "";
            ShowBuffer(_diskText);
            UpdateEmptyHint();
            UpdateSaveButton();
            // 磁盘内容真的变了才登记（内容没变不重复登记）：WriteFile 这条旧路径改的文件也得激活，
            // 否则模型落了盘但运行期还在用旧模板。保存成功后 TrySave 另登记一次。
            if (!string.Equals(before, _diskText, StringComparison.Ordinal)) _changed?.Invoke(_diskText);
        }

        private void LoadFromDisk(bool register)
        {
            var raw = TryReadRaw(_spec.Path);
            _diskText = raw?.Text ?? "";
            _bom = raw?.Bom ?? false;
            _crlf = raw?.Crlf ?? true;
            _baseline = raw?.Hash;
            _status = Clean;
            _lastError = "";
            _diskChanged = false;
            _actual = false;
            ShowBuffer(_diskText);
            UpdateEmptyHint();
            UpdateSaveButton();
            if (register) _changed?.Invoke(_diskText);
        }

        private void ShowBuffer(string text)
        {
            if (string.Equals(_text.Text, text, StringComparison.Ordinal)) return;
            int caret = _text.SelectionStart;
            _text.Text = text ?? "";
            _text.SelectionStart = Math.Min(caret, _text.Text.Length);
        }

        /// <summary>空面板要能一眼看出「读的是哪个文件」：指错路径或文件还没建时不该只有一片空白。</summary>
        private void UpdateEmptyHint()
        {
            if (_spec == null) return;
            string title = _spec.Title ?? "";
            if (_baseline == null) title += "（文件不存在，保存时创建）";
            _lblTitle.Text = title;
        }

        internal void Shutdown()
        {
            _watcher?.Dispose();
            _watcher = null;
            _debounce?.Stop();
            _debounce?.Dispose();
            _debounce = null;
        }

        /// <summary>upsert：目录里文件被整体替换也要能感知（WriteFile 是覆盖写）。</summary>
        private void Watch()
        {
            Shutdown();
            string dir = System.IO.Path.GetDirectoryName(_spec?.Path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            _debounce = new System.Windows.Forms.Timer { Interval = 400 };
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                Reload();
            };
            _watcher = new FileSystemWatcher(dir)
            {
                Filter = System.IO.Path.GetFileName(_spec.Path),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                    | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler touch = (_, _) =>
            {
                if (IsDisposed || _debounce == null) return;
                try { BeginInvoke(new Action(() => { if (_debounce != null) { _debounce.Stop(); _debounce.Start(); } })); }
                catch (InvalidOperationException) { }
            };
            _watcher.Changed += touch;
            _watcher.Created += touch;
            _watcher.Renamed += (_, _) => touch(null, null);
        }

        private void OnModeChanged(object sender, AntdUI.IntEventArgs e)
        {
            bool actual = (e?.Value ?? 0) == 1;
            // 两页共用一个正文控件：有未保存改动时切到「实际」会把缓冲冲掉，先让用户处置
            if (actual && IsDirty && !ResolveDirty("切到「实际」页"))
            {
                _segMode.SelectIndex = 0;
                return;
            }
            _actual = actual;
            Reload();
            UpdateSaveButton();
        }

        // ---------------- Puppet 动作（由 MainForm.Target.cs 转发） ----------------

        internal OperationResult Read(int fromLine, int toLine)
        {
            if (_spec == null || IsDisposed) return Missing();
            var buffer = Buffer;
            var page = TargetText.Page(buffer, fromLine, toLine);
            return OperationResult.Success(null, new Dictionary<string, object>
            {
                ["text"] = page.Text,
                ["fromLine"] = page.From,
                ["toLine"] = page.To,
                ["totalLines"] = page.Total,
                ["chars"] = buffer.Length,
                ["nextLine"] = page.Next,
                ["state"] = StateObject(buffer)
            });
        }

        internal OperationResult Replace(int fromLine, int toLine, string text)
        {
            if (!Editable(out var error)) return error;
            int total = Buffer.Split('\n').Length;
            var result = TargetText.Replace(Buffer, fromLine, toLine, text);
            if (!result.Ok) return Fail(result.Error);
            SetBuffer(result.Text);
            return OperationResult.Success(null, new Dictionary<string, object>
            {
                ["fromLine"] = fromLine,
                ["toLine"] = toLine <= 0 ? total : toLine,
                ["state"] = StateObject()
            });
        }

        internal OperationResult Append(string text)
        {
            if (!Editable(out var error)) return error;
            SetBuffer(Buffer + Normalize(text ?? ""));
            return OperationResult.Success(null, new Dictionary<string, object>
            {
                ["state"] = StateObject()
            });
        }

        internal OperationResult ReloadFromDisk()
        {
            if (_spec == null || IsDisposed) return Missing();
            if (_actual) return Fail("当前是「实际」页（只读 trace），没有可重读的文件");
            LoadFromDisk(register: true);
            return OperationResult.Success(null, new Dictionary<string, object> { ["state"] = StateObject() });
        }

        internal OperationResult RequestSave()
        {
            if (_spec == null || IsDisposed) return Missing();
            if (_actual) return Fail("当前是「实际」页（只读 trace），不能保存");
            if (!IsDirty)
                return OperationResult.Success("没有未保存的改动", new Dictionary<string, object> { ["state"] = StateObject() });
            _status = Pending;
            UpdateSaveButton();
            return OperationResult.Success("已标记待保存：写盘要用户点保存按钮确认，现在还没写",
                new Dictionary<string, object> { ["state"] = StateObject() });
        }

        internal OperationResult DiscardEdits()
        {
            if (_spec == null || IsDisposed) return Missing();
            if (_actual) return Fail("当前是「实际」页（只读 trace），没有可丢弃的改动");
            LoadFromDisk(register: false);
            return OperationResult.Success(null, new Dictionary<string, object> { ["state"] = StateObject() });
        }

        private bool Editable(out OperationResult error)
        {
            error = null;
            if (_spec == null || IsDisposed) { error = Missing(); return false; }
            if (_actual) { error = Fail("当前是「实际」页（只读 trace），不能编辑"); return false; }
            return true;
        }

        private OperationResult Missing()
            => OperationResult.Failure("该会话没有绑定编辑目标（目标面板未打开）",
                new Dictionary<string, object> { ["state"] = StateObject() });

        private OperationResult Fail(string message)
            => OperationResult.Failure(message, new Dictionary<string, object> { ["state"] = StateObject() });

        private void SetBuffer(string text)
        {
            if (string.Equals(_text.Text, text, StringComparison.Ordinal)) return;
            int caret = _text.SelectionStart;
            _text.BeginUpdate();
            try { _text.Text = text; }
            finally { _text.EndUpdate(); }
            _text.SelectionStart = Math.Min(caret, _text.Text.Length);
            _status = Dirty;
            _lastError = "";
            var raw = TryReadRaw(_spec.Path);
            if (raw != null) _diskChanged = !SameHash(raw.Value.Hash, _baseline);
            UpdateSaveButton();
        }

        /// <summary>用户点保存（唯一写盘入口）。返回 false 时 _lastError 说明原因。</summary>
        internal bool TrySave()
        {
            if (_spec == null || _actual || IsDisposed) return false;
            try
            {
                var current = TryReadRaw(_spec.Path);
                if (current == null)
                {
                    SetFailed("磁盘文件正在被写，稍后再试");
                    return false;
                }
                if (!SameHash(current.Value.Hash, _baseline))
                {
                    SetFailed("磁盘已被改动（不是面板读入的那份）：先 TargetReload 丢弃缓冲重读，再改");
                    return false;
                }

                WriteRaw(_spec.Path, Buffer, _bom, _crlf);
                var after = TryReadRaw(_spec.Path);
                _bom = after?.Bom ?? _bom;
                _crlf = after?.Crlf ?? _crlf;
                _baseline = after?.Hash;
                _diskText = after?.Text ?? Buffer;
                _status = Clean;
                _lastError = "";
                _diskChanged = false;
                UpdateEmptyHint();
                UpdateSaveButton();
                _changed?.Invoke(Buffer);
                return true;
            }
            catch (Exception ex)
            {
                SetFailed("保存失败：" + ex.Message);
                return false;
            }
        }

        private void SetFailed(string message)
        {
            _status = Failed;
            _lastError = message;
            UpdateSaveButton();
        }

        internal Dictionary<string, object> StateObject() => StateObject(Buffer);

        internal Dictionary<string, object> StateObject(string buffer) => new()
        {
            ["kind"] = Kind,
            ["title"] = Title,
            ["path"] = Path,
            ["session"] = _session,
            ["candidate"] = _spec?.Candidate == true,
            ["actual"] = _actual,
            ["visible"] = _expanded && !IsDisposed,
            ["dirty"] = IsDirty,
            ["pendingSave"] = _status == Pending,
            ["lines"] = buffer.Split('\n').Length,
            ["chars"] = buffer.Length,
            ["lastError"] = _lastError,
            ["status"] = _status,
            ["diskChanged"] = _diskChanged
        };

        /// <summary>
        /// 启用提示（TCF）：面板打开期间拼在每条用户消息前，agent 的全部认知只来自这一块。
        /// 动作清单从 B 面 manifest 生成，接口一改提示跟着变。
        /// </summary>
        internal string Prompt(string target)
        {
            if (_spec == null) return "";
            string buffer = Buffer;
            var sb = new StringBuilder();
            sb.Append(OpenLoop.Core.Agent.HostPrompt.TargetLead).Append("用户正看着左栏「").Append(Title)
              .Append("」面板，本会话的唯一编辑目标是这个文件：").AppendLine();
            sb.Append("路径：").AppendLine(Path);
            sb.Append("sessionId=").Append(_session).Append("；行号 1-based 闭区间；面板缓冲 ")
              .Append(buffer.Split('\n').Length).Append(" 行 / ").Append(buffer.Length).AppendLine(" 字符。");
            if (Candidate) sb.AppendLine("这是 A/B 评测候选包：未过考核不生效。");
            if (_actual) sb.AppendLine("面板现在在「实际」页（只读 trace）：编辑动作会被拒绝，等用户切回编辑页。");
            if (_diskChanged) sb.AppendLine("磁盘已被外部改动：现在保存会被拒绝，先 TargetReload 再改。");
            sb.Append("动作（Puppet 工具 target=").Append(target)
              .AppendLine("、instance=MainForm、action=invoke；第一个参数都传 sessionId）：");
            var actions = PuppetActions.Cached?.Actions
                .Where(a => a.Name.StartsWith("Target", StringComparison.OrdinalIgnoreCase)).ToList();
            if (actions is { Count: > 0 })
                foreach (var action in actions)
                    sb.Append("- ").Append(action.Name).Append('（').Append(action.ParamText)
                      .Append("）：").AppendLine(action.Desc);
            else
                sb.AppendLine("- 清单暂未取到：Puppet target=... action=describe instance=MainForm（一般不必，稍后再看）。");
            sb.AppendLine("读全文按返回的 nextLine 翻页到 0；按行号改时只读目标区段。不需要先探测 manifest、不需要读文档。");
            sb.AppendLine("规则：只改这个文件，不要动其它文件；不要用 WriteFile 改它（会与面板冲突）。");
            sb.Append("改动只进内存并实时显示；写盘只由用户点保存按钮触发——TargetRequestSave 只标记待确认，不要谎报已保存。改完一句话说明改了什么。");
            return sb.ToString();
        }

        private void UpdateSaveButton()
        {
            if (_spec == null || IsDisposed) return;
            _btnSave.Enabled = !_actual;
            string tip;
            switch (_status)
            {
                case Pending:
                    _btnSave.IconSvg = UiIcons.Of(UiIcons.Clock, UiStyle.Accent);
                    tip = "待确认保存（等用户点这里）";
                    break;
                case Failed:
                    _btnSave.IconSvg = UiIcons.Of(UiIcons.Warning, UiStyle.Error);
                    tip = "保存失败：" + _lastError;
                    break;
                case Dirty:
                    _btnSave.IconSvg = UiIcons.Of(UiIcons.Edit,
                        _diskChanged ? UiStyle.Error : UiStyle.Accent);
                    tip = _diskChanged ? "有未保存的改动；磁盘已被外部改动，保存会被拒绝，先重读" : "有未保存的改动";
                    break;
                default:
                    _btnSave.IconSvg = UiIcons.Of(UiIcons.Save, UiStyle.Muted);
                    tip = "无改动";
                    break;
            }
            _tips.SetToolTip(_btnSave, tip + "\n" + Path);
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (!IsDirty)
            {
                _tips.Show("无改动", _btnSave);
                return;
            }
            if (!TrySave()) MessageBox.Show(FindForm(), _lastError, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>关闭面板：有未保存内容就壳自己三选一（保存 / 放弃 / 取消），不绕 agent。</summary>
        private void OnClose(object sender, EventArgs e)
        {
            if (!ResolveDirty("关闭")) return;
            CloseRequested?.Invoke();
        }

        /// <summary>有未保存内容时问用户。true = 可以继续（已保存或已放弃）。</summary>
        private bool ResolveDirty(string action)
        {
            if (!IsDirty) return true;
            var answer = MessageBox.Show(FindForm(),
                $"「{Title}」有未保存的改动。\n\n是 = 保存后{action}\n否 = 放弃改动后{action}\n取消 = 先不{action}",
                "未保存的改动", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes && !TrySave())
            {
                MessageBox.Show(FindForm(), _lastError, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            // 放弃：缓冲回到磁盘内容（有未保存改动的状态随之清掉）
            if (answer == DialogResult.No) LoadFromDisk(register: false);
            return true;
        }

        private void OnUndock(object sender, EventArgs e)
        {
            if (_undocked != null && !_undocked.IsDisposed) { _undocked.Activate(); return; }

            var form = new Form
            {
                Text = _lblTitle.Text,
                Width = (int)Math.Round(760 * DeviceDpi / 96.0),
                Height = (int)Math.Round(900 * DeviceDpi / 96.0),
                StartPosition = FormStartPosition.CenterScreen
            };
            // 工具栏与提示条一起搬：保存按钮/状态不能留在被折叠的面板里
            Controls.Remove(_text);
            Controls.Remove(_banner);
            Controls.Remove(_bar);
            form.Controls.Add(_text);
            form.Controls.Add(_banner);
            form.Controls.Add(_bar);
            _text.Dock = DockStyle.Fill;
            form.FormClosed += (_, _) =>
            {
                _undocked = null;
                if (IsDisposed) return;
                Controls.Add(_text);
                Controls.Add(_banner);
                Controls.Add(_bar);
                Controls.SetChildIndex(_text, 0);
                FitBanner();
            };
            _undocked = form;
            form.Show();
        }

        // ---------------- 读写的字节保真 ----------------

        /// <summary>统一成 \n：面板缓冲只有一种行分隔符，行号才稳定。</summary>
        private static string Normalize(string text) => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

        private static (string Text, bool Bom, bool Crlf, string Hash)? TryReadRaw(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return ("", false, true, null);
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                string text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                bool crlf = text.Contains("\r\n");
                text = Normalize(text);
                return (text, bom, crlf, Convert.ToHexString(SHA256.HashData(bytes)));
            }
            catch (IOException)
            {
                return null;   // 正在被写：watcher 随后还会再来一次
            }
        }

        private static void WriteRaw(string path, string text, bool bom, bool crlf)
        {
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string normalized = Normalize(text);
            string output = crlf ? normalized.Replace("\n", "\r\n") : normalized;
            string tmp = path + ".tcf-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, output, new UTF8Encoding(bom));
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        private static bool SameHash(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    }
}
