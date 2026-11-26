using System.Drawing;
using System.Reflection;

namespace OpenLoop.App
{
    /// <summary>
    /// Enter 在 AntdUI 的 WM_KEYDOWN 里先被消化成换行（早于 KeyDown 事件），
    /// 所以 Shift+Enter 的软换行只能由 VerifyKeyboard 拦下来自己插。
    /// </summary>
    internal static class ComposerKeys
    {
        private static readonly FieldInfo CaretField =
            typeof(AntdUI.Input).GetField("CaretInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TextRectField =
            typeof(AntdUI.Input).GetField("rect_text", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo KeyXField =
            typeof(AntdUI.Input).GetField("tmpkeyx", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CaretRectField = CaretField?.FieldType.GetField("Rect");
        private static readonly MethodInfo SetCaretMethod = typeof(AntdUI.Input).GetMethod(
            "SetCaretPostion", BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(int), typeof(bool) }, null);

        internal static void InsertSoftNewline(AntdUI.Input input)
        {
            if (input == null || !input.IsHandleCreated) return;
            // SelectionStart 是字符簇索引（"\r\n" 算一个），不能拿去 Substring；
            // EnterText 自己按下标插入并挪光标。
            input.EnterText(Environment.NewLine);
        }

        /// <summary>
        /// AntdUI 的 Up 到首行只做「向左一格」，列号 tmpkeyx 还粘着上一行，首行比它短时
        /// 会在末尾两格之间来回跳；首行为空时更糟：空行没有光标槽位，下标 0 的光标被画在
        /// 第二行，看上去永远没到首行。首行按 Home 收尾，空首行再把光标槽位挪回 0。
        /// 升级 AntdUI 后确认 CaretInfo / rect_text / tmpkeyx / ICaret.Rect / SetCaretPostion
        /// 仍在（取不到就退回原行为）。
        /// </summary>
        internal static bool MoveToFirstLineStart(AntdUI.Input input)
        {
            if (input == null || !input.IsHandleCreated) return false;
            object caret = CaretField?.GetValue(input);
            if (caret == null) return false;
            if (TextRectField?.GetValue(input) is not Rectangle textRect) return false;
            if (CaretRectField?.GetValue(caret) is not Rectangle caretRect) return false;
            bool emptyFirst = StartsWithLineBreak(input.Text);
            bool onFirstLine = caretRect.Y <= textRect.Y;
            // 空首行没有自己的光标槽位，第二行上的光标仍属于「上一行是首行」
            bool belowFirstLine = emptyFirst && caretRect.Y <= textRect.Y + caretRect.Height + 2;
            if (!onFirstLine && !belowFirstLine) return false;
            input.SelectionLength = 0;
            input.SelectionStart = 0;
            KeyXField?.SetValue(input, null);
            if (emptyFirst) SetCaretMethod?.Invoke(input, new object[] { 0, false });
            return true;
        }

        private static bool StartsWithLineBreak(string text)
            => !string.IsNullOrEmpty(text) && (text[0] == '\r' || text[0] == '\n');
    }
}
