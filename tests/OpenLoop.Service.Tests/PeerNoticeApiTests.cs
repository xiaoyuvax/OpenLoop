using System.Text;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Config;
using OpenLoop.Service.Web;
using OpenLoop.Service.Worker;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 留言板私信的 A 侧投递入口（<c>POST /api/sessions/{id}/notice</c>）：
    /// 别的会话 say 带 to 指向本机的 Worker 会话时，PeerDelivery 走这条 HTTP 把私信推进它的队列。
    /// </summary>
    [Collection("chat-index")]
    public class PeerNoticeApiTests
    {
        private const string Key = "test-key";

        private static WebRequest Call(WorkerApiHandler handler, string method, string path, string body = null)
        {
            var req = new WebRequest
            {
                Method = method,
                InputStream = new MemoryStream(body == null ? [] : Encoding.UTF8.GetBytes(body))
            };
            req.Path = path;
            req.Headers[BusProtocol.KeyHeader] = Key;
            req.Response = new WebResponse();
            Assert.True(handler.Handle(req), $"{method} {path} 未被处理");
            return req;
        }

        [Fact]
        public async Task Notice_endpoint_pushes_the_private_message_into_the_queue()
        {
            var gate = new TaskCompletionSource<WorkerRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = new WorkerSessionHost((_, _) => gate.Task);
            var handler = new WorkerApiHandler(host, Key);
            string workspace = Path.Combine(Path.GetTempPath(), "ol-notice-" + Guid.NewGuid().ToString("N"));

            host.Create(workspace, "m", "", () => new AppSettings(), "notice01");
            Call(handler, "POST", "/api/sessions/notice01/messages", """{"tag":"task","text":"先跑一段"}""");
            Assert.Equal(WorkerSessionHost.Running, host.Get("notice01").State);

            string notice = PeerNoticeText.ForMessage("I-ccc", "同步一下边界");
            var posted = Call(handler, "POST", "/api/sessions/notice01/notice",
                $$"""{"text":{{System.Text.Json.JsonSerializer.Serialize(notice)}},"tag":"mention"}""");
            Assert.NotEqual(404, posted.Response.StatusCode);

            var snapshot = host.Get("notice01");
            int at = snapshot.Pending.FindIndex(p => p.Contains("同步一下边界"));
            Assert.True(at >= 0, "私信应以协作提示进队，实际：" + string.Join(" | ", snapshot.Pending));
            // 标 Auto：界面折成一行、淡黄底，且按「立即」排在用户排队消息之前
            Assert.True(snapshot.PendingAuto[at]);

            gate.SetResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            await Task.Delay(50);
        }

        [Fact]
        public void Notice_endpoint_reports_unknown_session_and_empty_text()
        {
            var host = new WorkerSessionHost((_, _) => Task.FromResult(new WorkerRunResult { Outcome = "ok" }));
            var handler = new WorkerApiHandler(host, Key);

            Assert.Equal(404, Call(handler, "POST", "/api/sessions/nope/notice", """{"text":"在吗"}""").Response.StatusCode);
            Assert.Equal(400, Call(handler, "POST", "/api/sessions/nope/notice", """{"text":""}""").Response.StatusCode);
        }
    }
}
