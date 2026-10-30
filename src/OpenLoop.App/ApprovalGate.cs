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

            string title = request.Kind == ApprovalKind.OutsideWorkspace ? "工作区外访问" : Localization.T("ApproveTitle");
            string text = request.Kind == ApprovalKind.OutsideWorkspace
                ? $"{request.Text}\n\n工具：{request.Tool}\n允许这次访问吗？"
                : string.Format(Localization.T("ApproveCommand"), request.Reason, request.Detail);

            var label = new System.Windows.Forms.Label
            {
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Text = text,
                ForeColor = UiStyle.Text
            };
            var remember = new System.Windows.Forms.CheckBox
            {
                AutoSize = true,
                Text = "后续同类操作都许可（可在设置里关闭）",
                ForeColor = UiStyle.Muted,
                Margin = new Padding(0, 10, 0, 0)
            };
            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            panel.Controls.Add(label);
            panel.Controls.Add(remember);

            var config = AntdUI.Modal.config(form, title, (object)panel);
            config.OkText = "允许";
            config.CancelText = "拒绝";
            bool ok = AntdUI.Modal.open(config) == DialogResult.OK;
            if (!ok) return ApprovalDecision.Deny;

            if (remember.Checked)
            {
                var target = settings();
                if (request.Kind == ApprovalKind.OutsideWorkspace) target.AllowOutsideWorkspace = true;
                else target.AllowSensitiveActions = true;
                try { target.Save(); }
                catch (Exception ex) { AppLog.Warn("保存审批设置失败：" + ex.Message); }
                return ApprovalDecision.AllowAlways;
            }
            return ApprovalDecision.AllowOnce;
        }
    }
}
