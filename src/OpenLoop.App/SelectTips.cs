namespace OpenLoop.App
{
    /// <summary>
    /// 下拉框宽度不足以显示完整选项时，用 ToolTip 补全（Text + SubText）。
    /// 选中项变化或鼠标移入时刷新。
    /// </summary>
    internal static class SelectTips
    {
        public static void Bind(ToolTip tips, params AntdUI.Select[] selects)
        {
            if (tips == null) return;
            foreach (var select in selects)
            {
                if (select == null) continue;
                var target = select;
                target.SelectedValueChanged += (_, _) => Refresh(tips, target);
                target.SelectedIndexChanged += (_, _) => Refresh(tips, target);
                target.MouseEnter += (_, _) => Refresh(tips, target);
                Refresh(tips, target);
            }
        }

        private static void Refresh(ToolTip tips, AntdUI.Select select)
        {
            if (tips == null || select == null || select.IsDisposed) return;
            tips.SetToolTip(select, Describe(select));
        }

        private static string Describe(AntdUI.Select select)
        {
            int index = select.SelectedIndex;
            if (index < 0 || index >= select.Items.Count) return "";
            if (select.Items[index] is not AntdUI.SelectItem item) return "";

            string text = item.Text ?? "";
            string sub = item.SubText ?? "";
            if (sub.Length == 0) return text;
            return text.Length == 0 ? sub : text + Environment.NewLine + sub;
        }
    }
}
