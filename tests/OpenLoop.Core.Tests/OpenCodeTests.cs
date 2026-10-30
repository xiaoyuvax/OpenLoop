using System.Net;
using System.Net.Http;
using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.OpenCode;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class OpenCodeTests
{
    [Fact]
    public void Model_id_round_trips_provider_and_model()
    {
        var reference = new OpenCodeModelRef("teamorouter", "deepseek-flash-free");

        string id = OpenCodeModelId.Format(reference);

        Assert.Equal("opencode/deepseek-flash-free@teamorouter", id);
        Assert.True(OpenCodeModelId.TryParse(id, out var parsed));
        Assert.Equal(reference, parsed);
    }

    [Fact]
    public void Model_id_rejects_foreign_or_malformed_values()
    {
        Assert.False(OpenCodeModelId.TryParse("openrouter/free", out _));
        Assert.False(OpenCodeModelId.TryParse("opencode/no-provider", out _));
        Assert.False(OpenCodeModelId.TryParse("opencode/@teamorouter", out _));
        Assert.False(OpenCodeModelId.TryParse(null, out _));
    }

    [Fact]
    public void Flatten_keeps_role_labels()
    {
        var text = OpenCodeReasoner.Flatten(
        [
            new LlmMessage("system", "规则"),
            new LlmMessage("user", "干活")
        ]);

        Assert.Contains("[system]\n规则", text);
        Assert.Contains("[user]\n干活", text);
    }

    [Fact]
    public async Task Generate_text_uses_session_scoped_generate()
    {
        var calls = new List<string>();
        string createBody = null;
        var handler = new StubHandler(async request =>
        {
            string path = request.RequestUri.AbsolutePath;
            calls.Add(request.Method.Method + " " + path);
            if (path == "/api/session")
            {
                createBody = await request.Content.ReadAsStringAsync();
                return Json("""{"data":{"id":"ses_g"}}""");
            }
            if (path == "/api/session/ses_g/generate")
                return Json("""{"data":{"text":"ok"}}""");
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "secret", handler);

        string text = await client.GenerateTextAsync(
            "hi", new OpenCodeModelRef("teamorouter", "m"), Path.GetTempPath());

        Assert.Equal("ok", text);
        Assert.Contains("POST /api/session", calls);
        Assert.Contains("POST /api/session/ses_g/generate", calls);
        Assert.Contains("DELETE /api/session/ses_g", calls);
        Assert.Contains("\"providerID\":\"teamorouter\"", createBody);
        Assert.Contains("\"directory\"", createBody);
    }

    [Fact]
    public async Task Reasoner_tells_model_it_has_no_native_tools()
    {
        string body = null;
        var handler = new StubHandler(async request =>
        {
            if (request.RequestUri.AbsolutePath == "/api/session")
                return Json("""{"data":{"id":"ses_r"}}""");
            body = await request.Content.ReadAsStringAsync();
            return Json("""{"data":{"text":"ok"}}""");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var reasoner = new OpenCodeReasoner(client, new OpenCodeModelRef("opencode", "m"), Path.GetTempPath());

        var result = await reasoner.CompleteAsync([new LlmMessage("user", "继续")]);

        Assert.Equal("ok", result.Content);
        Assert.Contains("Native tool calling is disabled", body);
        Assert.Contains("print the tool call as text", body);
    }

    [Fact]
    public async Task Reasoner_retries_once_when_text_is_empty()
    {
        int generates = 0;
        var bodies = new List<string>();
        var handler = new StubHandler(async request =>
        {
            if (request.RequestUri.AbsolutePath == "/api/session")
                return Json("""{"data":{"id":"ses_e"}}""");
            if (!request.RequestUri.AbsolutePath.EndsWith("/generate"))
                return Json("{}");
            generates++;
            bodies.Add(await request.Content.ReadAsStringAsync());
            return Json(generates == 1 ? """{"data":{"text":""}}""" : """{"data":{"text":"ok"}}""");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var reasoner = new OpenCodeReasoner(client, new OpenCodeModelRef("opencode", "m"), Path.GetTempPath());

        var result = await reasoner.CompleteAsync([new LlmMessage("user", "hi")]);

        Assert.Equal("ok", result.Content);
        Assert.Equal(2, generates);
        Assert.Contains("Native tool calling is disabled", bodies[0]);
        Assert.Contains("NO tools in this session", bodies[1]);
    }

    [Fact]
    public async Task List_models_maps_free_and_tools()
    {
        const string json = """
        {"data":[
          {"providerID":"opencode","modelID":"a","name":"A","capabilities":{"tools":true},"limit":{"context":1000},
           "cost":[{"input":0,"output":0}]},
          {"providerID":"teamorouter","modelID":"b","name":"b free","capabilities":{"tools":false},
           "cost":[{"input":1,"output":2}]}
        ]}
        """;
        var handler = new StubHandler(_ => Json(json));
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);

        var models = await client.ListModelsAsync();

        Assert.Equal(2, models.Count);
        Assert.True(models[0].IsFree);
        Assert.True(models[0].SupportsTools);
        Assert.Equal(1000, models[0].Context);
        Assert.False(models[1].IsFree);
        Assert.Equal("opencode/b@teamorouter", OpenCodeModelId.Format(models[1].Ref));
    }

    [Fact]
    public async Task Probe_reports_unauthorized_and_incompatible()
    {
        var endpoint = new OpenCodeEndpoint { Url = "http://127.0.0.1:1234", Password = "x", Pid = 7 };

        var unauthorized = await OpenCodeEndpoint.ProbeAsync(endpoint, Http(_ => Json("{}", 401)), default);
        var old = await OpenCodeEndpoint.ProbeAsync(endpoint,
            Http(_ => Json("""{"version":"1.9.0","pid":7}""")), default);
        var ready = await OpenCodeEndpoint.ProbeAsync(endpoint,
            Http(_ => Json("""{"version":"2.2.2","pid":7}""")), default);

        Assert.Equal(OpenCodeStatus.Unauthorized, unauthorized.Status);
        Assert.Equal(OpenCodeStatus.Incompatible, old.Status);
        Assert.Equal(OpenCodeStatus.Ready, ready.Status);
    }

    [Fact]
    public void Service_file_is_read_without_leaking_password()
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, """{"url":"http://127.0.0.1:9","password":"s3cret","pid":42,"version":"2.2.2"}""");
        try
        {
            var endpoint = OpenCodeEndpoint.FromFile(path);

            Assert.Equal("http://127.0.0.1:9", endpoint.Url);
            Assert.Equal("s3cret", endpoint.Password);
            Assert.Equal(42, endpoint.Pid);
            Assert.DoesNotContain("s3cret", endpoint.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Task_tool_returns_session_text_and_evidence()
    {
        string createBody = null;
        var handler = new StubHandler(request => request.RequestUri.AbsolutePath switch
        {
            "/api/session" => Create(request, body => createBody = body, "ses_1"),
            "/api/session/ses_1/prompt" => Json("{}"),
            "/api/session/ses_1/permission" => Json("""{"data":[]}"""),
            "/api/session/ses_1" => Json(
                """{"data":{"id":"ses_1","time":{"created":1,"idle":2},"cost":0.5,"tokens":{"input":10,"output":3},"outcome":"succeeded"}}"""),
            "/api/session/ses_1/message" => Json("""
            {"data":[{"type":"assistant","content":[
              {"type":"text","text":"done"},
              {"type":"tool","name":"bash","state":{},"time":{"created":1}}
            ]}]}
            """),
            "/api/session/ses_1/diff" => Json("""
            {"data":[{"file":"src/a.cs","patch":"","additions":3,"deletions":1,"status":"modified"}]}
            """),
            _ => Json("{}", 404)
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var tool = new OpenCodeTaskTool(() => client, Path.GetTempPath());

        var result = await tool.ExecuteAsync(
            new ToolArgs(new Dictionary<string, object> { ["task"] = "do it" }), default);

        Assert.True(result.Ok);
        Assert.StartsWith("done", result.Data);
        Assert.Contains("ses_1", result.Data);
        Assert.Contains("bash", result.Data);
        Assert.Contains("0.5", result.Data);
        Assert.Contains("src/a.cs(+3/-1)", result.Data);
        Assert.Contains("\"effect\":\"ask\"", createBody);
    }

    [Fact]
    public async Task Create_session_omits_permissions_when_disabled()
    {
        string body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{"data":{"id":"ses_x"}}""");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);

        await client.CreateSessionAsync("t", Path.GetTempPath(), askAllPermissions: false);

        Assert.DoesNotContain("permissions", body);
    }

    [Fact]
    public async Task Task_tool_fails_fast_when_permission_is_pending_without_approver()
    {
        var handler = new StubHandler(request => request.RequestUri.AbsolutePath switch
        {
            "/api/session" => Json("""{"data":{"id":"ses_2"}}"""),
            "/api/session/ses_2/prompt" => Json("{}"),
            "/api/session/ses_2/permission" => Json("""{"data":[{"id":"perm_1","action":"edit","resources":["a.cs"]}]}"""),
            _ => Json("{}")
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var tool = new OpenCodeTaskTool(() => client, Path.GetTempPath());

        var result = await tool.ExecuteAsync(
            new ToolArgs(new Dictionary<string, object> { ["task"] = "do it" }), default);

        Assert.False(result.Ok);
        Assert.Contains("审批", result.Error);
    }

    [Fact]
    public async Task Task_tool_bridges_approval_to_once()
    {
        int polls = 0;
        string reply = null;
        var handler = new StubHandler(request => request.RequestUri.AbsolutePath switch
        {
            "/api/session" => Json("""{"data":{"id":"ses_3"}}"""),
            "/api/session/ses_3/prompt" => Json("{}"),
            "/api/session/ses_3/permission" => ++polls == 1
                ? Json("""{"data":[{"id":"perm_1","action":"edit","resources":["a.cs"]}]}""")
                : Json("""{"data":[]}"""),
            "/api/session/ses_3/permission/perm_1/reply" => Capture(request, body => reply = body),
            "/api/session/ses_3" => Json(
                """{"data":{"id":"ses_3","time":{"created":1,"idle":2},"outcome":"succeeded"}}"""),
            "/api/session/ses_3/message" => Json("""{"data":[{"type":"assistant","content":[{"type":"text","text":"done"}]}]}"""),
            _ => Json("{}")
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var tool = new OpenCodeTaskTool(
            () => client, Path.GetTempPath(), approve: _ => ApprovalDecision.AllowOnce);

        var result = await tool.ExecuteAsync(
            new ToolArgs(new Dictionary<string, object> { ["task"] = "do it" }), default);

        Assert.True(result.Ok);
        Assert.Contains("\"decision\":\"once\"", reply);
    }

    [Fact]
    public async Task Task_tool_rejects_when_user_denies()
    {
        string reply = null;
        var handler = new StubHandler(request => request.RequestUri.AbsolutePath switch
        {
            "/api/session" => Json("""{"data":{"id":"ses_4"}}"""),
            "/api/session/ses_4/prompt" => Json("{}"),
            "/api/session/ses_4/permission" => Json("""{"data":[{"id":"perm_1","action":"bash","resources":["rm -rf x"]}]}"""),
            "/api/session/ses_4/permission/perm_1/reply" => Capture(request, body => reply = body),
            _ => Json("{}")
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var tool = new OpenCodeTaskTool(
            () => client, Path.GetTempPath(), approve: _ => ApprovalDecision.Deny);

        var result = await tool.ExecuteAsync(
            new ToolArgs(new Dictionary<string, object> { ["task"] = "do it" }), default);

        Assert.False(result.Ok);
        Assert.Contains("拒绝", result.Error);
        Assert.Contains("\"decision\":\"reject\"", reply);
    }

    private static HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage> handle)
        => new(new StubHandler(request => Task.FromResult(handle(request))));

    private static HttpResponseMessage Json(string body, int status = 200)
        => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Capture(HttpRequestMessage request, Action<string> capture)
    {
        capture(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
        return Json("{}");
    }

    private static HttpResponseMessage Create(HttpRequestMessage request, Action<string> capture, string sessionID)
    {
        capture(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
        return Json("{\"data\":{\"id\":\"" + sessionID + "\"}}");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handle;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
            : this(request => Task.FromResult(handle(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle)
        {
            _handle = handle;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => _handle(request);
    }
}
