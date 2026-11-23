using System.Text;
using ChatFlow;
using OpenLoop.Core.Store;
using OpenLoop.Service.Web;

namespace OpenLoop.App
{
    /// <summary>
    /// [Puppet] 页打开的会话视图：内容来自 @A / @I 的留言板（只读合并，外加用户以 U 身份参与）。
    /// 这些会话没有主 agent 驱动、也没有模型选择；谁在板上说话就显示谁，工具只有 /complain 与 /debug。
    /// </summary>
    public partial class PuppetView : UserControl
    {
        private static readonly (string Command, string Description)[] Commands =
        {
            ("/complain", "把 Puppet 交谈中的问题发给 Loop（不附上下文）"),
            ("/debug", "把问题连同最近几条留言发给 Loop")
        };

        private readonly ChatTurnBook _book = new();
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
        private string _host = "@A";
        private string _agent = "";
        private string _signature = "";
        private bool _loading;
        private bool _closed;
        /// <summary>本次启动后活跃过：停下后图标保持特征色，与启动前的历史（灰色）区分。</summary>
        private bool _wasLive;

        public PuppetView()
        {
            InitializeComponent();
        }

        /// <summary>用户在当前会话里用 /complain、/debug 报告问题；投递内容由本视图按命令拼好。</summary>
        internal event Action<string> ComplaintRequested;

        /// <summary>/debug 附带几条留言：由宿主按账本 limit.debug_turns 解析。</summary>
        internal Func<int> FeedbackTurns { get; set; } = () => FeatureCatalog.DefaultDebugTurns;

        internal string Host => _host;
        internal string Agent => _agent;

        /// <summary>留言板会话是否还在活跃（与 [Puppet] 列表的 Live 同源）。</summary>
        internal SessionState State { get; private set; } = SessionState.Idle;
        internal event Action<PuppetView> StateChanged;

        internal void Initialize(string host, string agentId)
        {
            _host = string.IsNullOrWhiteSpace(host) ? "@A" : host;
            _agent = agentId ?? "";
            _lblTitle.Text = "Puppet · " + _host + (string.IsNullOrWhiteSpace(_agent) ? "" : " · " + _agent);
            _chat.PlaceholderText = "Puppet 留言板：各会话的 agent 在这里自行对话；你也可以输入参与。";
            _chat.Bind(_book);
            _cmdPopup.Accepted += OnCommandAccepted;
            _txtInput.TextChanged += (_, _) => UpdateCommandPopup();
            _txtInput.KeyDown += OnInputKeyDown;
            _btnSend.Click += (_, _) => Submit();
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
            Refresh();
        }

        public void Shutdown()
        {
            _closed = true;
            _timer.Stop();
            _timer.Dispose();
        }

        /// <summary>标签页被选中后再拉一次：Initialize 时页面还不可见，定时器要等 2 秒。</summary>
        internal void Reload() => Refresh();

        private void Refresh()
        {
            if (_closed || _loading || !Visible) return;
            _loading = true;
            string host = _host;
            string agent = _agent;
            _ = Task.Run(() =>
            {
                var messages = PuppetBoard.Chat(host, agent);
                string hosts = PuppetBoard.HostsText();
                bool live = PuppetBoard.Sessions()
                    .Any(s => s.Live && string.Equals(s.Host, host, StringComparison.Ordinal));
                RunOnUi(() =>
                {
                    _loading = false;
                    if (_closed || IsDisposed) return;
                    Apply(messages, hosts);
                    if (live) _wasLive = true;
                    SetState(live ? SessionState.Running
                        : _wasLive ? SessionState.Done : SessionState.Idle);
                });
            });
        }

        private void Apply(IReadOnlyList<PuppetMessage> messages, string hosts)
        {
            _lblStatus.Text = (hosts.Length == 0 ? "没有可用的留言板" : "在线 " + hosts) + $" · {messages.Count} 条留言";
            string signature = string.Join(",", messages.Select(m => m.Host + "#" + m.Seq + "#" + m.At.Ticks));
            if (signature == _signature) return;
            bool first = _signature.Length == 0;
            _signature = signature;
            _book.ReplaceAll(messages.Select(ToTurn), null);
            if (first || messages.Count > 0) _chat.ScrollToEnd();
        }

        private void SetState(SessionState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(this);
        }

        private static ChatTurn ToTurn(PuppetMessage message)
        {
            if (string.Equals(message.AgentId, PuppetBoard.UserId, StringComparison.Ordinal))
                return new ChatTurn { User = new ChatMessage("user", message.Text ?? "") };

            string head = "**" + (message.AgentId ?? "") + "**"
                + (string.IsNullOrEmpty(message.To) ? "（广播）" : " → " + message.To);
            return new ChatTurn
            {
                Assistant = new ChatMessage("assistant", head + "\n\n" + (message.Text ?? ""))
            };
        }

        private void Submit()
        {
            string text = _txtInput.Text?.Trim() ?? "";
            if (text.Length == 0) return;
            _txtInput.Clear();

            if (text.StartsWith("/complain", StringComparison.OrdinalIgnoreCase)
                && (text.Length == 9 || char.IsWhiteSpace(text[9])))
            {
                string complaint = text[9..].Trim();
                if (complaint.Length == 0)
                {
                    WarnMissing("/complain 后面要写具体问题。");
                    return;
                }
                ComplaintRequested?.Invoke(ComplaintText(complaint, debug: false));
                return;
            }

            if (text.StartsWith("/debug", StringComparison.OrdinalIgnoreCase)
                && (text.Length == 6 || char.IsWhiteSpace(text[6])))
            {
                string complaint = text[6..].Trim();
                if (complaint.Length == 0)
                {
                    WarnMissing("/debug 后面要写具体问题。");
                    return;
                }
                ComplaintRequested?.Invoke(ComplaintText(complaint, debug: true));
                return;
            }

            PuppetBoard.Say(_host, text, _agent);
            _signature = "";
            Refresh();
        }

        private void WarnMissing(string message)
        {
            if (FindForm() is { } form)
                AntdUI.Notification.warn(form, "缺少内容", message, AntdUI.TAlignFrom.TR, null, 3);
        }

        /// <summary>/complain 只带一句与读板入口，/debug 另附账本配置的最近几条留言。</summary>
        private string ComplaintText(string complaint, bool debug)
        {
            var sb = new StringBuilder();
            sb.Append("[用户反馈] 对 Puppet 会话 ").Append(_host).Append(' ').Append(_agent)
              .Append(" 中的问题：").AppendLine(complaint);
            if (debug)
            {
                foreach (var turn in _book.Turns.TakeLast(Math.Max(0, FeedbackTurns())))
                {
                    if (!string.IsNullOrWhiteSpace(turn.UserText))
                        sb.Append("U: ").AppendLine(Clip(turn.UserText, 300));
                    if (!string.IsNullOrWhiteSpace(turn.AssistantText))
                        sb.AppendLine(Clip(turn.AssistantText, 300));
                }
            }
            sb.Append("需要上下文时用 Puppet 工具读留言板（action=\"chat\"）。");
            return sb.ToString().TrimEnd();
        }

        private static string Clip(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";

        private void UpdateCommandPopup()
        {
            string text = _txtInput.Text ?? "";
            if (!text.StartsWith('/') || text.Contains(' '))
            {
                _cmdPopup.Close();
                return;
            }

            var matches = Commands
                .Where(c => c.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
            {
                _cmdPopup.Close();
                return;
            }
            _cmdPopup.Open(matches);
            _cmdPopup.PlaceAbove(_inputArea);
            _cmdPopup.BringToFront();
        }

        private void OnCommandAccepted(string command)
        {
            _txtInput.Text = command + " ";
            _txtInput.SelectionStart = _txtInput.Text.Length;
            _txtInput.Focus();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (_cmdPopup.IsOpen)
            {
                if (e.KeyCode == Keys.Down) { _cmdPopup.MoveSelection(1); e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Up) { _cmdPopup.MoveSelection(-1); e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Escape) { _cmdPopup.Close(); e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Enter) { _cmdPopup.Accept(); e.SuppressKeyPress = true; return; }
            }

            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (e.Shift) ComposerKeys.InsertSoftNewline(_txtInput);
            else Submit();
        }

        private void RunOnUi(Action action)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(action); }
                catch (InvalidOperationException) { }
            }
            else action();
        }
    }
}
