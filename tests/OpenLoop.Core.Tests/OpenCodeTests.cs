using System.Net;
using System.Net.Http;
using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.OpenCode;
using OpenLoop.Core.Reasoning;
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
    public async Task Turn_denies_native_tools_and_rejects_stray_permissions()
    {
        var calls = new List<string>();
        string createBody = null;
        string reply = null;
        int permissionPolls = 0;
        var handler = new StubHandler(async request =>
        {
            string path = request.RequestUri.AbsolutePath;
            calls.Add(request.Method.Method + " " + path);
            if (path == "/api/event") return Sse("");
            if (path == "/api/session")
            {
                createBody = await request.Content.ReadAsStringAsync();
                return Json("""{"data":{"id":"ses_t"}}""");
            }
            if (path == "/api/session/ses_t/prompt") return Json("{}");
            if (path == "/api/session/ses_t/permission")
                return ++permissionPolls == 1
                    ? Json("""{"data":[{"id":"perm_1","action":"shell","resources":["echo x"]}]}""")
                    : Json("""{"data":[]}""");
            if (path == "/api/session/ses_t/permission/perm_1/reply")
            {
                reply = await request.Content.ReadAsStringAsync();
                return Json("{}");
            }
            if (path == "/api/session/ses_t")
                return Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"succeeded","cost":0.1}}""");
            if (path == "/api/session/ses_t/message") return Json("""
            {"data":[{"type":"assistant","content":[
              {"type":"reasoning","text":"想一想"},
              {"type":"text","text":"tool-call: {\"tool\":\"read_file\",\"args\":{\"path\":\"a\"}}"}
            ]}]}
            """);
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "secret", handler);
        var reasoner = new OpenCodeReasoner(client, new OpenCodeModelRef("teamorouter", "m"), Path.GetTempPath());

        var result = await reasoner.CompleteAsync(
            [new LlmMessage("system", "规则"), new LlmMessage("user", "干活")]);

        Assert.Contains("read_file", result.Content);
        Assert.Equal("想一想", result.Reasoning);
        Assert.Contains("\"effect\":\"deny\"", createBody);
        Assert.Contains("\"providerID\":\"teamorouter\"", createBody);
        Assert.Contains("\"decision\":\"reject\"", reply);
        Assert.Contains("POST /api/session/ses_t/prompt", calls);
        Assert.Contains("DELETE /api/session/ses_t", calls);
        Assert.DoesNotContain(calls, call => call.EndsWith("/generate"));
    }

    [Fact]
    public async Task Turn_keeps_tool_table_for_free_models()
    {
        string createBody = null;
        var handler = new StubHandler(async request =>
        {
            string path = request.RequestUri.AbsolutePath;
            if (path == "/api/event") return Sse("");
            if (path == "/api/session")
            {
                createBody = await request.Content.ReadAsStringAsync();
                return Json("""{"data":{"id":"ses_free"}}""");
            }
            if (path == "/api/session/ses_free/prompt") return Json("{}");
            if (path == "/api/session/ses_free/permission") return Json("""{"data":[]}""");
            if (path == "/api/session/ses_free")
                return Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"succeeded"}}""");
            if (path == "/api/session/ses_free/message")
                return Json("""{"data":[{"type":"assistant","content":[{"type":"text","text":"ok"}]}]}""");
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var reasoner = new OpenCodeReasoner(
            client, new OpenCodeModelRef("opencode", "m"), Path.GetTempPath(), keepTools: true);

        var result = await reasoner.CompleteAsync([new LlmMessage("user", "hi")]);

        Assert.Equal("ok", result.Content);
        Assert.Contains("\"effect\":\"ask\"", createBody);
    }

    [Fact]
    public async Task Free_model_native_calls_follow_host_approval()
    {
        string workspace = Path.GetTempPath();
        string inside = Path.Combine(workspace, "a.cs");
        var replies = new Dictionary<string, string>();
        int permissionPolls = 0;
        var handler = new StubHandler(async request =>
        {
            string path = request.RequestUri.AbsolutePath;
            if (path == "/api/event") return Sse("");
            if (path == "/api/session") return Json("""{"data":{"id":"ses_scope"}}""");
            if (path == "/api/session/ses_scope/prompt") return Json("{}");
            if (path == "/api/session/ses_scope/permission")
                return ++permissionPolls == 1
                    ? Json("{\"data\":["
                           + "{\"id\":\"p_in\",\"action\":\"read\",\"resources\":[\"" + inside.Replace("\\", "\\\\") + "\"]},"
                           + "{\"id\":\"p_out\",\"action\":\"read\",\"resources\":[\"C:\\\\outside\\\\b.cs\"]},"
                           + "{\"id\":\"p_shell\",\"action\":\"shell\",\"resources\":[\"C:\\\\outside\"]}]}")
                    : Json("""{"data":[]}""");
            if (path.StartsWith("/api/session/ses_scope/permission/") && path.EndsWith("/reply"))
            {
                string id = path.Split('/')[5];
                replies[id] = await request.Content.ReadAsStringAsync();
                return Json("{}");
            }
            if (path == "/api/session/ses_scope")
                return Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"succeeded"}}""");
            if (path == "/api/session/ses_scope/message")
                return Json("""{"data":[{"type":"assistant","content":[{"type":"text","text":"ok"}]}]}""");
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var reasoner = new OpenCodeReasoner(
            client, new OpenCodeModelRef("opencode", "m"), workspace,
            approve: _ => ApprovalDecision.AllowOnce, keepTools: true);

        await reasoner.CompleteAsync([new LlmMessage("user", "hi")]);

        Assert.Contains("\"decision\":\"once\"", replies["p_in"]);
        Assert.Contains("\"decision\":\"once\"", replies["p_out"]);
        Assert.Contains("\"decision\":\"once\"", replies["p_shell"]);
    }

    [Fact]
    public async Task Free_model_without_approval_rejects_native_calls_with_protocol_hint()
    {
        string reply = null;
        int permissionPolls = 0;
        var handler = new StubHandler(async request =>
        {
            string path = request.RequestUri.AbsolutePath;
            if (path == "/api/event") return Sse("");
            if (path == "/api/session") return Json("""{"data":{"id":"ses_free"}}""");
            if (path == "/api/session/ses_free/prompt") return Json("{}");
            if (path == "/api/session/ses_free/permission")
                return ++permissionPolls == 1
                    ? Json("""{"data":[{"id":"perm_1","action":"shell","resources":["C:\\outside"]}]}""")
                    : Json("""{"data":[]}""");
            if (path == "/api/session/ses_free/permission/perm_1/reply")
            {
                reply = await request.Content.ReadAsStringAsync();
                return Json("{}");
            }
            if (path == "/api/session/ses_free")
                return Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"succeeded"}}""");
            if (path == "/api/session/ses_free/message")
                return Json("""{"data":[{"type":"assistant","content":[{"type":"text","text":"ok"}]}]}""");
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var reasoner = new OpenCodeReasoner(
            client, new OpenCodeModelRef("opencode", "m"), Path.GetTempPath(), keepTools: true);

        var result = await reasoner.CompleteAsync([new LlmMessage("user", "hi")]);

        Assert.Equal("ok", result.Content);
        Assert.NotNull(reply);
        Assert.Contains("\"decision\":\"reject\"", reply);
        Assert.Contains("tool-call", reply);
    }

    [Fact]
    public async Task Turn_streams_native_tool_calls_to_observer()
    {
        var seen = new List<NativeToolCall>();
        var handler = new StubHandler(async request =>
        {
            string path = request.RequestUri.AbsolutePath;
            if (path == "/api/event") return Sse("");
            if (path == "/api/session") return Json("""{"data":{"id":"ses_n"}}""");
            if (path == "/api/session/ses_n/prompt") return Json("{}");
            if (path == "/api/session/ses_n/permission") return Json("""{"data":[]}""");
            if (path == "/api/session/ses_n")
                return Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"succeeded"}}""");
            if (path == "/api/session/ses_n/message")
                return Json("""
                {"data":[{"type":"assistant","content":[
                  {"type":"text","text":"ok"},
                  {"type":"tool","id":"call_1","name":"shell","state":{"status":"completed","input":{"command":"dir"},"content":[{"type":"text","text":"listing"}]},"time":{"created":1000,"ran":1200,"completed":2200}},
                  {"type":"tool","id":"call_2","name":"read","state":{"status":"running","input":{}}}
                ]}]}
                """);
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var reasoner = new OpenCodeReasoner(client, new OpenCodeModelRef("opencode", "m"), Path.GetTempPath());
        reasoner.OnNativeTool = seen.Add;

        await reasoner.CompleteAsync([new LlmMessage("user", "hi")]);

        var native = Assert.Single(seen);
        Assert.Equal("shell", native.Tool);
        Assert.Contains("dir", native.Input);
        Assert.True(native.Ok);
        Assert.Equal("listing", native.Output);
        Assert.Equal("completed", native.Status);
        // 执行耗时取 ran→completed（created 到 ran 之间是排队/审批）
        Assert.Equal(1000, native.DurationMs);
    }

    [Fact]
    public async Task Turn_returns_native_history_in_chronological_order()
    {
        int prompts = 0;
        var handler = new StubHandler(async request =>
        {
            string path = request.RequestUri.AbsolutePath;
            if (path == "/api/event") return Sse("");
            if (path == "/api/session") return Json("""{"data":{"id":"ses_h"}}""");
            if (path == "/api/session/ses_h/prompt") { prompts++; return Json("{}"); }
            if (path == "/api/session/ses_h/permission") return Json("""{"data":[]}""");
            if (path == "/api/session/ses_h")
                return Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"succeeded"}}""");
            if (path == "/api/session/ses_h/message")
                // 接口按最新在前返回；第二条（较旧）才是原生调用
                return Json("""
                {"data":[
                  {"type":"idle"},
                  {"type":"assistant","content":[{"type":"text","text":"最终答复"}]},
                  {"type":"user"},
                  {"type":"assistant","content":[
                    {"type":"tool","id":"t1","name":"read","state":{"status":"completed","input":{"path":"a.cs"},"content":[{"type":"text","text":"FILE CONTENT"}]}},
                    {"type":"text","text":"先读一下"}
                  ]}
                ]}
                """);
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var reasoner = new OpenCodeReasoner(
            client, new OpenCodeModelRef("opencode", "m"), Path.GetTempPath(), keepTools: true);

        var result = await reasoner.CompleteAsync([new LlmMessage("user", "hi")]);

        Assert.Equal("最终答复", result.Content);
        Assert.True(result.NativeToolSucceeded);
        // 有原生结果就不该再整轮重发
        Assert.Equal(1, prompts);
        Assert.Collection(result.ExtraMessages,
            m =>
            {
                Assert.Equal("assistant", m.Role);
                Assert.Contains("\"tool\":\"native:read\"", m.Content);
                Assert.Contains("\"args\":{\"path\":\"a.cs\"}", m.Content);
            },
            m =>
            {
                Assert.Equal("user", m.Role);
                Assert.Contains("FILE CONTENT", m.Content);
            },
            m =>
            {
                Assert.Equal("assistant", m.Role);
                Assert.Equal("先读一下", m.Content);
            });
    }

    [Fact]
    public void Protocol_sits_right_after_system_prompt()
    {
        string text = OpenCodeReasoner.Flatten(
            [new LlmMessage("system", "规则"), new LlmMessage("user", "干活")]);

        int system = text.IndexOf("[system]", StringComparison.Ordinal);
        int protocol = text.IndexOf("Native tool calling is disabled", StringComparison.Ordinal);
        int user = text.IndexOf("[user]", StringComparison.Ordinal);

        Assert.True(system >= 0 && protocol > system && protocol < user);
        Assert.Contains("[user]\n干活", text);
    }

    [Fact]
    public async Task Reasoner_retries_once_when_text_is_empty()
    {
        int turns = 0;
        var handler = new StubHandler(async request =>
        {
            string path = request.RequestUri.AbsolutePath;
            if (path == "/api/event") return Sse("");
            if (path == "/api/session") return Json("""{"data":{"id":"ses_e"}}""");
            if (path == "/api/session/ses_e/prompt")
            {
                turns++;
                return Json("{}");
            }
            if (path == "/api/session/ses_e")
                return Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"succeeded"}}""");
            if (path == "/api/session/ses_e/message")
                return Json(turns == 1
                    ? """{"data":[{"type":"assistant","content":[]}]}"""
                    : """{"data":[{"type":"assistant","content":[{"type":"text","text":"ok"}]}]}""");
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);
        var reasoner = new OpenCodeReasoner(client, new OpenCodeModelRef("opencode", "m"), Path.GetTempPath());

        var result = await reasoner.CompleteAsync([new LlmMessage("user", "hi")]);

        Assert.Equal("ok", result.Content);
        Assert.Equal(2, turns);
    }

    [Fact]
    public async Task Turn_surfaces_execution_failure_instead_of_empty_text()
    {
        string sse = string.Join("\n",
            """data: {"type":"session.execution.failed","data":{"sessionID":"ses_f","error":{"type":"provider.free-limit","message":"Free usage limit reached."}}}""") + "\n";

        var handler = new StubHandler(async request => request.RequestUri.AbsolutePath switch
        {
            "/api/event" => Sse(sse),
            "/api/session" => Json("""{"data":{"id":"ses_f"}}"""),
            "/api/session/ses_f/prompt" => Json("{}"),
            "/api/session/ses_f" => Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"failed"}}"""),
            "/api/session/ses_f/message" => Json("""{"data":[]}"""),
            _ => Json("{}")
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RunTurnAsync("hi", new OpenCodeModelRef("opencode", "m"), Path.GetTempPath()));

        Assert.Contains("provider.free-limit", ex.Message);
        Assert.Contains("Free usage limit reached.", ex.Message);
    }

    [Fact]
    public async Task Turn_streams_reasoning_and_text_deltas_of_its_own_session()
    {
        var deltas = new List<string>();
        var reasoning = new List<string>();
        string sse = string.Join("\n",
            """data: {"type":"session.reasoning.delta","data":{"sessionID":"ses_s","delta":"想"}}""",
            """data: {"type":"session.text.delta","data":{"sessionID":"ses_s","delta":"答"}}""",
            """data: {"type":"session.text.delta","data":{"sessionID":"other","delta":"别人的"}}""",
            """data: {"type":"session.execution.succeeded","data":{"sessionID":"ses_s"}}""") + "\n";

        var handler = new StubHandler(async request => request.RequestUri.AbsolutePath switch
        {
            "/api/event" => Sse(sse),
            "/api/session" => Json("""{"data":{"id":"ses_s"}}"""),
            "/api/session/ses_s/prompt" => Json("{}"),
            "/api/session/ses_s" => Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"succeeded"}}"""),
            "/api/session/ses_s/message" => Json(
                """{"data":[{"type":"assistant","content":[{"type":"text","text":"答"}]}]}"""),
            _ => Json("{}")
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);

        var turn = await client.RunTurnAsync(
            "hi", new OpenCodeModelRef("opencode", "m"), Path.GetTempPath(),
            delta => { lock (deltas) deltas.Add(delta); },
            piece => { lock (reasoning) reasoning.Add(piece); });

        Assert.Equal("答", turn.Text);
        Assert.Equal(["答"], deltas);
        Assert.Equal(["想"], reasoning);
    }

    [Fact]
    public async Task Turn_keeps_waiting_while_output_still_grows()
    {
        int polls = 0;
        var handler = new StubHandler(request =>
        {
            string path = request.RequestUri.AbsolutePath;
            if (path == "/api/event") return Sse("");
            if (path == "/api/session") return Json("""{"data":{"id":"ses_slow"}}""");
            if (path == "/api/session/ses_slow/prompt") return Json("{}");
            if (path == "/api/session/ses_slow/permission") return Json("""{"data":[]}""");
            if (path == "/api/session/ses_slow/message")
            {
                // 每轮多一个字：跑够 10 轮（约 7 秒）也不该被 5 秒的空闲窗口砍掉
                int n = Interlocked.Increment(ref polls);
                return Json("""{"data":[{"type":"assistant","content":[{"type":"text","text":" """
                    + new string('a', n) + "\"}]}]}");
            }
            if (path == "/api/session/ses_slow")
                return polls < 10
                    ? Json("""{"data":{"time":{"created":1}}}""")
                    : Json("""{"data":{"time":{"created":1,"idle":2},"outcome":"succeeded"}}""");
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);

        var turn = await client.RunTurnAsync(
            "hi", new OpenCodeModelRef("opencode", "m"), Path.GetTempPath(), timeoutSeconds: 5);

        Assert.True(polls >= 10);
        Assert.True(turn.Text.Length > 0);
    }

    [Fact]
    public async Task Turn_gives_up_when_nothing_moves_for_the_idle_window()
    {
        var handler = new StubHandler(request => request.RequestUri.AbsolutePath switch
        {
            "/api/event" => Sse(""),
            "/api/session" => Json("""{"data":{"id":"ses_stuck"}}"""),
            "/api/session/ses_stuck" => Json("""{"data":{"time":{"created":1}}}"""),
            "/api/session/ses_stuck/message" => Json("""{"data":[]}"""),
            "/api/session/ses_stuck/permission" => Json("""{"data":[]}"""),
            _ => Json("{}")
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => client.RunTurnAsync(
            "hi", new OpenCodeModelRef("opencode", "m"), Path.GetTempPath(), timeoutSeconds: 5));

        Assert.Contains("没有任何输出", ex.Message);
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
    public async Task Sweep_deletes_only_sessions_whose_owner_process_is_gone()
    {
        var deleted = new List<string>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string list = """
        {"data":[
          {"id":"ses_dead","title":"OpenLoop turn","outcome":null,"metadata":{"openloopPid":999999},"time":{"updated":1,"busy":1}},
          {"id":"ses_own_old","title":"OpenLoop turn","outcome":null,"metadata":{"openloopPid":__PID__},"time":{"updated":1}},
          {"id":"ses_mine","title":"OpenLoop turn","outcome":null,"metadata":{"openloopPid":__PID__},"time":{"updated":__NOW__}},
          {"id":"ses_legacy","title":"OpenLoop turn","outcome":null,"time":{"updated":__NOW__}},
          {"id":"ses_user","title":"我的会话","outcome":null,"time":{"updated":1}}
        ]}
        """.Replace("__PID__", Environment.ProcessId.ToString())
           .Replace("__NOW__", now.ToString());
        var handler = new StubHandler(request =>
        {
            string path = request.RequestUri.AbsolutePath;
            if (path == "/api/session") return Json(list);
            if (path.StartsWith("/api/session/ses_")) deleted.Add(request.Method + " " + path);
            return Json("{}");
        });
        using var client = new OpenCodeClient("http://127.0.0.1:1234", "", handler);

        int removed = await client.SweepStaleSessionsAsync();

        Assert.Equal(2, removed);
        // 忙着的先打断再删；本进程不再使用、以及主人进程已退出的都要清
        Assert.Contains("POST /api/session/ses_dead/interrupt", deleted);
        Assert.Contains("DELETE /api/session/ses_dead", deleted);
        Assert.Contains("DELETE /api/session/ses_own_old", deleted);
        // 正在用的、刚建好的、别人的、非本标题的一律不碰
        Assert.DoesNotContain(deleted, d => d.Contains("ses_mine"));
        Assert.DoesNotContain(deleted, d => d.Contains("ses_legacy"));
        Assert.DoesNotContain(deleted, d => d.Contains("ses_user"));
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

        await client.CreateSessionAsync("t", Path.GetTempPath(), permissionEffect: null);

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

    private static HttpResponseMessage Sse(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

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
