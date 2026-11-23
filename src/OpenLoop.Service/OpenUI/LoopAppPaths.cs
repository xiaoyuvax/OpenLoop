using System.Text;

namespace OpenLoop.Service.OpenUI
{
    /// <summary>
    /// LoopApp 的落盘布局：<c>&lt;workspace&gt;/.openloop/tmp/LoopApps/&lt;slug&gt;/</c>。
    /// 运行时资产（openui.css/js/icons.svg）来自 openui 技能目录，create 时整份复制进来，应用自包含。
    /// 规范与资产在本仓库 <c>src/OpenUI/</c>（SKILL.md + references/runtime），随 OpenLoop 一起迭代。
    /// </summary>
    public static class LoopAppPaths
    {
        /// <summary>WebView2 虚拟域：映射到某个会话的 LoopApps 目录。</summary>
        public const string VirtualHost = "openui.local";

        /// <summary>LoopApp 落盘根：工作区里唯一放行给 Worker 写的 .openloop 子路径。</summary>
        public static string Root(string workspace)
            => Path.Combine(workspace ?? "", ".openloop", "tmp", "LoopApps");

        /// <summary>应用目录；名字里的非法字符换成下划线，其余（含中文）保留。</summary>
        public static string For(string workspace, string name)
            => Path.Combine(Root(workspace), Slug(name));

        /// <summary>目录名：保留字母数字与中文，空格换连字符，其余丢弃。</summary>
        public static string Slug(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c is ' ' or '\t') sb.Append('-');
            }
            string slug = sb.ToString().Trim('-');
            return slug.Length == 0 ? "app" : slug;
        }

        /// <summary>页面 URL（虚拟域 + 应用目录）。</summary>
        public static string Url(string name)
            => $"https://{VirtualHost}/{Slug(name)}/index.html";

        /// <summary>技能目录下的资产库位置。<c>references/runtime</c>（现用）；曾试过独立仓库 <c>OpenUI/runtime</c>，一并兼容，以后加新布局在这里登记。</summary>
        public static string FindRuntime(string skillDir)
        {
            if (string.IsNullOrWhiteSpace(skillDir)) return null;
            foreach (string rel in new[] { Path.Combine("references", "runtime"), Path.Combine("OpenUI", "runtime") })
            {
                string full = Path.Combine(skillDir, rel);
                if (Directory.Exists(full)) return Path.GetFullPath(full);
            }
            return null;
        }

        /// <summary>找 openui 技能的目录（规范与资产的来源）。找不到返回 null。</summary>
        public static string FindSkill(string workspace, string globalSkills)
        {
            string explicitPath = Environment.GetEnvironmentVariable("OPENLOOP_OPENUI_SKILL");
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(explicitPath)) candidates.Add(explicitPath);
            if (!string.IsNullOrWhiteSpace(workspace))
            {
                candidates.Add(Path.Combine(workspace, ".openloop", "skills", "openui"));
                candidates.Add(Path.Combine(workspace, ".opencode", "skills", "openui"));
            }
            if (!string.IsNullOrWhiteSpace(globalSkills)) candidates.Add(Path.Combine(globalSkills, "openui"));

            foreach (string dir in candidates)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                if (!File.Exists(Path.Combine(dir, "SKILL.md"))) continue;
                if (FindRuntime(dir) == null) continue;
                return Path.GetFullPath(dir);
            }
            return null;
        }

        /// <summary>
        /// 建应用目录：复制运行时、写 app.json 与起步骨架。
        /// 已存在时只补缺的文件——不覆盖 agent 已经写的 app.js。
        /// </summary>
        public static string Create(string workspace, string name, string skillRoot, string title, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(workspace)) { error = "工作目录为空"; return null; }
            if (string.IsNullOrWhiteSpace(skillRoot)) { error = "找不到 openui 技能（缺 SKILL.md 或资产库）"; return null; }
            string runtime = FindRuntime(skillRoot);
            if (runtime == null) { error = $"技能目录里没有资产库（references/runtime）：{skillRoot}"; return null; }

            string dir = For(workspace, name);
            try
            {
                Directory.CreateDirectory(dir);
                CopyRuntime(runtime, Path.Combine(dir, "runtime"));

                string display = string.IsNullOrWhiteSpace(title) ? Slug(name) : title;
                WriteIfMissing(Path.Combine(dir, "index.html"), IndexHtml(display));
                WriteIfMissing(Path.Combine(dir, "app.js"), AppJs(display));
                WriteIfMissing(Path.Combine(dir, "app.json"),
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        name = Slug(name),
                        title = display,
                        window = "dock",
                        size = new[] { 0, 0 }
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return dir;
            }
            catch (Exception ex)
            {
                error = "创建失败：" + ex.Message;
                return null;
            }
        }

        /// <summary>列本工作区已有的应用（只读，不建目录）。</summary>
        public static IReadOnlyList<(string Slug, string Title, string Directory)> List(string workspace)
        {
            var list = new List<(string, string, string)>();
            string root = Root(workspace);
            if (!Directory.Exists(root)) return list;

            foreach (string dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(Path.Combine(dir, "index.html"))) continue;
                string title = Path.GetFileName(dir);
                try
                {
                    string meta = Path.Combine(dir, "app.json");
                    if (File.Exists(meta))
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(meta));
                        if (doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String)
                            title = t.GetString() ?? title;
                    }
                }
                catch
                {
                    // app.json 坏了用目录名
                }
                list.Add((Path.GetFileName(dir), title, dir));
            }
            return list;
        }

        private static void CopyRuntime(string source, string target)
        {
            if (!Directory.Exists(source)) throw new InvalidOperationException("技能里没有 references/runtime");
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(source, file);
                string dest = Path.Combine(target, rel);
                string parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                // 运行时是框架资产：跟着技能走，每次 create 都刷新
                File.Copy(file, dest, overwrite: true);
            }
        }

        private static void WriteIfMissing(string path, string content)
        {
            if (File.Exists(path)) return;
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string IndexHtml(string title) => $"""
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>{title}</title>
<link rel="stylesheet" href="./runtime/openui.css">
</head>
<body class="ui">
<div class="page" id="app"></div>
<script src="./runtime/openui.js"></script>
<script src="./app.js"></script>
</body>
</html>
""";

        private static string AppJs(string title) => $$"""
// {{title}} —— 起步骨架：改这个文件，然后 LoopApp(reload) 重新加载
ui.ready(() => {
  ui.mount('#app', [
    ui.h('div.row.between', [
      ui.h('h1', { text: '{{title}}' }),
      ui.h('span.badge.accent', { text: 'LoopApp' })
    ]),
    ui.h('div.card', [
      ui.h('p.muted', { text: '把这个卡片换成真正的界面：表单、表格、看板、图表都行。' })
    ]),
    ui.h('div.row.end', [
      ui.h('button.btn.primary', { text: '提交', onclick: submit })
    ])
  ]);
});

async function submit(e) {
  e.target.disabled = true;
  try {
    await ui.submit({ ok: true, at: new Date().toISOString() });
    ui.toast('已提交', 'ok');
  } catch (err) {
    ui.toast('提交失败：' + err.message, 'err');
    e.target.disabled = false;
  }
}
""";
    }
}
