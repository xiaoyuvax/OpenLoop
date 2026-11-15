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
            // SelectionStart 是字符簇索引（"\r\n" 算一个），不能拿去 Substring；
            // EnterText 自己按下标插入并挪光标。
            input.EnterText(Environment.NewLine);
        }
    }
}
