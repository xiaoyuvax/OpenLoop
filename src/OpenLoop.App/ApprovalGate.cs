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

            //实测（250%）AntdUI Modal 直接拿内容的物理像素当弹框尺寸，不会再按 DPI 缩放，
            //所以这里一律用按 DPI 放大后的像素值，否则弹框会偏小、文字被裁掉。
            float scale = form.DeviceDpi / 96f;
            int deviceWidth = (int)(420 * scale);

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
                AutoSize = false,
                Margin = new Padding(0)
            };
            panel.Controls.Add(label);
            panel.Controls.Add(remember);
            panel.Controls.Add(rememberHint);

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
