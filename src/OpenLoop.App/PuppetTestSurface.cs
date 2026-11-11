using System.Collections;

namespace OpenLoop.App
{
    /// <summary>
    /// 「Puppet 测试面」标记：整体移除测试入口时按它删（本文件 + MainForm 的 Test 字段/方法）。
    /// Puppet.Core 的同名标记只允许成员级，类级标记用这个本地定义。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PuppetTestAttribute : Attribute { }

    /// <summary>
    /// 临时测试面：AntdUI 这类自绘控件不符合标准 WinForms 控件模型（Puppet 的 /agent/control 点不到），
    /// 统一在这里用控件自己的公开 API 暴露通用动作，经 <c>MainForm.Test(verb, arg1, arg2)</c> 调用：
    /// <c>tree / click / clicknowait / settext / select / menu / key / wheel / close</c>。
    /// 以后遇到同类不兼容控件，也在这里加动词，不要再往 MainForm 堆测试方法。
    /// 动作都在 UI 线程执行；找不到控件会抛错，先用 <c>tree</c> 看名字。
    /// 开模态窗口的按钮：用 clicknowait 排队后立刻返回，再用 close 关掉；直接 click 会一直阻塞到窗口关闭。
    /// </summary>
    [PuppetTest]
    public sealed class TestSurface
    {
        private const uint WmKeyDown = 0x0100;
        private const uint WmKeyUp = 0x0101;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private readonly Control _ui;

        public TestSurface(Control ui) => _ui = ui;

        /// <summary>动作分派：tree / click / clicknowait / settext / select / menu / cell / key / wheel / close / prop / call。</summary>
        public object Run(string verb, string arg1 = "", string arg2 = "") => (verb ?? "").Trim().ToLowerInvariant() switch
        {
            "tree" => Tree(arg1),
            "click" => Click(arg1),
            "clicknowait" => ClickNoWait(arg1),
            "settext" => SetText(arg1, arg2),
            "select" => Select(arg1, arg2),
            "menu" => Menu(arg1, arg2),
            "cell" => Cell(arg1, arg2),
            "key" => Key(arg1, arg2),
            "wheel" => Wheel(arg1, arg2),
            "close" => Close(arg1),
            "prop" => Prop(arg1, arg2),
            "call" => Call(arg1, arg2),
            var other => throw new InvalidOperationException(
                $"未知动作 '{other}'；可用：tree/click/clicknowait/settext/select/menu/cell/key/wheel/close/prop/call")
        };

        /// <summary>列出打开窗体里的控件：路径 | 类型 | 名称 | 文本 | 位置 | 可见/可用。filter 匹配整行。</summary>
        public string[] Tree(string filter = "") => OnUi(() =>
        {
            string q = (filter ?? "").Trim();
            var lines = new List<string>();
            foreach (var form in OpenForms())
                foreach (var c in Walk(form))
                {
                    string line = $"{PathOf(form, c)} | {c.GetType().Name} | {c.Name} | {SafeText(c)}"
                        + $" | {c.Left},{c.Top} {c.Width}x{c.Height} | {(c.Visible ? "vis" : "hid")}/{(c.Enabled ? "on" : "off")}";
                    if (q.Length == 0 || line.Contains(q, StringComparison.OrdinalIgnoreCase)) lines.Add(line);
                }
            return lines.ToArray();
        });

        /// <summary>点按钮 / 勾选框 / 下拉框（按控件 Name，例如 btnNewSession、selEffort、btnModelProps）。</summary>
        public string Click(string name) => OnUi(() => ClickControl(Find(name)));

        /// <summary>排队点一下并立刻返回（不等点击代码执行完）：专门用于会开模态窗口的按钮，随后用 close 关窗口。</summary>
        public string ClickNoWait(string name) => OnUi(() =>
        {
            var c = Find(name);
            _ui.BeginInvoke(new Action(() =>
            {
                try { ClickControl(c); }
                catch (Exception ex) { AppLog.Warn($"TestSurface 异步点击 {c.Name} 失败：{ex.Message}"); }
            }));
            return "queued";
        });

        /// <summary>给控件发一个命名键（Enter / Escape / Tab / Up / Down…）：PostMessage 到它自己的句柄，不抢全局焦点。</summary>
        public string Key(string name, string key) => OnUi(() =>
        {
            var c = Find(name);
            if (!c.IsHandleCreated) throw new InvalidOperationException($"'{name}' 还没有窗口句柄");
            if (!Enum.TryParse<Keys>(key, true, out var keys))
                throw new InvalidOperationException($"认不出按键 '{key}'；用 Keys 名称，如 Enter / Escape / Tab / Up / Down");
            PostMessage(c.Handle, WmKeyDown, (IntPtr)(int)keys, IntPtr.Zero);
            PostMessage(c.Handle, WmKeyUp, (IntPtr)(int)keys, IntPtr.Zero);
            return keys.ToString();
        });

        /// <summary>在控件上滚轮一档（负=向下，正=向上），走控件自己的 OnMouseWheel。</summary>
        public string Wheel(string name, string delta) => OnUi(() =>
        {
            var c = Find(name);
            if (!int.TryParse(delta, out int value))
                throw new InvalidOperationException($"滚动量 '{delta}' 不是整数（负=向下，正=向上）");
            var onWheel = c.GetType().GetMethod("OnMouseWheel",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (onWheel == null) throw new InvalidOperationException($"{c.GetType().Name} '{name}' 没有滚轮处理");
            onWheel.Invoke(c, [new MouseEventArgs(MouseButtons.None, 0, c.Width / 2, c.Height / 2, value)]);
            return value.ToString();
        });

        /// <summary>关掉最上面的非主窗窗体（模态的设置/属性/确认框）；title 为空关最新一个，非空时按标题包含匹配。</summary>
        public string Close(string title = "") => OnUi(() =>
        {
            Form target = null;
            foreach (Form form in Application.OpenForms)
                if (!form.IsDisposed && !ReferenceEquals(form, _ui) && form.Visible
                    && (string.IsNullOrEmpty(title) || (form.Text ?? "").Contains(title, StringComparison.OrdinalIgnoreCase)))
                    target = form;
            if (target == null) return "no-form";
            string text = target.Text ?? "";
            target.Close();
            return text;
        });

        /// <summary>读控件上的公开属性，路径用点号（如 AtEnd、ClientSize.Width），只读不写。</summary>
        public string Prop(string name, string path) => OnUi(() =>
        {
            object current = Find(name);
            foreach (string part in (path ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var property = current.GetType().GetProperty(part,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (property == null)
                    throw new InvalidOperationException($"{current.GetType().Name} 上没有公开属性 '{part}'");
                current = property.GetValue(current);
                if (current == null) return "null";
            }
            return current.ToString() ?? "";
        });

        /// <summary>调控件上的公开无参方法（如 ScrollToEnd），返回方法结果或 ok。</summary>
        public string Call(string name, string method) => OnUi(() =>
        {
            var c = Find(name);
            var info = c.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null, Type.EmptyTypes, null);
            if (info == null)
                throw new InvalidOperationException($"{c.GetType().Name} 上没有公开无参方法 '{method}'");
            var result = info.Invoke(c, null);
            return result?.ToString() ?? "ok";
        });

        private static string ClickControl(Control c)
        {
            switch (c)
            {
                case AntdUI.Button button:   // 含 AntdUI.Dropdown：点一下就把菜单弹出来
                    button.PerformClick();
                    return "ok";
                case AntdUI.Select select:   // 点下拉框 = 展开/收起列表
                    select.Expand = !select.Expand;
                    return select.Expand ? "expanded" : "collapsed";
                case AntdUI.Checkbox check:
                    check.Checked = !check.Checked;
                    return check.Checked ? "checked" : "unchecked";
                default:
                    return RaiseClick(c);
            }
        }

        /// <summary>自绘控件没有公共 PerformClick（Label / 面板 / 菜单控件）时，走它受保护的 OnClick，等价于一次单击。</summary>
        private static string RaiseClick(Control control)
        {
            var onClick = control.GetType().GetMethod("OnClick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (onClick == null)
                throw new InvalidOperationException($"{control.GetType().Name} '{control.Name}' 没有 Click 动作；用 tree 看它的类型");
            onClick.Invoke(control, [EventArgs.Empty]);
            return "ok";
        }

        /// <summary>给输入框赋值（AntdUI.Input：Clear + EnterText，会正常触发 TextChanged）。</summary>
        public string SetText(string name, string text) => OnUi(() =>
        {
            var c = Find(name);
            if (c is AntdUI.Select)
                throw new InvalidOperationException($"'{name}' 是下拉框，用 select 选值、click 展开/收起");
            if (c is not AntdUI.Input input)
                throw new InvalidOperationException($"{c.GetType().Name} '{name}' 不是输入框");
            input.Clear();
            if (!string.IsNullOrEmpty(text)) input.EnterText(text);
            return input.Text ?? "";
        });

        /// <summary>点表格的一个单元格：arg2 为「行,列」，行 0 是表头。会走控件自己的鼠标处理（含复选列勾选）。</summary>
        public string Cell(string name, string arg) => OnUi(() =>
        {
            var c = Find(name);
            if (c is not AntdUI.Table table)
                throw new InvalidOperationException($"{c.GetType().Name} '{name}' 不是表格");
            var parts = (arg ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out int row) || !int.TryParse(parts[1], out int col))
                throw new InvalidOperationException($"'{arg}' 不是「行,列」");
            var rect = table.CellRectangle(row, col);
            if (rect.Width <= 0 || rect.Height <= 0)
                throw new InvalidOperationException($"表格没有单元格 {row},{col}");
            var point = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            var down = typeof(Control).GetMethod("OnMouseDown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var up = typeof(Control).GetMethod("OnMouseUp",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (down == null || up == null) throw new InvalidOperationException("没有鼠标处理");
            down.Invoke(table, [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);
            up.Invoke(table, [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);
            return $"{point.X},{point.Y}";
        });

        /// <summary>选下拉项（AntdUI.Select，按项文本或 Tag 匹配，会触发 SelectedIndexChanged）。</summary>
        public string Select(string name, string item) => OnUi(() =>
        {
            var c = Find(name);
            if (c is not AntdUI.Select select)
                throw new InvalidOperationException($"{c.GetType().Name} '{name}' 不是下拉框");
            int index = ItemIndex(select.Items, item);
            if (index < 0) throw new InvalidOperationException($"'{name}' 里没有 '{item}'；用 Tree 看 Items 候选");
            select.SelectedIndex = index;
            return (select.Text ?? "").Trim();
        });

        /// <summary>选中菜单项（AntdUI.Menu，按项文本匹配顶层项，会触发 SelectChanged）。</summary>
        public string Menu(string name, string item) => OnUi(() =>
        {
            var c = Find(name);
            if (c is not AntdUI.Menu menu)
                throw new InvalidOperationException($"{c.GetType().Name} '{name}' 不是菜单");
            foreach (AntdUI.MenuItem mi in menu.Items)
            {
                if (!string.Equals(mi.Text, item, StringComparison.OrdinalIgnoreCase)) continue;
                menu.Select(mi, true);
                return mi.Text ?? "";
            }
            throw new InvalidOperationException($"'{name}' 里没有菜单项 '{item}'");
        });

        private T OnUi<T>(Func<T> action)
            => !_ui.IsHandleCreated || _ui.IsDisposed
                ? action()
                : _ui.InvokeRequired ? (T)_ui.Invoke(action) : action();

        private static IEnumerable<Form> OpenForms()
        {
            foreach (Form form in Application.OpenForms)
                if (!form.IsDisposed) yield return form;
        }

        private static IEnumerable<Control> Walk(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (var c in Walk(child)) yield return c;
        }

        private static string PathOf(Form form, Control control)
        {
            var names = new List<string>();
            for (Control c = control; c != null && c != form; c = c.Parent)
                names.Insert(0, string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name);
            return (form.Name.Length > 0 ? form.Name : form.GetType().Name) + ">" + string.Join(">", names);
        }

        private static string SafeText(Control c)
        {
            try { return (c.Text ?? "").Replace("\r", " ").Replace("\n", " "); }
            catch { return ""; }
        }

        private static Control Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("控件名不能为空");
            Control byText = null;
            Control byTextHidden = null;
            Control byType = null;
            Control byTypeHidden = null;
            foreach (var form in OpenForms())
                foreach (var c in Walk(form))
                {
                    if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;   // 先按 Name
                    if (string.Equals((c.Text ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        // 再按文本；池化复用的隐藏行会有同样文本，优先取可见的那个
                        if (c.Visible) byText ??= c;
                        else byTextHidden ??= c;
                    }
                    if (string.Equals(c.GetType().Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (c.Visible) byType ??= c;
                        else byTypeHidden ??= c;
                    }
                }
            return byText ?? byTextHidden ?? byType ?? byTypeHidden
                ?? throw new InvalidOperationException($"找不到控件 '{name}'（Name / 文本 / 类型）；先用 tree 看");
        }

        private static int ItemIndex(IList items, string item)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is not AntdUI.SelectItem si) continue;
                if (string.Equals(si.Text, item, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(si.Tag?.ToString(), item, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }
}
