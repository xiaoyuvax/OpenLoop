using System.Text.Json;
using OpenLoop.Core.Acp;
using OpenLoop.Core.Messages;
using Xunit;

namespace OpenLoop.Core.Tests
{
    /// <summary>
    /// ACP 客户端 + 宿主：本地假代理（node 脚本）走一遍 initialize → session/new → prompt，
    /// 顺带验证权限请求按策略回答。没有 node 的机器跳过。
    /// </summary>
    public class DshAcpTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-acp-" + Guid.NewGuid().ToString("N"));
        private readonly string _script;
        private readonly string _record;

        public DshAcpTests()
        {
            Directory.CreateDirectory(_root);
            _script = Path.Combine(_root, "fake-acp.mjs");
            _record = Path.Combine(_root, "permission.json");
            File.WriteAllText(_script, FakeAgent);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static bool HasNode()
        {
            string name = OperatingSystem.IsWindows() ? "node.exe" : "node";
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                if (File.Exists(Path.Combine(dir.Trim('"'), name))) return true;
            }
            return false;
        }

        private DshAcpHost NewHost(bool allow)
            => new(OperatingSystem.IsWindows() ? "node.exe" : "node",
                [_script, _record], null, () => allow);

        [Fact]
        public async Task Models_come_from_the_acp_config_options()
        {
            if (!HasNode()) return;
            using var host = NewHost(true);
            await host.InitializeAsync(CancellationToken.None);

            var models = await host.ModelsAsync(_root, CancellationToken.None);

            Assert.Equal(["dsh/p/m1", "dsh/p/m2"], models.Select(m => m.Id));
            Assert.Equal("""["p","m1"]""", models[0].Value);
        }

        [Fact]
        public async Task Prompt_streams_chunks_and_returns_text()
        {
            if (!HasNode()) return;
            using var host = NewHost(true);
            await host.InitializeAsync(CancellationToken.None);

            var deltas = new List<string>();
            var thoughts = new List<string>();
            var result = await host.RunAsync(_root, "dsh/p/m2",
                [new LlmMessage("user", "say pong")], deltas.Add, thoughts.Add, CancellationToken.None);

            Assert.Equal("PONG", result.Content);
            Assert.Equal("thinking", result.Reasoning);
            Assert.Contains("PONG", string.Concat(deltas));
            Assert.Contains("thinking", string.Concat(thoughts));
        }

        [Fact]
        public async Task Permission_follows_the_host_policy()
        {
            if (!HasNode()) return;
            using var host = NewHost(false);
            await host.InitializeAsync(CancellationToken.None);

            await host.RunAsync(_root, null, [new LlmMessage("user", "do it")], null, null,
                CancellationToken.None);

            // 假代理把收到的权限结果写盘：应当是 reject
            var json = JsonDocument.Parse(File.ReadAllText(_record));
            Assert.Equal("selected", json.RootElement.GetProperty("outcome").GetString());
            Assert.Equal("reject_once", json.RootElement.GetProperty("optionId").GetString());
        }

        /// <summary>本地假 ACP 代理：固定两个模型，prompt 时先发 thinking/正文，再问一次权限。</summary>
        private const string FakeAgent = """
        import fs from 'node:fs';
        import readline from 'node:readline';

        const record = process.argv[2];
        const send = (o) => process.stdout.write(JSON.stringify(o) + '\n');
        const sessionId = 's1';
        let nextId = 100;
        const waiting = new Map();

        const model = {
          id: 'model', name: 'Model', type: 'select', category: 'model', currentValue: '["p","m1"]',
          options: [{ group: 'p', name: 'P', options: [
            { value: '["p","m1"]', name: 'M1' },
            { value: '["p","m2"]', name: 'M2' }
          ] }]
        };

        readline.createInterface({ input: process.stdin }).on('line', (line) => {
          let msg;
          try { msg = JSON.parse(line); } catch { return; }

          if (msg.id !== undefined && msg.method === undefined) {
            if (waiting.has(msg.id)) { waiting.get(msg.id)(msg); waiting.delete(msg.id); }
            return;
          }

          if (msg.method === 'initialize') {
            send({ jsonrpc: '2.0', id: msg.id, result: { protocolVersion: 1, agentInfo: { name: 'fake' }, agentCapabilities: {} } });
            return;
          }
          if (msg.method === 'session/new') {
            send({ jsonrpc: '2.0', id: msg.id, result: { sessionId, configOptions: [model] } });
            return;
          }
          if (msg.method === 'session/set_config_option') {
            send({ jsonrpc: '2.0', id: msg.id, result: { configOptions: [model] } });
            return;
          }
          if (msg.method === 'session/cancel') return;
          if (msg.method === 'session/prompt') {
            const update = (u) => send({ jsonrpc: '2.0', method: 'session/update', params: { sessionId, update: u } });
            update({ sessionUpdate: 'agent_thought_chunk', content: { type: 'text', text: 'thinking' } });

            const id = nextId++;
            const answer = new Promise((resolve) => waiting.set(id, resolve));
            send({ jsonrpc: '2.0', id, method: 'session/request_permission', params: {
              sessionId,
              toolCall: { toolCallId: 't1', title: 'write file', kind: 'edit' },
              options: [{ optionId: 'allow_once', kind: 'allow_once', name: 'Allow' },
                        { optionId: 'reject_once', kind: 'reject_once', name: 'Reject' }]
            } });

            answer.then((reply) => {
              fs.writeFileSync(record, JSON.stringify(reply.result.outcome));
              update({ sessionUpdate: 'agent_message_chunk', content: { type: 'text', text: 'PONG' } });
              send({ jsonrpc: '2.0', id: msg.id, result: { stopReason: 'end_turn' } });
            });
            return;
          }

          send({ jsonrpc: '2.0', id: msg.id, error: { code: -32601, message: 'no ' + msg.method } });
        });
        """;
    }
}
