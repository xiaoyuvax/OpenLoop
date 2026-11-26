using System.Text;
using System.Text.Json;
using Common.Logging;
using Microsoft.Web.WebView2.Core;
using Puppet.Core;
using Puppet.Core.AppAgent;
using Puppet.Core.Extentions;
using OpenLoop.Service.OpenUI;

namespace OpenLoop.App
{
    /// <summary>页面能问到的本会话状态（只读快照，不含别的会话）。</summary>
    public sealed record LoopAppSessionState(bool Busy, string Model, string Title, int QueueLength);

    /// <summary>
    /// 一个会话的内嵌浏览器向 agent 暴露的接口（Puppet 实例 <c>LoopApp-&lt;会话id&gt;</c>）。
    ///
    /// 会话边界全靠这个形状：实例在 <see cref="Configure"/> 时绑定 owner，
    /// 所有 action 的签名里都没有会话参数——页面无论怎么调用都指不到别的会话。
    /// 页面经 <c>chrome.webview.postMessage</c> 进来（见 OnWebMessage），不经端口、不需要 key。
    /// </summary>
    public partial class BrowserView : IPuppet
    {
        /// <summary>WebView2 虚拟域：把本会话的 LoopApps 目录当成一个站点。</summary>
        internal const string VirtualHost = "openui.local";

        private string _owner = "";
        private string _loopAppsRoot = "";
        private bool _registered;
        private bool _mapped;
        private string _shimId;

        [PuppetIgnore] string IPuppet.AgentAccessKey => PuppetKeyVault.GlobalKey;
        [PuppetIgnore] ILog IPuppet.AgentLog => AppLog.Log;

        string IPuppet.AgentInstanceName => "LoopApp-" + (string.IsNullOrWhiteSpace(_owner) ? "unknown" : _owner);

        /// <summary>本浏览器所属会话 id（只用于实例名与人看；任何 action 都不接受它做参数）。</summary>
        [PuppetIgnore] public string Owner => _owner;

        /// <summary>读本会话状态（宿主注入：BrowserView 不认识 Session）。</summary>
        [PuppetIgnore] public Func<LoopAppSessionState> StateProvider { get; set; }

        /// <summary>把页面数据作为一条用户消息入队本会话；返回一句结果说明。</summary>
        [PuppetIgnore] public Func<string, string> SubmitHandler { get; set; }

        /// <summary>停止本会话当前轮；返回是否本来在跑。</summary>
        [PuppetIgnore] public Func<bool> StopHandler { get; set; }

        /// <summary>
        /// 绑定 owner 与该会话的 LoopApps 目录，并把本视图注册成会话级 Puppet 实例。
        /// 「网页」页创建时由宿主调用；重复调用只更新路径，不重复注册。
        /// 不建目录：落盘由 <see cref="LoopAppPaths.Create"/> 这类写入口负责。
        /// </summary>
        public void Configure(string owner, string loopAppsRoot)
        {
            _owner = (owner ?? "").Trim();
            string root = loopAppsRoot ?? "";
            if (!string.Equals(_loopAppsRoot, root, StringComparison.OrdinalIgnoreCase)) _mapped = false;
            _loopAppsRoot = root;
            if (_registered || string.IsNullOrWhiteSpace(_owner)) return;
            PuppetRegistry.Register(this);
            _registered = true;
        }

        // ---- 页面可直接调的 action（A 面/B 面同名；页面经消息桥进来） ----

        /// <summary>读本会话状态。</summary>
        [PuppetAction(Desc = "读本会话状态：busy / model / title / queueLen")]
        public string SessionState()
        {
            var state = StateProvider?.Invoke();
            if (state == null) return "{\"busy\":false}";
            return JsonSerializer.Serialize(new
            {
                busy = state.Busy,
                model = state.Model ?? "",
                title = state.Title ?? "",
                queueLen = state.QueueLength
            });
        }

        /// <summary>把页面提交的数据作为一条用户消息放进本会话队列（唯一会唤醒 agent 的动作）。</summary>
        [PuppetAction(Desc = "把页面提交的数据作为一条用户消息放进本会话队列")]
        public OperationResult SessionSubmit(string text, string tag = "task")
        {
            if (string.IsNullOrWhiteSpace(text)) return OperationResult.Failure("text 为空");
            var handler = SubmitHandler;
            if (handler == null) return OperationResult.Failure("这个浏览器没有绑定会话");
            string message = handler(text.Trim());
            Submitted = true;
            return OperationResult.Success(message ?? "已提交", new { accepted = true });
        }

        /// <summary>
        /// 页面提交过数据（LoopApp 的任务做完了）：宿主在本轮收尾时收起网页页。
        /// 下一次 LoopAppOpen 会清掉它——新一轮任务又从这里开始。
        /// </summary>
        [PuppetIgnore] public bool Submitted { get; private set; }

        /// <summary>新一轮任务开始（打开 LoopApp）：清掉上一轮的提交标记与「用户已停靠」的意愿。</summary>
        private void ResetSubmitted()
        {
            Submitted = false;
            _userDocked = false;
        }

        /// <summary>停止本会话当前轮。</summary>
        [PuppetAction(Desc = "停止本会话当前轮")]
        public OperationResult SessionStop()
            => StopHandler?.Invoke() == true
                ? OperationResult.Success("已停止本会话当前轮")
                : OperationResult.Failure("本会话没有在运行");

        /// <summary>
        /// 打开本会话的一个 LoopApp。<paramref name="target"/> 收应用目录名，也收完整页面 URL
        /// （agent 手上常常是 <c>LoopAppPaths.Url(name)</c> 那个串，别让它因为多传了个 URL 就失败）。
        /// </summary>
        [PuppetAction(Desc = "打开本会话的一个 LoopApp：name 是应用目录名（也接受页面 URL）")]
        public OperationResult LoopAppOpen(string target)
        {
            string slug = SlugOf(target);
            if (string.IsNullOrWhiteSpace(_loopAppsRoot))
                return OperationResult.Failure("这个浏览器没有绑定会话工作区");
            if (string.IsNullOrWhiteSpace(slug))
                return OperationResult.Failure("name 为空");
            if (!File.Exists(Path.Combine(_loopAppsRoot, slug, "index.html")))
                return OperationResult.Failure($"还没有这个 LoopApp：{slug}");

            string url = $"https://{VirtualHost}/{slug}/index.html";
            ResetSubmitted();
            if (!Navigate(url, out string error)) return OperationResult.Failure(error);
            // LoopApp 是给人点的：打开即解锁（普通网页仍按原语义锁着）
            SetInteract(true);
            return OperationResult.Success("已打开 " + slug, url);
        }

        /// <summary>
        /// 重新加载当前 LoopApp。
        /// 不用 <c>location.reload()</c>：经脚本触发的 reload 在 WebView2 里会被静默丢弃
        /// （实测页面时间戳不动），而重新 Navigate 到同一个 URL 是确定生效的。
        /// </summary>
        [PuppetAction(Desc = "重新加载本会话的 LoopApp（改完文件用这个，不要用 location.reload()）")]
        public OperationResult LoopAppReload()
        {
            string url = Url;
            if (!IsLoopAppPage) return OperationResult.Failure("当前页面不是 LoopApp（先 LoopAppOpen）");
            // 带时间戳绕开 WebView2 的启发式缓存：映射来的响应没有 Cache-Control，同名文件会被复用
            string fresh = url.Contains('?') ? url : url + "?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds();
            return Navigate(fresh, out string error)
                ? OperationResult.Success("已重新加载", fresh)
                : OperationResult.Failure(error);
        }

        /// <summary>在页面里执行 JS，返回 WebView2 的 JSON 结果串（字符串结果带引号）。</summary>
        [PuppetAction(Desc = "在本会话的 LoopApp 里执行 JS；改完文件用 LoopAppReload")]
        public OperationResult LoopAppEval(string js)
        {
            if (string.IsNullOrWhiteSpace(js)) return OperationResult.Failure("js 为空");
            if (!IsLoopAppPage) return OperationResult.Failure("当前页面不是 LoopApp（先 LoopAppOpen）");
            return Eval(js, out string value, out string error)
                ? OperationResult.Success("已执行", value)
                : OperationResult.Failure(error);
        }

        /// <summary>截当前页面；PNG 经 Puppet asset 通道返回（agent 可直接下载，不落临时文件路径）。</summary>
        [PuppetAction(Desc = "截取本会话 LoopApp 的当前画面，PNG 作为可下载产物返回")]
        public OperationResult LoopAppShot()
        {
            if (!Screenshot(out string png, out string error)) return OperationResult.Failure(error);
            return OperationResult.Success("已截图",
                new PuppetArtifact("loopapp-" + LoopAppPaths.Slug(_owner) + ".png", "image/png", Convert.FromBase64String(png)));
        }

        /// <summary>浏览器状态：锁定/浮动/地址。</summary>
        public string LoopAppState
            => $"{(_interact ? "unlocked" : "locked")} | {(IsUndocked ? "undocked" : "docked")} | {Url}";

        [PuppetAction(Desc = "把本会话的 LoopApp 搬进独立窗口")]
        public OperationResult LoopAppUndock()
        {
            Undock();
            return OperationResult.Success("已搬进独立窗口");
        }

        [PuppetAction(Desc = "把独立窗口里的 LoopApp 搬回右侧栏")]
        public OperationResult LoopAppDock()
        {
            BringBack();
            return OperationResult.Success("已搬回右侧栏");
        }

        [PuppetAction(Desc = "解锁/锁定：解锁后用户可以直接点按 LoopApp")]
        public OperationResult LoopAppInteract(bool enabled)
        {
            SetInteract(enabled);
            return OperationResult.Success(enabled ? "已解锁：用户可以操作" : "已锁定：由 Agent 控制");
        }

        /// <summary>当前页面是不是本会话的 LoopApp（而不是外部网页）。</summary>
        private bool IsLoopAppPage
            => (Url ?? "").StartsWith("https://" + VirtualHost + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 从「目录名」或「页面 URL」里取出应用目录名。
        /// 认 <c>https://openui.local/&lt;slug&gt;/...</c>，其余一律按名字归一。
        /// </summary>
        private static string SlugOf(string target)
        {
            string text = (target ?? "").Trim();
            if (text.Length == 0) return "";
            foreach (string prefix in new[] { "https://" + VirtualHost + "/", "http://" + VirtualHost + "/" })
            {
                if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                text = text[prefix.Length..];
                int slash = text.IndexOfAny(['/', '?', '#']);
                if (slash >= 0) text = text[..slash];
                break;
            }
            return LoopAppPaths.Slug(text);
        }

        /// <summary>
        /// 把 <c>https://openui.local</c> 映射到本会话的 LoopApps 目录。
        /// 纯读路径：目录不存在就直接回 false，建目录是写入口（<see cref="LoopAppPaths.Create"/>）的事。
        /// 需要时先把 WebView2 建起来——第一次 <see cref="LoopAppOpen"/> 时它还没初始化。
        /// </summary>
        private bool ApplyVirtualHost()
        {
            if (_mapped) return true;
            if (string.IsNullOrWhiteSpace(_loopAppsRoot)) return false;
            if (!Directory.Exists(_loopAppsRoot)) return false;
            if (!_ready && !EnsureCore(out _)) return false;
            try
            {
                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHost, _loopAppsRoot, CoreWebView2HostResourceAccessKind.Allow);
                _mapped = true;
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("openui.local 映射失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 注入桥垫片：每个新文档都会先跑它，把 postMessage 往返包成 <c>ui.call</c> 的 Promise。
        /// 只在映射域里生效（垫片自己也判 origin），被导航到远程站点等于没有。
        /// </summary>
        private void InstallShim(CoreWebView2 core)
        {
            if (_shimId != null) return;
            try
            {
                var task = core.AddScriptToExecuteOnDocumentCreatedAsync(OpenUiBridge.Shim);
                if (Pump(task)) _shimId = task.Result;
            }
            catch (Exception ex)
            {
                AppLog.Warn("LoopApp 桥垫片注入失败：" + ex.Message);
            }
        }

        // ---- 页面 → agent 的消息桥 ----

        /// <summary>页面发来一条消息：转成本实例的 action，结果按 id 回给页面。</summary>
        internal void OnWebMessage(string source, string json)
        {
            string id = null;
            try
            {
                // shim 注入到每个新文档：远程站点也能拿到 postMessage，必须按来源拦住
                if (!_ready || !IsLoopAppSource(source)) return;

                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "null" : json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    id = idEl.GetString();

                string action = root.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String
                    ? a.GetString() : null;
                if (string.IsNullOrWhiteSpace(action))
                {
                    Reply(id, false, null, "缺少 action");
                    return;
                }

                string body = ((IPuppet)this).Invoke(action, BuildArgs(root));
                var (ok, data, err) = ParseAgentResult(body);
                Reply(id, ok, data, err);
            }
            catch (Exception ex)
            {
                Reply(id, false, null, ex.Message);
            }
        }

        /// <summary>只有映射域下的页面算 LoopApp；其余来源一律不服务。</summary>
        private static bool IsLoopAppSource(string source)
            => !string.IsNullOrWhiteSpace(source)
               && source.StartsWith("https://" + VirtualHost + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>把 {args:{...}} 转成位置参数（按 action 方法签名的顺序取）。</summary>
        private static object[] BuildArgs(JsonElement root)
        {
            if (!root.TryGetProperty("args", out var argsEl) || argsEl.ValueKind != JsonValueKind.Object)
                return [];

            var values = new List<object>();
            foreach (var prop in argsEl.EnumerateObject())
            {
                values.Add(prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => prop.Value.TryGetInt64(out long l) ? l : prop.Value.GetDouble(),
                    _ => prop.Value.GetRawText()
                });
            }
            return [.. values];
        }

        private static (bool Ok, string Data, string Error) ParseAgentResult(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return (true, null, null);

            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException) { return (true, body, null); }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return (true, body, null);

                if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
                {
                    string err = root.TryGetProperty("err", out var e) && e.ValueKind == JsonValueKind.String
                        ? e.GetString() : "调用失败";
                    return (false, null, err);
                }

                if (!root.TryGetProperty("result", out var result)) return (true, null, null);

                if (result.ValueKind == JsonValueKind.Object)
                {
                    // OperationResult → {ok, message, data}：页面只关心 ok 与 data
                    if (result.TryGetProperty("ok", out var rok) && rok.ValueKind == JsonValueKind.False)
                    {
                        string msg = result.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                            ? m.GetString() : "调用失败";
                        return (false, null, msg);
                    }
                    string data = result.TryGetProperty("data", out var d) ? d.GetRawText() : null;
                    return (true, data, null);
                }

                return (true, result.ValueKind == JsonValueKind.String ? result.GetString() : result.GetRawText(), null);
            }
        }

        private void Reply(string id, bool ok, string data, string error)
        {
            if (string.IsNullOrWhiteSpace(id) || !_ready) return;
            OnUi(() =>
            {
                if (IsDisposed || _web.CoreWebView2 == null) return;
                var sb = new StringBuilder("{\"id\":");
                sb.Append(JsonSerializer.Serialize(id)).Append(",\"ok\":").Append(ok ? "true" : "false");
                if (ok)
                {
                    sb.Append(",\"data\":").Append(string.IsNullOrEmpty(data) ? "null" : data);
                }
                else
                {
                    sb.Append(",\"error\":").Append(JsonSerializer.Serialize(error ?? ""));
                }
                sb.Append('}');
                try { _web.CoreWebView2.PostWebMessageAsJson(sb.ToString()); }
                catch (Exception) { /* 页面已销毁 */ }
            });
        }
    }
}
