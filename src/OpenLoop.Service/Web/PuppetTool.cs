using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using RuntimeInfo = OpenLoop.Service.Runtime.RuntimeInfo;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// Puppet 探测工具：A/I 的内部 Agent 用它看自己、对端和壳的运行时状态。
    /// target=self 走进程内 Hub（ProcessWebRequest，无端口/网络）；其余走 Puppet 接口。
    /// </summary>
    public sealed class PuppetTool : ITool
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        /// <summary>响应回填模型的字节上限（账本 limit.puppet_result，注入；0=默认）。</summary>
        private readonly int _maxBytes;

        public PuppetTool(int maxBytes = 0)
        {
            _maxBytes = maxBytes > 0 ? maxBytes : ToolText.ModelBytes;
        }

        public string Name => "Puppet";

        public string Description =>
            "探测 Puppet 实例与状态。target=self 走本进程内 Hub（不经端口、无网络）；target=a / i / shell 走 Puppet 接口。" +
            "壳只被动提供窗口/会话状态；Web 页面是不完整 Puppet（实例 WebUi，只有页面状态与截图钩子，截图需页面勾选开关）。" +
            "chat=读本机会话留言板；book=看在场会话、各自在做的事与最近几次工具步骤；say=以本会话身份发言（text、to），用于会话间协作。" +
            "带 to（或在正文里 @ 一个同工作区会话）的 say 是私信：只有你与目标可见，并会直接投递到目标会话的输入队列（协作提示样式），不必等它来读板；省略才是广播。";

        public string ParamsSchema =>
            "target: self|a|i|shell（默认 self）; action: registry|state|get|describe|invoke|chat|book|say（默认 registry）; " +
            "instance: string（state/get/describe/invoke 必填）; path: string（get 的属性路径）; " +
            "method: string（invoke 的方法名）; args: array（invoke 的位置参数，JSON 数组）; " +
            "chat: tail/q/from/to（读留言板）; say: text（要说的内容）、to（同工作区会话 id，也可只写前缀；省略就认正文里的 @id；都没有=广播，只有主动投递才算送达）";

        public bool IsReadOnly => false;

        public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string target = (args.Str("target") ?? "self").Trim().ToLowerInvariant();
            string action = (args.Str("action") ?? "registry").Trim().ToLowerInvariant();
            string instance = args.Str("instance")?.Trim();
            string path = args.Str("path")?.Trim();

            var query = new List<string>();
            void Add(string name, string value)
            {
                if (!string.IsNullOrEmpty(value)) query.Add(name + "=" + Uri.EscapeDataString(value));
            }

            string route;
            string body = null;
            string self = AgentIdentity.Id;
            // 私信（say 带 to）投递用：目标与原文，投递在写板成功之后
            string recipient = null;
            string saidText = null;
            switch (action)
            {
                case "registry":
                    route = "/agent/registry";
                    break;
                case "state":
                    if (string.IsNullOrWhiteSpace(instance)) return AgentToolResult.Failure("state 需要 instance");
                    route = "/agent/state";
                    Add("name", instance);
                    break;
                case "describe":
                    if (string.IsNullOrWhiteSpace(instance)) return AgentToolResult.Failure("describe 需要 instance");
                    route = "/agent/describe";
                    Add("name", instance);
                    break;
                case "get":
                    if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(path))
                        return AgentToolResult.Failure("get 需要 instance 和 path");
                    route = "/agent/get";
                    Add("name", instance);
                    Add("path", path);
                    break;
                case "invoke":
                    if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(args.Str("method")?.Trim()))
                        return AgentToolResult.Failure("invoke 需要 instance 和 method");
                    route = "/agent/invoke";
                    Add("name", instance);
                    Add("method", args.Str("method")?.Trim());
                    body = RawJson(args.Raw.TryGetValue("args", out var v) ? v : null);
                    break;
                case "chat":
                    route = "/agent/chat";
                    Add("tail", args.Str("tail") ?? "10");
                    Add("q", args.Str("q"));
                    Add("from", args.Str("from"));
                    Add("to", args.Str("to"));
                    break;
                case "book":
                    // 与 peer-board 技能同一窗口：30 分钟内活动过的都算在场，是否停手看 idleSeconds
                    route = "/agent/book?includeStale=true";
                    break;
                case "say":
                    {
                        if (string.IsNullOrWhiteSpace(self))
                            return AgentToolResult.Failure("当前会话没有留言板身份，不能发言");
                        saidText = args.Str("text") ?? args.Str("message");
                        if (string.IsNullOrWhiteSpace(saidText)) return AgentToolResult.Failure("say 需要 text");
                        // 定向：to 显式给，或在正文里 @ 了同工作区会话（@A-xxxx / @I-xxxx / @前缀）；
                        // 两者都没有就是广播（要对方自己来读板）。
                        recipient = PeerDelivery.MentionOf(saidText, args.Str("to"));
                        if (string.Equals(recipient, self, StringComparison.OrdinalIgnoreCase)) recipient = null;
                        route = "/agent/book/heartbeat";
                        body = JsonSerializer.Serialize(new { id = self, say = saidText, sayTo = recipient });
                        break;
                    }
                default:
                    return AgentToolResult.Failure($"未知 action：{action}（可用 registry/state/get/describe/invoke/chat/book/say）");
            }

            // 留言板在 A 的 Puppet 实例上：壳里的 Loop 会话固定走 a，A 里的 Worker 会话走 self。
            if ((action is "chat" or "say" or "book") && !string.IsNullOrWhiteSpace(self))
                target = AgentIdentity.BoardTarget;

            string urlPath = query.Count == 0 ? route : route + "?" + string.Join("&", query);
            bool post = action is "invoke" or "say";
            bool board = action is "chat" or "say" or "book";

            // 留言板在 A 上；A 掉线时同一个会话继续用 I（再不行用本进程兜底）。
            string[] candidates = board && !string.IsNullOrWhiteSpace(self)
                ? (target == "a" ? new[] { "a", "i", "self" } : new[] { target, "self" })
                : new[] { target };

            Exception lastError = null;
            string lastTarget = null;
            foreach (string candidate in candidates)
            {
                // 本进程：直调 ProcessWebRequest（Hub），不经监听端口
                if (candidate == "self" && PuppetBridge.LocalHub is { } hub)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(post ? body ?? "[]" : "[]");
                        var response = post
                            ? await hub.PostAsync(urlPath, body: doc.RootElement)
                            : await hub.GetAsync(urlPath);
                        return await FinishAsync(response.Status, response.Body, action, recipient, self, saidText, ct);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        continue;
                    }
                }

                string endpoint = candidate switch
                {
                    "self" => SelfEndpoint(),
                    "a" or "i" => RuntimeInfo.TryLoad(AppPaths.RuntimeFile(candidate)) is { IsAlive: true } peer ? peer.Bus : null,
                    "shell" => PuppetBridge.ShellEndpoint,
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(endpoint)) continue;
                lastTarget = candidate;

                string key = PuppetBridge.Key;
                if (string.IsNullOrWhiteSpace(key)) return AgentToolResult.Failure("读不到 Puppet key 文件");

                try
                {
                    using var request = new HttpRequestMessage(
                        post ? HttpMethod.Post : HttpMethod.Get,
                        (endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? endpoint : "http://" + endpoint) + urlPath);
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                    if (post)
                        request.Content = new StringContent(body ?? "[]", Encoding.UTF8, "application/json");

                    using var response = await Http.SendAsync(request, ct);
                    string text = await response.Content.ReadAsStringAsync(ct);
                    return await FinishAsync((int)response.StatusCode, text, action, recipient, self, saidText, ct);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            if (lastTarget == null)
                return AgentToolResult.Failure($"找不到 target={target} 的 Puppet 端点（壳未写发现文件 / 对端未启动）");
            return AgentToolResult.Failure($"Puppet 接口调用失败（{lastTarget}）：{lastError?.Message}");
        }

        /// <summary>
        /// 收尾：私信（say 带 to）在写板成功后主动投递到目标会话的队列；
        /// 读板结果按「私信只有发送方与目标可见」过滤掉别人的私信。
        /// </summary>
        private async Task<AgentToolResult> FinishAsync(int status, string body, string action,
            string recipient, string self, string saidText, CancellationToken ct)
        {
            bool board = action is "say" or "chat" or "book";
            string text = board && !string.IsNullOrWhiteSpace(self)
                ? BoardPrivacy.Filter(body, self)
                : body;
            var result = Result(status, text);
            if (!result.Ok || string.IsNullOrWhiteSpace(recipient)) return result;

            string delivery = await PeerDelivery.DeliverAsync(self, recipient, saidText, ct)
                .ConfigureAwait(false);
            string note = string.IsNullOrWhiteSpace(delivery)
                ? $"（已作为私信投递到 {recipient} 的输入队列）"
                : delivery;
            return AgentToolResult.Success((result.Data ?? "") + "\n" + note);
        }

        private static string SelfEndpoint()
        {
            if (PuppetBridge.SelfRole is "a" or "i")
                return RuntimeInfo.TryLoad(AppPaths.RuntimeFile(PuppetBridge.SelfRole))?.Bus;
            if (PuppetBridge.SelfRole == "shell") return PuppetBridge.ShellEndpoint;
            return null;
        }

        private AgentToolResult Result(int status, string body)
        {
            string text = string.IsNullOrWhiteSpace(body) ? "(no body)" : body;
            // 截图类产物: PuppetArtifact -> base64
            var image = ExtractImage(text);
            if (image != null)
            {
                return status is 200 or 204
                    ? AgentToolResult.Success("截图完成：图片已附件返回", new[] { image })
                    : AgentToolResult.Failure($"HTTP {status}: (image)");
            }

            text = ToolText.Clip(text, _maxBytes);
            return status is 200 or 204
                ? AgentToolResult.Success(text)
                : AgentToolResult.Failure($"HTTP {status}: {text}");
        }
        /// <summary>识别 PuppetArtifact 图片 (result.Content = base64).</summary>
        /// <summary>璇嗗埆 /agent/invoke 杩斿洖鐨?PuppetArtifact 鍥剧墖锛坮esult.Content 鏄?base64 PNG锛夈€?/summary>
        private static ToolAttachment ExtractImage(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Length < 200 || !body.Contains("Puppet.Core.AppAgent.PuppetArtifact"))
                return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("result", out var result)) return null;
                if (!result.TryGetProperty("Content", out var content)) return null;
                if (content.ValueKind != JsonValueKind.String) return null;
                string b64 = content.GetString();
                if (string.IsNullOrEmpty(b64) || b64.Length < 100) return null;
                string mime = result.TryGetProperty("ContentType", out var ct) && ct.ValueKind == JsonValueKind.String
                    ? ct.GetString()
                    : "image/png";
                return new ToolAttachment { MimeType = mime, Base64 = b64 };
            }
            catch (JsonException)
            {
                return null;
            }
        }
        private static string RawJson(object value)
        {
            return value switch
            {
                null => "[]",
                JsonElement element => element.GetRawText(),
                string text => string.IsNullOrWhiteSpace(text) ? "[]" : text,
                _ => JsonSerializer.Serialize(value)
            };
        }
    }

    public static class PuppetTools
    {
        /// <summary>maxBytes 取工作区账本 limit.puppet_result；不传则用默认上限。</summary>
        public static IEnumerable<ITool> For(int maxBytes = 0) => [new PuppetTool(maxBytes)];
    }
}
