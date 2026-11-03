using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Core.Safety;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Config;
using OpenLoop.Service.Web;
using OpenLoop.Service.Worker;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>界面客户端依赖的 API 形状：显式 id、404、审批回传。</summary>
    public class WorkerApiHandlerTests
    {
        private const string Key = "test-key";

        private static readonly JsonSerializerOptions Json = BusProtocol.Json;

        private static WorkerApiHandler Handler(WorkerSessionHost host) => new(host, Key);

        private static WebRequest Call(WorkerApiHandler handler, string method, string path, string body = null,
            string query = null)
        {
            var req = new WebRequest
            {
                Method = method,
                InputStream = new MemoryStream(body == null ? [] : Encoding.UTF8.GetBytes(body))
            };
            req.Path = path;
            if (!string.IsNullOrEmpty(query))
            {
                var pair = query.Split('=', 2);
                req.Queries[pair[0]] = pair.Length > 1 ? pair[1] : "";
            }
            req.Headers[BusProtocol.KeyHeader] = Key;
            req.Response = new WebResponse();
            Assert.True(handler.Handle(req), $"{method} {path} 未被处理");
            return req;
        }

        private static WorkerSessionSnapshot Snapshot(WebRequest req)
            => JsonSerializer.Deserialize<WorkerSessionSnapshot>(req.Response.Buffer, Json);

        [Fact]
        public void Create_honours_the_id_supplied_by_the_ui()
        {
            var host = new WorkerSessionHost((_, _) => Task.FromResult(new WorkerRunResult { Outcome = "ok" }));
            var handler = Handler(host);

            var created = Call(handler, "POST", "/api/sessions",
                """{"id":"abc12345","workspace":"C:/work","model":"openrouter/free","effort":""}""");

            Assert.Equal(201, created.Response.StatusCode);
            Assert.Equal("abc12345", Snapshot(created).Id);

            var fetched = Call(handler, "GET", "/api/sessions/abc12345");
            Assert.Equal("abc12345", Snapshot(fetched).Id);

            var missing = Call(handler, "GET", "/api/sessions/nope");
            Assert.Equal(404, missing.Response.StatusCode);
        }

        [Fact]
        public void Delete_removes_the_session_so_the_shell_stops_listing_it()
        {
            var host = new WorkerSessionHost((_, _) => Task.FromResult(new WorkerRunResult { Outcome = "ok" }));
            var handler = Handler(host);

            Call(handler, "POST", "/api/sessions",
                """{"id":"del00001","workspace":"C:/work","model":"m","effort":""}""");

            var deleted = Call(handler, "DELETE", "/api/sessions/del00001");
            Assert.Equal(200, deleted.Response.StatusCode);
            Assert.Null(host.Get("del00001"));
            Assert.Equal(404, Call(handler, "DELETE", "/api/sessions/del00001").Response.StatusCode);
        }

        [Fact]
        public void Summary_list_stays_small_but_keeps_state()
        {
            var host = new WorkerSessionHost((_, _) => Task.FromResult(new WorkerRunResult { Outcome = "ok" }));
            var handler = Handler(host);
            Call(handler, "POST", "/api/sessions",
                """{"id":"sum00001","workspace":"C:/work","model":"m","effort":""}""");

            var summary = Call(handler, "GET", "/api/sessions", null, "summary=1");
            var list = JsonSerializer.Deserialize<List<WorkerSessionSnapshot>>(summary.Response.Buffer, Json);
            var one = Assert.Single(list);
            Assert.Equal("sum00001", one.Id);
            Assert.Equal(WorkerSessionHost.Idle, one.State);
            Assert.Empty(one.Turns);
        }

        [Fact]
        public async Task Approval_posted_through_the_api_reaches_the_running_turn()
        {
            var decisions = new List<ApprovalDecision>();
            var host = new WorkerSessionHost((request, ct) =>
            {
                var decision = request.Approve(new ApprovalRequest(
                    ApprovalKind.OutsideWorkspace, "ReadFile", "C:/other/a.txt", ""));
                lock (decisions) decisions.Add(decision);
                return Task.FromResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            });
            var handler = Handler(host);
            var session = host.Create("C:/work", "m", "", () => new AppSettings(), "sess0001");

            Call(handler, "POST", "/api/sessions/sess0001/messages", """{"tag":"task","text":"hi"}""");

            WorkerPromptView prompt = null;
            for (int i = 0; i < 100 && prompt == null; i++)
            {
                await Task.Delay(20);
                prompt = Snapshot(Call(handler, "GET", "/api/sessions/sess0001")).Prompts.FirstOrDefault();
            }

            Assert.NotNull(prompt);
            Assert.Equal("approval", prompt.Kind);
            Assert.Equal("outside", prompt.Category);

            Call(handler, "POST", "/api/sessions/sess0001/approval",
                $$"""{"id":"{{prompt.Id}}","decision":1}""");

            for (int i = 0; i < 100 && host.Get("sess0001").State == WorkerSessionHost.Running; i++)
                await Task.Delay(20);

            Assert.Equal(WorkerSessionHost.Done, host.Get("sess0001").State);
            lock (decisions) Assert.Equal(ApprovalDecision.AllowOnce, Assert.Single(decisions));
        }

        [Fact]
        public void Wrong_key_is_not_served()
        {
            var host = new WorkerSessionHost((_, _) => Task.FromResult(new WorkerRunResult { Outcome = "ok" }));
            var req = new WebRequest { Method = "GET", InputStream = new MemoryStream() };
            req.Path = "/api/sessions";
            req.Headers[BusProtocol.KeyHeader] = "wrong";
            req.Response = new WebResponse();

            Assert.True(Handler(host).Handle(req));
            Assert.Equal(404, req.Response.StatusCode);
        }
    }
}
