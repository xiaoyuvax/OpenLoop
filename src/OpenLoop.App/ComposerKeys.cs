namespace OpenLoop.App
{
    /// <summary>
    /// Enter 在 AntdUI 的 WM_KEYDOWN 里先被消化成换行（早于 KeyDown 事件），
    /// 所以 Shift+Enter 的软换行只能由 VerifyKeyboard 拦下来自己插。
    /// </summary>
    internal static class ComposerKeys
    {
        internal static void InsertSoftNewline(AntdUI.Input input)
        {
            if (input == null || !input.IsHandleCreated) return;
            int start = input.SelectionStart;
            int length = input.SelectionLength;
            string text = input.Text ?? "";
            input.Text = text.Substring(0, start) + Environment.NewLine + text.Substring(start + length);
            input.SelectionStart = start + Environment.NewLine.Length;
            input.SelectionLength = 0;
            input.Focus();
        }
    }
}
