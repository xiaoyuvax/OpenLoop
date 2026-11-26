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

        internal static void InsertSoftNewline(AntdUI.Input input)
        {
            if (input == null || !input.IsHandleCreated) return;
            // SelectionStart 是字符簇索引（"\r\n" 算一个），不能拿去 Substring；
            // EnterText 自己按下标插入并挪光标。
            input.EnterText(Environment.NewLine);
        }

        /// <summary>
        /// AntdUI 的 Up 到首行只做「向左一格」，而列号 tmpkeyx 还粘着上一行，
        /// 首行比它短时会在末尾两格之间来回跳、到不了行首；首行按 Home 收尾。
        /// 升级 AntdUI 后确认 CaretInfo / rect_text / tmpkeyx / ICaret.Rect 仍在（取不到就退回原行为）。
        /// </summary>
        internal static bool MoveToFirstLineStart(AntdUI.Input input)
        {
            if (input == null || !input.IsHandleCreated) return false;
            object caret = CaretField?.GetValue(input);
            if (caret == null) return false;
            if (TextRectField?.GetValue(input) is not Rectangle textRect) return false;
            if (CaretRectField?.GetValue(caret) is not Rectangle caretRect) return false;
            if (caretRect.Y > textRect.Y) return false;
            input.SelectionLength = 0;
            input.SelectionStart = 0;
            KeyXField?.SetValue(input, null);
            return true;
        }
    }
}
