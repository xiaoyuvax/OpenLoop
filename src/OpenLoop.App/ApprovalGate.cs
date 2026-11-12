using System.Drawing;
using OpenLoop.Core.Safety;

namespace OpenLoop.App
{
    /// <summary>
    /// 工具调用的审批分级入口：
    /// 工作区内、安全的操作根本不经过这里；这里只处理「工作区外访问」和「风险操作」两类。
    /// 自动审批打开、或该类此前选过「后续都许可」时，直接放行不再弹框。
    /// </summary>
    internal static class ApprovalGate
    {
        /// <summary>可跨线程调用：内部会转到 owner 的 UI 线程弹框。</summary>
        public static ApprovalDecision Ask(Control owner, Func<AppSettings> settings, ApprovalRequest request)
        {
            var current = settings();
            if (current.AutoApprove) return ApprovalDecision.AllowOnce;
            if (Remembered(current, request.Kind)) return ApprovalDecision.AllowOnce;

            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return ApprovalDecision.Deny;
            if (owner.InvokeRequired) return (ApprovalDecision)owner.Invoke(new Func<ApprovalDecision>(() => Ask(owner, settings, request)));
            return Prompt(owner, settings, request);
        }

        private static bool Remembered(AppSettings settings, ApprovalKind kind) => kind switch
        {
            ApprovalKind.OutsideWorkspace => settings.AllowOutsideWorkspace,
            ApprovalKind.SensitiveAction => settings.AllowSensitiveActions,
            _ => false
        };

        private static ApprovalDecision Prompt(Control owner, Func<AppSettings> settings, ApprovalRequest request)
        {
            var form = owner as Form ?? owner.FindForm();
            if (form == null) return ApprovalDecision.Deny;

            string title = TitleFor(request);
            string text = TextFor(request);

            //实测（250%）AntdUI Modal 直接拿内容的物理像素当弹框尺寸，不会再按 DPI 缩放，
            //所以这里一律用按 DPI 放大后的像素值，否则弹框会偏小、文字被裁掉。
            float scale = form.DeviceDpi / 96f;
            int deviceWidth = (int)(420 * scale);

            var (panel, remember) = BuildPanel(text, deviceWidth, autoSize: false);
            int deviceHeight = 0;
            foreach (Control child in panel.Controls)
                deviceHeight += child.PreferredSize.Height + child.Margin.Vertical;
            //弹框会在内容下方留出按钮行，尺寸没算进去就会把最后一行文字压掉
            panel.Size = new Size(deviceWidth, deviceHeight + (int)(44 * scale));

            var config = AntdUI.Modal.config(form, title, (object)panel);
            config.OkText = "允许";
            config.CancelText = "拒绝";
            bool ok = AntdUI.Modal.open(config) == DialogResult.OK;
            if (!ok) return ApprovalDecision.Deny;

            return remember.Checked ? AllowAlways(settings, request) : ApprovalDecision.AllowOnce;
        }

        /// <summary>非模态审批：不阻塞 UI 线程（也就不阻塞 Worker 的快照轮询），结果走回调。
        /// 一直没人点就挂着；Worker 侧 60 秒超时按拒绝处理，对话框随后自动关掉。</summary>
        public static void AskNonBlocking(Control owner, Func<AppSettings> settings, ApprovalRequest request,
            Action<ApprovalDecision> done)
        {
            var current = settings();
            if (current.AutoApprove || Remembered(current, request.Kind))
            {
                done(ApprovalDecision.AllowOnce);
                return;
            }
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated)
            {
                done(ApprovalDecision.Deny);
                return;
            }
            if (owner.InvokeRequired)
            {
                owner.BeginInvoke(new Action(() => AskNonBlocking(owner, settings, request, done)));
                return;
            }

            var app = owner as Form ?? owner.FindForm();
            float scale = (app?.DeviceDpi ?? owner.DeviceDpi) / 96f;
            int width = (int)(420 * scale);
            var (panel, remember) = BuildPanel(TextFor(request), width, autoSize: true);
            panel.Dock = DockStyle.Fill;

            var allow = new Button
            {
                Text = "允许",
                DialogResult = DialogResult.OK,
                Size = new Size((int)(92 * scale), (int)(32 * scale)),
                Margin = new Padding((int)(8 * scale), 0, 0, 0)
            };
            var deny = new Button
            {
                Text = "拒绝",
                DialogResult = DialogResult.Cancel,
                Size = new Size((int)(92 * scale), (int)(32 * scale))
            };
            var bottom = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                Height = (int)(52 * scale),
                Padding = new Padding(0, (int)(10 * scale), (int)(14 * scale), 0)
            };
            bottom.Controls.Add(allow);   // RightToLeft：先加的排最右
            bottom.Controls.Add(deny);

            var dialog = new Form
            {
                Text = TitleFor(request),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = app == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = app == null,
                TopMost = true,
                AutoScaleMode = AutoScaleMode.None,
                BackColor = UiStyle.Surface,
                ClientSize = new Size(panel.PreferredSize.Width + (int)(28 * scale),
                    panel.PreferredSize.Height + bottom.Height + (int)(20 * scale))
            };
            dialog.Controls.Add(panel);
            dialog.Controls.Add(bottom);
            dialog.AcceptButton = allow;
            dialog.CancelButton = deny;

            bool answered = false;
            void Finish(ApprovalDecision decision)
            {
                if (answered) return;
                answered = true;
                done(decision);
                if (!dialog.IsDisposed) dialog.Close();
            }
            allow.Click += (_, _) => Finish(remember.Checked ? AllowAlways(settings, request) : ApprovalDecision.AllowOnce);
            deny.Click += (_, _) => Finish(ApprovalDecision.Deny);
            // 关窗（X）等同拒绝
            dialog.FormClosed += (_, _) => { if (!answered) { answered = true; done(ApprovalDecision.Deny); } };

            // Worker 侧 60 秒超时会按拒绝收尾：这里同步关掉，别留一个点了也没用的框
            var timer = new System.Windows.Forms.Timer { Interval = 60_000 };
            timer.Tick += (_, _) => { timer.Stop(); Finish(ApprovalDecision.Deny); };
            dialog.FormClosed += (_, _) => timer.Dispose();

            dialog.Show(app);
            timer.Start();
        }

        private static string TitleFor(ApprovalRequest request)
            => request.Kind == ApprovalKind.OutsideWorkspace ? "工作区外访问" : Localization.T("ApproveTitle");

        private static string TextFor(ApprovalRequest request)
            => request.Kind == ApprovalKind.OutsideWorkspace
                ? $"{request.Text}\n\n工具：{request.Tool}\n允许这次访问吗？"
                : string.Format(Localization.T("ApproveCommand"), request.Reason, request.Detail);

        private static ApprovalDecision AllowAlways(Func<AppSettings> settings, ApprovalRequest request)
        {
            var target = settings();
            if (request.Kind == ApprovalKind.OutsideWorkspace) target.AllowOutsideWorkspace = true;
            else target.AllowSensitiveActions = true;
            try { target.Save(); }
            catch (Exception ex) { AppLog.Warn("保存审批设置失败：" + ex.Message); }
            return ApprovalDecision.AllowAlways;
        }

        private static (FlowLayoutPanel Panel, CheckBox Remember) BuildPanel(string text, int deviceWidth, bool autoSize)
        {
            var label = new System.Windows.Forms.Label
            {
                AutoSize = true,
                MaximumSize = new Size(deviceWidth, 0),
                Text = text,
                ForeColor = UiStyle.Text
            };
            //CheckBox 不换行，长文案会被弹框裁掉，所以勾选项只留短句，说明另起一行可换行的标签
            var remember = new System.Windows.Forms.CheckBox
            {
                AutoSize = true,
                Text = "后续同类操作都许可",
                ForeColor = UiStyle.Text,
                Margin = new Padding(0, 10, 0, 0)
            };
            var rememberHint = new System.Windows.Forms.Label
            {
                AutoSize = true,
                MaximumSize = new Size(deviceWidth, 0),
                Text = "（可在设置 →「普通」里关闭）",
                ForeColor = UiStyle.Muted,
                Margin = new Padding(2, 2, 0, 0)
            };
            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = autoSize,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            panel.Controls.Add(label);
            panel.Controls.Add(remember);
            panel.Controls.Add(rememberHint);
            return (panel, remember);
        }
    }
}
