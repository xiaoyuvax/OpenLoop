using System.Net;
using System.Net.Http.Json;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Worker;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// 访问 A 的 /api/sessions。WinForms 与浏览器走同一组端点，界面不再自己跑 Agent。
    /// 与 A 共用 bus.key；地址优先取 runtime/a.json，其次默认 19180。
    /// </summary>
    public sealed class WorkerApiClient : IDisposable
    {
        private readonly HttpClient _http;

        /// <summary>手动压缩要等一次模型摘要（服务端兜底 120 秒 + 传输余量），不能沿用轮询用的 30 秒超时。</summary>
        private readonly HttpClient _long;

        public WorkerApiClient(string endpoint = null, string key = null)
        {
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? Discover() : endpoint;
            string actual = key ?? KeyFile.ReadOrCreate(AppPaths.BusKeyPath);
            // loopback：系统代理就是 VPN 自己，走代理会把本地请求发到隧道外
            _http = Create(TimeSpan.FromSeconds(30), actual);
            _long = Create(TimeSpan.FromMinutes(5), actual);
        }

        private HttpClient Create(TimeSpan timeout, string key)
        {
            var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            {
                BaseAddress = new Uri("http://" + Endpoint),
                Timeout = timeout
            };
            if (!string.IsNullOrEmpty(key))
                http.DefaultRequestHeaders.TryAddWithoutValidation(BusProtocol.KeyHeader, key);
            return http;
        }

        public string Endpoint { get; }

        public static string Discover()
        {
            var info = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(BusRoles.A));
            return info is { IsAlive: true } && !string.IsNullOrWhiteSpace(info.Bus)
                ? info.Bus
                : "127.0.0.1:19180";
        }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                using var response = await _http.GetAsync("/api/capabilities", ct);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<WorkerSessionSnapshot>> ListAsync(bool summary = false, CancellationToken ct = default)
            => await _http.GetFromJsonAsync<List<WorkerSessionSnapshot>>(
                   summary ? "/api/sessions?summary=1" : "/api/sessions", BusProtocol.Json, ct)
               ?? [];

        /// <summary>没有这个会话时返回 null（A 用 404 表示）。传输层瞬断重试一次：
        /// 界面每 250ms 轮询一次，不该因为一条 reset 就把还在跑的轮次判成失败。</summary>
        public async Task<WorkerSessionSnapshot> GetAsync(string id, CancellationToken ct = default)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var response = await _http.GetAsync(Path(id), ct);
                    if (response.StatusCode == HttpStatusCode.NotFound) return null;
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadFromJsonAsync<WorkerSessionSnapshot>(BusProtocol.Json, ct);
                }
                catch (HttpRequestException) when (attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                }
            }
        }

        public async Task<WorkerSessionSnapshot> CreateAsync(string id, string workspace, string model,
            string effort, CancellationToken ct = default)
            => await PostAsync("/api/sessions",
                new { id, workspace, model, effort }, ct);

        public async Task<WorkerSessionSnapshot> SendAsync(string id, string tag, string text,
            CancellationToken ct = default, string imageModel = null,
            IReadOnlyList<Media.AttachmentPayload> attachments = null, IReadOnlyList<string> paths = null,
            string context = null)
            => await PostAsync(Path(id) + "/messages",
                new { tag, text, context, imageModel, attachments = attachments ?? [], paths = paths ?? [] }, ct);

        /// <summary>
        /// 以「协作提示」身份给会话插一条消息（agent 自动塞的仿用户消息）：空闲直接开跑，运行中插到队首、
        /// 当前轮在下一个 step 前收尾后先发它；界面折叠成一行、淡黄底，不冒充用户输入。
        /// </summary>
        public async Task<WorkerSessionSnapshot> NoticeAsync(string id, string text, string tag = "puppet",
            CancellationToken ct = default)
            => await PostAsync(Path(id) + "/notice", new { text, tag }, ct);

        public async Task<WorkerSessionSnapshot> StopAsync(string id, CancellationToken ct = default)
            => await PostAsync(Path(id) + "/stop", new { }, ct);

        /// <summary>软停止：当前轮在下一个 step 前收尾，忙着的工具不打断；on=false 收回。</summary>
        public async Task<WorkerSessionSnapshot> InterruptAsync(string id, bool on = true, CancellationToken ct = default)
            => await PostAsync(Path(id) + "/interrupt", new { on }, ct);

        /// <summary>撤回对话：让 A 也删掉第 keep 轮之后的历史，否则下一次发送会把撤回的轮次带回来。</summary>
        public async Task<WorkerSessionSnapshot> TruncateAsync(string id, int keep, CancellationToken ct = default)
            => await PostAsync(Path(id) + "/truncate", new { keep }, ct);

        /// <summary>手动压缩上下文（/compact）：A 把较早的消息换成摘要，返回压缩后的历史供界面同步。</summary>
        public async Task<WorkerCompactResult> CompactAsync(string id, CancellationToken ct = default)
        {
            using var response = await _long.PostAsJsonAsync(Path(id) + "/compact", new { }, BusProtocol.Json, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<WorkerCompactResult>(BusProtocol.Json, ct);
        }

        /// <summary>让 A 丢掉这个会话（正在跑的先停）；A 不认识它也算成功。</summary>
        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            using var response = await _http.DeleteAsync(Path(id), ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return;
            response.EnsureSuccessStatusCode();
        }

        public async Task<WorkerSessionSnapshot> ResumeAsync(string id, CancellationToken ct = default)
            => await PostAsync(Path(id) + "/resume", new { }, ct);

        public async Task<WorkerSessionSnapshot> SetModelAsync(string id, string model, string effort,
            CancellationToken ct = default)
            => await PostAsync(Path(id) + "/model", new { model, effort }, ct);

        /// <summary>0 = 拒绝，1 = 允许一次，2 = 同类后续都允许。</summary>
        public async Task<WorkerSessionSnapshot> ApproveAsync(string id, string promptId, int decision,
            CancellationToken ct = default)
            => await PostAsync(Path(id) + "/approval", new { id = promptId, decision }, ct);

        private static string Path(string id) => "/api/sessions/" + Uri.EscapeDataString(id ?? "");

        private async Task<WorkerSessionSnapshot> PostAsync(string path, object body, CancellationToken ct)
        {
            using var response = await _http.PostAsJsonAsync(path, body, BusProtocol.Json, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<WorkerSessionSnapshot>(BusProtocol.Json, ct);
        }

        public void Dispose()
        {
            _http.Dispose();
            _long.Dispose();
        }
    }
}
