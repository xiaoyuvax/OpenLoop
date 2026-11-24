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
        /// <summary>prompt | agents | …（以后：svg / gltf / web）。</summary>
        internal string Kind { get; init; }
        internal string Title { get; init; }
        internal string Path { get; init; }

        /// <summary>「实际」形态（只读），没有就隐藏那一页。</summary>
        internal Func<string> Actual { get; init; }

        internal static TargetSpec Prompt(string workspace)
        {
            // 打开编辑器 = 显式写入口：没有模板文件时把内置默认落盘，agent 从这里改起
            PromptCatalog.EnsureFile(workspace);
            return new()
            {
                Kind = "prompt",
                Title = "系统提示词模板",
                Path = PromptCatalog.FilePath(workspace),
                Actual = () => NewestTracePrompt(workspace)
            };
        }

        internal static TargetSpec Agents(string workspace) => new()
        {
            Kind = "agents",
            Title = "AGENTS.md",
            Path = PromptCatalog.AgentsPath(workspace)
        };

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
            Watch();
            Reload();
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
            form.FormClosed += (_, _) =>
            {
                _undocked = null;
                if (IsDisposed) return;
                Controls.Add(_text);
                Controls.SetChildIndex(_text, 0);
            };
            _undocked = form;
            form.Show();
        }
    }
}
