using System.Drawing;
using System.Windows.Forms;

namespace OpenLoop.App
{
    /// <summary>
    /// 侧栏详情页「审批豁免」两行的统一尺寸与样式：一行一个说明 + 一个开关，开关右对齐。
    /// Worker（SessionView）与 Loop（LoopView）共用同一套数值，免得两侧各摆一套、说明与开关对不上号
    /// （此前是一行合成标签「工作区外写入 / 风险操作」加两个无标注开关，看不出哪个管什么）。
    /// 容器与 Controls.Add 仍写在各自 Designer 里（设计器兼容约定），这里只给属性与尺寸。
    /// </summary>
    internal static class PermissionRows
    {
        /// <summary>与侧栏按钮组同宽：FlowButtons.Center 按组内最宽者对齐左边，宽度一致才不会摆成锯齿。</summary>
        internal const int BoxWidth = 236;

        /// <summary>单行高度（说明与开关同一行）。</summary>
        internal const int RowHeight = 28;

        internal const int SwitchWidth = 44;
        internal const int SwitchHeight = 22;

        internal static void ConfigureLabel(AntdUI.Label label, string textKey)
        {
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.Text = Localization.T(textKey);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = new Font("Segoe UI", 9F);
            label.ForeColor = UiStyle.Muted;
            label.Margin = new Padding(0);
        }

        internal static void ConfigureSwitch(AntdUI.Switch toggle)
        {
            toggle.Width = SwitchWidth;
            toggle.Height = SwitchHeight;
            toggle.Margin = new Padding(0);
            toggle.Anchor = AnchorStyles.None;   // 单元格内居中，别被 100% 行高拉成整格
        }

        /// <summary>两行开关的外容器：按钮组下方独立一组，靠 FlowButtons.Center 跟按钮组一起居中。</summary>
        internal static void ConfigureBox(FlowLayoutPanel box)
        {
            box.FlowDirection = FlowDirection.TopDown;
            box.WrapContents = false;
            // 不用 AutoSize：FlowButtons.Center 按控件 Width 算居中，自适宽会让它跟按钮组对不齐
            box.AutoSize = false;
            box.Width = BoxWidth;
            box.Height = RowHeight * 2;
            // 与上方按钮组留一段距离：这是一组全局设置，不是又一颗按钮
            box.Margin = new Padding(0, 16, 0, 0);
        }

        /// <summary>单行容器：说明占满剩余宽度，开关固定在右侧 44 宽。</summary>
        internal static void ConfigureRow(TableLayoutPanel row)
        {
            row.ColumnCount = 2;
            row.RowCount = 1;
            row.Width = BoxWidth;
            row.Height = RowHeight;
            row.Margin = new Padding(0);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SwitchWidth));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        }
    }
}