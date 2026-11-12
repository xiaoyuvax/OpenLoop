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
        private const int MaxChars = 6000;
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        public string Name => "Puppet";

        public string Description =>
            "探测 Puppet 实例与状态。target=self 走本进程内 Hub（不经端口、无网络）；target=a / i / shell 走 Puppet 接口。" +
            "壳只被动提供窗口/会话状态；Web 页面是不完整 Puppet（实例 WebUi，只有页面状态与截图钩子，截图需页面勾选开关）。";

        public string ParamsSchema =>
            "target: self|a|i|shell（默认 self）; action: registry|state|get|describe|invoke（默认 registry）; " +
            "instance: string（state/get/describe/invoke 必填）; path: string（get 的属性路径）; " +
            "method: string（invoke 的方法名）; args: array（invoke 的位置参数，JSON 数组）";

        public bool IsReadOnly => false;

        public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string target = (args.Str("target") ?? "self").Trim().ToLowerInvariant();
            string action = (args.Str("action") ?? "registry").Trim().ToLowerInvariant();
            string instance = args.Str("instance")?.Trim();
            string path = args.Str("path")?.Trim();
            string method = args.Str("method")?.Trim();

            var query = new List<string>();
            void Add(string name, string value)
            {
                if (!string.IsNullOrEmpty(value)) query.Add(name + "=" + Uri.EscapeDataString(value));
            }

            string route;
            string body = null;
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
                    if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(method))
                        return AgentToolResult.Failure("invoke 需要 instance 和 method");
                    route = "/agent/invoke";
                    Add("name", instance);
                    Add("method", method);
                    body = RawJson(args.Raw.TryGetValue("args", out var v) ? v : null);
                    break;
                default:
                    return AgentToolResult.Failure($"未知 action：{action}（可用 registry/state/get/describe/invoke）");
            }

            string urlPath = query.Count == 0 ? route : route + "?" + string.Join("&", query);
            bool post = action == "invoke";

            // 本进程：直调 ProcessWebRequest（Hub），不经监听端口
            if (target == "self" && PuppetBridge.LocalHub is { } hub)
            {
                try
                {
                    using var doc = JsonDocument.Parse(post ? body ?? "[]" : "[]");
                    var response = post
                        ? await hub.PostAsync(urlPath, body: doc.RootElement)
                        : await hub.GetAsync(urlPath);
                    return Result(response.Status, response.Body);
                }
                catch (Exception ex)
                {
                    return AgentToolResult.Failure("进程内 Puppet 调用失败：" + ex.Message);
                }
            }

            string endpoint = target switch
            {
                "self" => SelfEndpoint(),
                "a" or "i" => RuntimeInfo.TryLoad(AppPaths.RuntimeFile(target)) is { IsAlive: true } peer ? peer.Bus : null,
                "shell" => PuppetBridge.ShellEndpoint,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(endpoint))
                return AgentToolResult.Failure($"找不到 target={target} 的 Puppet 端点（壳未写发现文件 / 对端未启动）");

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
                return Result((int)response.StatusCode, text);
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"Puppet 接口调用失败（{target}）：{ex.Message}");
            }
        }

        private static string SelfEndpoint()
        {
            if (PuppetBridge.SelfRole is "a" or "i")
                return RuntimeInfo.TryLoad(AppPaths.RuntimeFile(PuppetBridge.SelfRole))?.Bus;
            if (PuppetBridge.SelfRole == "shell") return PuppetBridge.ShellEndpoint;
            return null;
        }

        private static AgentToolResult Result(int status, string body)
        {
            string text = string.IsNullOrWhiteSpace(body) ? "(no body)" : body;
            if (text.Length > MaxChars) text = text[..MaxChars] + "…(截断)";
            return status is 200 or 204
                ? AgentToolResult.Success(text)
                : AgentToolResult.Failure($"HTTP {status}: {text}");
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
        public static IEnumerable<ITool> For() => [new PuppetTool()];
    }
}
