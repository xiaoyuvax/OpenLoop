using System.Text;
using FastColoredTextBoxNS;
using OpenLoop.Core.Store;

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

        internal static TargetSpec Prompt(string workspace)
        {
            bool eval = HarnessMode.IsEval(workspace);
            return new()
            {
                Kind = PromptCatalog.SystemKind,
                Title = eval ? "系统提示词模板（候选）" : "系统提示词模板",
                Path = PromptCatalog.EditingPath(workspace, PromptCatalog.SystemKind),
                Actual = () => NewestTracePrompt(workspace),
                Candidate = eval
            };
        }

        internal static TargetSpec Agents(string workspace)
        {
            bool eval = HarnessMode.IsEval(workspace);
            return new()
            {
                Kind = PromptCatalog.AgentsKind,
                Title = eval ? "AGENTS.md（候选）" : "AGENTS.md",
                Path = PromptCatalog.EditingPath(workspace, PromptCatalog.AgentsKind),
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
    /// 会话左栏的目标面板：只读呈现目标文本，用户通过聊天让 agent 改，文件一变这里立刻刷新。
    /// 用户不能直接改文本（面板只读），改动只能经模型落盘——与 TargetCentricSession 的约定一致。
    /// </summary>
    internal sealed partial class TargetPane : UserControl
    {
        private TargetSpec _spec;
        private Action<string> _changed;
        private FileSystemWatcher _watcher;
        private System.Windows.Forms.Timer _debounce;
        private string _shown;
        private bool _actual;
        private Form _undocked;

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

        /// <summary>用户点「关闭」：宿主收起目标模式（面板不自己拆，由会话决定布局）。</summary>
        internal event Action CloseRequested;

        internal void Bind(TargetSpec spec, Action<string> changed = null)
        {
            Shutdown();
            // 已 undock 的正文控件在独立窗体里：换目标/收起前先关窗，让它回到面板
            if (_undocked != null && !_undocked.IsDisposed) _undocked.Close();
            _spec = spec;
            _changed = changed;
            _lblTitle.Text = spec?.Title ?? "";
            _segMode.Visible = spec?.Actual != null;
            _actual = false;
            _segMode.SelectIndex = 0;
            _shown = null;
            _banner.Visible = spec?.Candidate == true;
            if (_banner.Visible) ShowBanner();
            Watch();
            Reload();
        }

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

        /// <summary>重新读文件（watcher、以及每轮收尾都可以调）。</summary>
        internal void Reload()
        {
            if (_spec == null || IsDisposed) return;
            string text = _actual ? _spec.Actual?.Invoke() : SafeRead(_spec.Path);
            if (text == null && !_actual) text = "（还没有内容）";
            if (string.Equals(text, _shown, StringComparison.Ordinal)) return;
            _shown = text;
            int caret = _text.SelectionStart;
            _text.Text = text ?? "";
            _text.SelectionStart = Math.Min(caret, _text.Text.Length);
            if (_actual) return;
            _changed?.Invoke(_shown);
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

        private static string SafeRead(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                return File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
            }
            catch (IOException)
            {
                return null;   // 正在被写：watcher 随后还会再来一次
            }
        }

        private void OnModeChanged(object sender, AntdUI.IntEventArgs e)
        {
            _actual = (e?.Value ?? 0) == 1;
            _shown = null;
            Reload();
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
            Controls.Remove(_text);
            _text.Dock = DockStyle.Fill;
            form.Controls.Add(_text);
            // 提示条跟着正文走：独立窗口也是编辑面，不能少了「这是候选包」这句
            bool banner = _banner.Visible;
            if (banner)
            {
                Controls.Remove(_banner);
                form.Controls.Add(_banner);
            }
            form.FormClosed += (_, _) =>
            {
                _undocked = null;
                if (IsDisposed) return;
                if (banner) Controls.Add(_banner);
                Controls.Add(_text);
                Controls.SetChildIndex(_text, 0);
            };
            _undocked = form;
            form.Show();
        }
    }
}
