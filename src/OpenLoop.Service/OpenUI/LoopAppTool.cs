using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Config;
using OpenLoop.Service.Web;

namespace OpenLoop.Service.OpenUI
{
    /// <summary>
    /// agent 侧工具：建/开/迭代 LoopApp。页面 ↔ 会话的双向通道由壳里的消息桥负责，
    /// 这个工具只管「落盘 + 让浏览器显示 + 读回结果」。
    /// </summary>
    public sealed class LoopAppTool : ITool
    {
        private readonly string _workspace;
        private readonly string _owner;
        private readonly ILoopAppHost _host;

        public LoopAppTool(string workspace, string owner, ILoopAppHost host)
        {
            _workspace = workspace ?? "";
            _owner = owner ?? "";
            _host = host;
        }

        public string Name => "LoopApp";

        public string Description =>
            "在壳的内嵌浏览器里即时生成/迭代界面（LoopApp）。create 建目录并复制运行时、open 打开、" +
            "eval 在页面里执行 JS、reload 重新加载（改完文件用这个，不要用 location.reload()）、shot 截图、drain 取页面攒下的事件、" +
            "list 列已有应用。页面用 ui.submit 把数据作为一条用户消息送回本会话。" +
            "只在需要用户填表/看表/反复调视觉时用；一次问答用 AskUser。";

        public string ParamsSchema =>
            "action: create|open|eval|reload|shot|drain|state|list|undock|dock|interact（必填）; " +
            "name: string（应用名，create/open 必填）; title?: string（显示标题）; " +
            "js?: string（eval 必填）; enabled?: bool（interact，默认 true）";

        public bool IsReadOnly => false;

        public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string action = (args.Str("action") ?? "").Trim().ToLowerInvariant();
            string name = args.Str("name")?.Trim();

            try
            {
                switch (action)
                {
                    case "create": return Create(name, args.Str("title"));
                    case "list": return List();
                    case "state": return Ok(await _host.StateAsync(_owner, ct));
                    case "open": return await OpenAppAsync(name, ct);
                    case "eval":
                        {
                            string js = args.Str("js");
                            if (string.IsNullOrWhiteSpace(js)) return AgentToolResult.Failure("eval 需要 js");
                            return Ok(await _host.EvalAsync(_owner, js, ct));
                        }
                    case "drain":
                        return Ok(await _host.EvalAsync(_owner,
                            "JSON.stringify(window.__openui ? window.__openui.events : [])", ct));
                    case "reload": return Ok(await _host.ReloadAsync(_owner, ct));
                    case "shot": return Ok(await _host.ShotAsync(_owner, ct));
                    case "undock": return Ok(await _host.UndockAsync(_owner, ct));
                    case "dock": return Ok(await _host.DockAsync(_owner, ct));
                    case "interact": return Ok(await _host.InteractAsync(_owner, args.Str("enabled") != "false", ct));
                    default:
                        return AgentToolResult.Failure(
                            $"未知 action：{action}（create/open/eval/reload/shot/drain/state/list/undock/dock/interact）");
                }
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure("LoopApp 失败：" + ex.Message);
            }
        }

        private AgentToolResult Create(string name, string title)
        {
            if (string.IsNullOrWhiteSpace(name)) return AgentToolResult.Failure("create 需要 name");

            string skill = LoopAppPaths.FindSkill(_workspace, AppSettings.GlobalSkillsPath);
            if (skill == null)
                return AgentToolResult.Failure(
                    "找不到 openui 技能：把它装进技能库（或用 OPENLOOP_OPENUI_SKILL 指向技能目录）");

            string dir = LoopAppPaths.Create(_workspace, name, skill, title, out string error);
            if (dir == null) return AgentToolResult.Failure(error);

            return AgentToolResult.Success(
                $"已创建：{dir}\n入口：{LoopAppPaths.Url(name)}\n" +
                "改 index.html / app.js 后用 LoopApp(reload) 重新加载");
        }

        /// <summary>
        /// 列本工作区的 LoopApp。给出绝对路径：agent 拿到名字之后第一件事就是找文件，
        /// 只给 slug 它会去猜 `.openloop/apps` 之类的路径，白跑好几次 Shell（实测）。
        /// </summary>
        private AgentToolResult List()
        {
            var apps = LoopAppPaths.List(_workspace);
            if (apps.Count == 0)
                return AgentToolResult.Success(
                    $"还没有 LoopApp。\n落盘根：{LoopAppPaths.Root(_workspace)}\n" +
                    "用 LoopApp(create, name) 建一个");

            var sb = new StringBuilder("本工作区的 LoopApp：\n");
            foreach (var app in apps)
                sb.Append("- ").Append(app.Slug).Append("  「").Append(app.Title).Append("」\n")
                  .Append("  目录：").Append(app.Directory).Append('\n')
                  .Append("  改这个：").Append(Path.Combine(app.Directory, "app.js")).Append('\n');
            sb.Append("落盘根：").Append(LoopAppPaths.Root(_workspace));
            return AgentToolResult.Success(sb.ToString());
        }

        private async Task<AgentToolResult> OpenAppAsync(string name, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(name)) return AgentToolResult.Failure("open 需要 name");
            string dir = LoopAppPaths.For(_workspace, name);
            if (!File.Exists(Path.Combine(dir, "index.html")))
                return AgentToolResult.Failure($"还没有这个 LoopApp：{name}（先 create）");
            return Ok(await _host.OpenAsync(_owner, LoopAppPaths.Url(name), ct));
        }

        private static AgentToolResult Ok(string result)
            => AgentToolResult.Success(string.IsNullOrWhiteSpace(result) ? "完成" : result);
    }

    /// <summary>工具暴露：有桌面壳、且工作区里装了 openui 技能时才把 LoopApp 摆给 agent。</summary>
    public static class LoopAppTools
    {
        public static IEnumerable<ITool> For(string workspace, string owner)
        {
            if (string.IsNullOrWhiteSpace(owner)) return [];
            if (string.IsNullOrWhiteSpace(PuppetBridge.ShellEndpoint) && PuppetBridge.SelfRole != "shell") return [];
            return [new LoopAppTool(workspace, owner, new ShellLoopAppHost(owner))];
        }
    }
}
