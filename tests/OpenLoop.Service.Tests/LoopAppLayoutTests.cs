using OpenLoop.Service.OpenUI;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// LoopApp 停靠/独立窗口的判定与闲置时长解析：
    /// 竖版右侧栏只适合小表单；内容宽或密的页面要直接给独立窗口，否则挤成一条缝。
    /// 判定是纯函数（<see cref="LoopAppLayout"/>），壳只负责量 DOM。
    /// </summary>
    public sealed class LoopAppLayoutTests
    {
        [Fact]
        public void Small_form_stays_in_the_side_panel()
        {
            var content = new LoopAppContent(Elements: 40, Columns: 2, WideBlocks: 0, Height: 620, OverflowX: 0);
            Assert.False(LoopAppLayout.PreferFloat(null, content));
        }

        [Theory]
        [InlineData(80, 2, 0, 620, 0)]      // 元素太多：一屏读不完
        [InlineData(40, 7, 0, 620, 0)]      // 表格列太多：侧栏放不下
        [InlineData(40, 2, 1, 620, 0)]      // 有宽块（grid.c3/c4、table.wide）
        [InlineData(40, 2, 0, 2000, 0)]     // 一整页数据，不是小对话框
        [InlineData(40, 2, 0, 620, 120)]    // 横向溢出：已经挤出去了
        public void Wide_or_dense_pages_go_to_a_window(int elements, int columns, int wide, int height, int overflow)
        {
            var content = new LoopAppContent(elements, columns, wide, height, overflow);
            Assert.True(LoopAppLayout.PreferFloat(null, content));
        }

        [Fact]
        public void Declaration_wins_over_the_measurement()
        {
            var small = new LoopAppContent(10, 1, 0, 300, 0);
            // agent 显式写了 float：内容再小也照办
            Assert.True(LoopAppLayout.PreferFloat(new LoopAppWindowHint("float", 0, 0, ""), small));
            // 显式 dock：内容再宽也留在侧栏（这是覆盖自动判定的唯一入口）
            var wide = new LoopAppContent(200, 9, 3, 3000, 400);
            Assert.False(LoopAppLayout.PreferFloat(new LoopAppWindowHint("dock", 0, 0, ""), wide));
            // 没写 window 但声明了宽度：按宽度判定
            Assert.True(LoopAppLayout.PreferFloat(new LoopAppWindowHint("", 960, 0, ""), small));
            Assert.False(LoopAppLayout.PreferFloat(new LoopAppWindowHint("", 420, 0, ""), small));
        }

        [Fact]
        public void Without_a_measurement_it_keeps_the_side_panel()
        {
            // 量不出来（页面还没渲染）时不要贸然弹窗：留在侧栏，下一次导航再说
            Assert.False(LoopAppLayout.PreferFloat(null, null));
        }

        [Fact]
        public void Measure_output_is_parsed_from_the_webview_json_string()
        {
            // WebView2 的 ExecuteScriptAsync 对字符串结果会再加一层引号与转义
            string raw = "\"{\\\"elements\\\":72,\\\"columns\\\":8,\\\"wide\\\":1,\\\"height\\\":1800,\\\"overflowX\\\":64}\"";
            var content = LoopAppLayout.ParseContent(raw);

            Assert.NotNull(content);
            Assert.Equal(72, content.Elements);
            Assert.Equal(8, content.Columns);
            Assert.Equal(1, content.WideBlocks);
            Assert.Equal(1800, content.Height);
            Assert.Equal(64, content.OverflowX);
            Assert.True(LoopAppLayout.PreferFloat(null, content));

            // 页面还没画出来 / 脚本失败：当成量不到，不是「内容为 0 所以停靠」
            Assert.Null(LoopAppLayout.ParseContent("null"));
            Assert.Null(LoopAppLayout.ParseContent(""));
            Assert.Null(LoopAppLayout.ParseContent("undefined"));
        }

        [Fact]
        public void Hint_is_read_from_app_json_and_a_broken_file_counts_as_none()
        {
            string dir = Path.Combine(Path.GetTempPath(), "openui-hint-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "app.json");
                File.WriteAllText(file, "{\"title\":\"宽看板\",\"window\":\"float\",\"size\":[1200,820]}");
                var hint = LoopAppLayout.ReadHint(file);
                Assert.Equal("float", hint.Mode);
                Assert.Equal(1200, hint.Width);
                Assert.Equal(820, hint.Height);
                Assert.Equal("宽看板", hint.Title);

                File.WriteAllText(file, "{ 坏 json");
                Assert.Null(LoopAppLayout.ReadHint(file));
                Assert.Null(LoopAppLayout.ReadHint(Path.Combine(dir, "missing.json")));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void Workspace_is_recovered_from_the_loopapps_root()
        {
            string workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "openui-ws"));
            string root = LoopAppPaths.Root(workspace);
            Assert.Equal(workspace, LoopAppLayout.WorkspaceOf(root));

            // 不是 LoopApps 目录就回空串，别把别的路径当工作区去读账本
            Assert.Equal("", LoopAppLayout.WorkspaceOf(""));
            Assert.Equal("", LoopAppLayout.WorkspaceOf(Path.Combine(workspace, "other")));
        }

        [Fact]
        public void Idle_minutes_come_from_the_workspace_ledger()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "openui-idle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            try
            {
                // 账本还没建：默认 30 分钟
                Assert.Equal(30, LoopAppLayout.IdleMinutes(workspace));

                var ledger = OpenLoop.Core.Store.Ledger.Open(workspace);
                ledger.EnsureInitialized();
                var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                var refs = ledger.Current.Features
                    .Select(r => string.Equals(r.Id, OpenLoop.Core.Store.FeatureCatalog.BrowserIdle, StringComparison.Ordinal)
                        ? new OpenLoop.Core.Store.FeatureRef
                        {
                            Id = r.Id,
                            Params = new() { ["value"] = System.Text.Json.JsonSerializer.SerializeToElement(45) }
                        }
                        : r)
                    .ToList();
                features.Remove(OpenLoop.Core.Store.FeatureCatalog.BrowserIdle);
                ledger.Activate(refs, "user", "网页页闲置 45 分钟再销毁");

                // 按工作区配置生效
                Assert.Equal(45, LoopAppLayout.IdleMinutes(workspace));
            }
            finally
            {
                try { Directory.Delete(workspace, true); } catch { }
            }
        }
    }
}
