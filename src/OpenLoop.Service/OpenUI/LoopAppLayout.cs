using System.Text.Json;
using OpenLoop.Core.Store;

namespace OpenLoop.Service.OpenUI
{
    /// <summary>页面自己声明的窗口意图（app.json 里的 <c>window</c> / <c>size</c> / <c>title</c>）。</summary>
    public sealed record LoopAppWindowHint(string Mode, int Width, int Height, string Title);

    /// <summary>页面内容度量：壳在文档加载完后用 DOM 量出来，决定竖版右侧栏够不够。</summary>
    public sealed record LoopAppContent(int Elements, int Columns, int WideBlocks, int Height, int OverflowX);

    /// <summary>
    /// LoopApp 的呈现方式与闲置回收：
    /// 右侧栏是竖版窄栏，只适合小表单/单列列表；内容宽或密的页面塞不下，直接走独立窗口。
    /// 判定是纯函数：声明（app.json）优先，其次看 DOM 实测；阈值改这里，壳只负责量。
    /// </summary>
    public static class LoopAppLayout
    {
        /// <summary>元素数量到这个量级，竖版一屏读不完。</summary>
        private const int DenseElements = 60;

        /// <summary>表格/栅格到这个列数，侧栏宽度下必然横向挤压。</summary>
        private const int WideColumns = 6;

        /// <summary>声明宽度到这个值就按宽版处理（侧栏最窄 360 DIP，通常不超过 600）。</summary>
        private const int WideWidth = 720;

        /// <summary>正文高度超过这个值，说明是一整页数据而不是一个小对话框。</summary>
        private const int LongPage = 1400;

        /// <summary>横向溢出的容差（像素）：滚动条本身会带来一点差。</summary>
        private const int OverflowTolerance = 8;

        /// <summary>壳在文档加载完后跑这段脚本，把内容量交回来（返回 JSON 字符串）。</summary>
        public const string MeasureScript =
            "(function(){" +
            "var all=document.querySelectorAll('*').length;" +
            "var cols=0;" +
            "document.querySelectorAll('table tr').forEach(function(r){if(r.children.length>cols)cols=r.children.length;});" +
            "var wide=document.querySelectorAll('.grid.c3,.grid.c4,table.wide,.wide').length;" +
            "var h=Math.max(document.body?document.body.scrollHeight:0,document.documentElement.scrollHeight);" +
            "var over=document.documentElement.scrollWidth-window.innerWidth;" +
            "return JSON.stringify({elements:all,columns:cols,wide:wide,height:h,overflowX:over});" +
            "})()";

        /// <summary>读 app.json 的窗口声明；文件不在/坏了都返回 null（当作没声明）。</summary>
        public static LoopAppWindowHint ReadHint(string appJsonPath)
        {
            if (string.IsNullOrWhiteSpace(appJsonPath) || !File.Exists(appJsonPath)) return null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(appJsonPath));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                string mode = root.TryGetProperty("window", out var w) && w.ValueKind == JsonValueKind.String
                    ? (w.GetString() ?? "").Trim().ToLowerInvariant()
                    : "";
                string title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                    ? (t.GetString() ?? "").Trim()
                    : "";
                int width = 0, height = 0;
                if (root.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Array)
                {
                    var items = size.EnumerateArray().ToList();
                    if (items.Count > 0 && items[0].ValueKind == JsonValueKind.Number) width = items[0].GetInt32();
                    if (items.Count > 1 && items[1].ValueKind == JsonValueKind.Number) height = items[1].GetInt32();
                }

                if (mode.Length == 0 && width <= 0 && title.Length == 0) return null;
                return new LoopAppWindowHint(mode, width, height, title);
            }
            catch (Exception)
            {
                // app.json 坏了：按没声明处理，照样按 DOM 判定
                return null;
            }
        }

        /// <summary>
        /// 要不要走独立窗口。声明优先（显式写 float/dock 就照办），
        /// 否则看内容：宽块、列数、元素数、页长、横向溢出任一条成立就给独立窗口。
        /// </summary>
        public static bool PreferFloat(LoopAppWindowHint hint, LoopAppContent content)
        {
            if (hint != null)
            {
                if (string.Equals(hint.Mode, "float", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(hint.Mode, "dock", StringComparison.OrdinalIgnoreCase)) return false;
                if (hint.Width >= WideWidth) return true;
            }

            if (content == null) return false;
            return content.WideBlocks > 0
                   || content.Columns >= WideColumns
                   || content.Elements >= DenseElements
                   || content.Height >= LongPage
                   || content.OverflowX > OverflowTolerance;
        }

        /// <summary>解析 <c>MeasureScript</c> 的返回值（WebView2 的 JSON 结果串，字符串结果带引号）。</summary>
        public static LoopAppContent ParseContent(string raw)
        {
            string json = Unwrap(raw);
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                return new LoopAppContent(
                    Int(root, "elements"), Int(root, "columns"), Int(root, "wide"),
                    Int(root, "height"), Int(root, "overflowX"));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>从 LoopApps 落盘根（<c>&lt;workspace&gt;/.openloop/tmp/LoopApps</c>）反推工作区；对不上返回空串。</summary>
        public static string WorkspaceOf(string loopAppsRoot)
        {
            string root = (loopAppsRoot ?? "").TrimEnd('\\', '/');
            if (root.Length == 0) return "";
            string marker = Path.Combine(".openloop", "tmp", "LoopApps");
            if (!root.EndsWith(marker, StringComparison.OrdinalIgnoreCase)) return "";
            string workspace = root[..^marker.Length].TrimEnd('\\', '/');
            return workspace.Length == 0 ? "" : workspace;
        }

        /// <summary>壳收起来的实例闲置多久销毁 WebView2：账本 limit.browser_idle 覆盖默认值。</summary>
        public static int IdleMinutes(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return FeatureCatalog.DefaultBrowserIdleMinutes;
            try
            {
                var ledger = Ledger.Open(workspace);
                int value = FeatureCatalog.ValueOf(ledger.Current,
                    ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal),
                    FeatureCatalog.BrowserIdle, FeatureCatalog.DefaultBrowserIdleMinutes);
                return Math.Clamp(value, 1, 1440);
            }
            catch (Exception)
            {
                // 账本读不到就用默认值：这是回收策略，不该把界面带崩
                return FeatureCatalog.DefaultBrowserIdleMinutes;
            }
        }

        private static string Unwrap(string raw)
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return null;
            if (text[0] == '"')
            {
                try { return JsonSerializer.Deserialize<string>(text); }
                catch (JsonException) { return null; }
            }
            return text;
        }

        private static int Int(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out int number)
                ? number
                : 0;
    }
}
