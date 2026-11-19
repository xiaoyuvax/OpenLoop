using System.Text.Json;
using System.Text.Json.Nodes;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Plugins
{
    /// <summary>
    /// 插件目录的增删改查：WinForms 设置页与 Web 设置页共用这一份，
    /// 扫描规则与 <see cref="PluginCatalog.Load"/> 一致（工作区优先、同名去重、无效清单跳过）。
    /// 只管清单与 key.txt，不托管插件进程。
    /// </summary>
    public static class PluginStore
    {
        public static string GlobalRoot => PluginCatalog.GlobalRoot;

        /// <summary>新建插件写到哪里：有工作区写工作区，没有就写全局。</summary>
        public static string RootFor(string workspace)
            => string.IsNullOrWhiteSpace(workspace)
                ? GlobalRoot
                : Path.Combine(workspace, ".openloop", "plugins");

        /// <summary>卸载不是直接删：整个目录搬到这儿，需要时捞得回来。</summary>
        public static string TrashRoot => Path.Combine(AppPaths.DataRoot, "plugins-trash");

        /// <summary>列表里的一行；可编辑的那几个字段是原始清单值（<c>{root}</c> 不展开）。</summary>
        public sealed class Row
        {
            public string Name { get; set; }
            public string Id { get; set; }
            public string Version { get; set; }
            public string Kind { get; set; }
            public string From { get; set; }
            public string Directory { get; set; }
            public int Providers { get; set; }
            public int Models { get; set; }
            public string BaseUrl { get; set; }
            public string ModelsUrl { get; set; }
            public string ModelPrefix { get; set; }
        }

        /// <summary>保存用的那一行。</summary>
        public sealed class Item
        {
            /// <summary>空表示新建（目录名取 Id）。</summary>
            public string Directory { get; set; }
            public string Id { get; set; }
            public string Name { get; set; }
            public string BaseUrl { get; set; }
            public string ModelsUrl { get; set; }
            public string ModelPrefix { get; set; }
            /// <summary>空表示不动现有 key.txt。</summary>
            public string Key { get; set; }
            /// <summary>清单里声明了多少模型（只读，给对话框说明用）。</summary>
            public int Models { get; set; }

            /// <summary>清单里声明的模型 id；null 表示不改 models（编辑对话框）。</summary>
            public List<string> ModelList { get; set; }

            /// <summary>contributions.providers 里的下标（默认第一个）。</summary>
            public int ProviderIndex { get; set; }
            public string ProviderId { get; set; }
        }

        public static List<Row> List(string workspace)
        {
            var rows = new List<Row>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (root, from) in Roots(workspace))
            {
                if (!Directory.Exists(root)) continue;

                List<string> dirs;
                try
                {
                    dirs = [.. Directory.EnumerateDirectories(root)];
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"插件目录读不了，已跳过 {root}：{ex.Message}");
                    continue;
                }

                foreach (string dir in dirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var manifest = PluginManifest.TryLoad(dir);
                    if (manifest == null)
                    {
                        AppLog.Warn("插件清单无效：" + dir);
                        continue;
                    }
                    if (!seen.Add(manifest.Id)) continue;

                    var items = ReadProviders(dir);
                    var item = items.FirstOrDefault() ?? new Item { Directory = dir };
                    int providers = manifest.Contributions?.Providers?.Count ?? 0;
                    rows.Add(new Row
                    {
                        Name = string.IsNullOrWhiteSpace(item.Name) ? manifest.Id : item.Name.Trim(),
                        Id = manifest.Id,
                        Version = manifest.Version ?? "",
                        Kind = Describe(manifest),
                        From = from,
                        Directory = dir,
                        Providers = providers,
                        Models = items.Sum(i => i.Models),
                        BaseUrl = item.BaseUrl,
                        ModelsUrl = item.ModelsUrl,
                        ModelPrefix = item.ModelPrefix
                    });
                }
            }

            return rows;
        }

        /// <summary>读清单里 providers[0] 的原始字段（编辑对话框用）。</summary>
        public static Item ReadRaw(string dir) => ReadProviders(dir).FirstOrDefault() ?? new Item { Directory = dir };

        /// <summary>读清单里全部 provider 的原始字段（模型页按插件提供商逐项管理）。</summary>
        public static List<Item> ReadProviders(string dir)
        {
            var items = new List<Item>();
            try
            {
                var doc = Parse(File.ReadAllText(Path.Combine(dir, "plugin.json")));
                string pluginId = doc?["id"]?.ToString() ?? "";
                if (doc?["contributions"]?["providers"] is not JsonArray providers) return items;

                for (int i = 0; i < providers.Count; i++)
                {
                    if (providers[i] is not JsonObject provider) continue;
                    items.Add(new Item
                    {
                        Directory = dir,
                        Id = pluginId,
                        ProviderIndex = i,
                        ProviderId = provider["id"]?.ToString() ?? "",
                        Name = provider["name"]?.ToString() ?? "",
                        BaseUrl = provider["baseUrl"]?.ToString() ?? "",
                        ModelsUrl = provider["modelsUrl"]?.ToString() ?? "",
                        ModelPrefix = provider["modelPrefix"]?.ToString() ?? "",
                        Models = (provider["models"] as JsonArray)?.Count ?? 0,
                        ModelList = (provider["models"] as JsonArray)?
                            .Select(node => node?.ToString())
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .ToList() ?? []
                    });
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"插件清单读不了 {dir}：{ex.Message}");
            }
            return items;
        }

        /// <summary>新建或更新 provider 型插件清单（按 ProviderIndex 只动那一个 provider，别的字段原样写回）；返回写入的目录。</summary>
        public static string Save(string workspace, Item item)
        {
            if (item == null) throw new InvalidOperationException("缺少插件内容");

            string id = (item.Id ?? "").Trim();
            if (id.Length == 0) throw new InvalidOperationException("插件 id 不能为空");
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException("插件 id 会当目录名用，不能含路径分隔符等字符");

            string dir;
            if (string.IsNullOrWhiteSpace(item.Directory))
            {
                dir = Path.Combine(RootFor(workspace), id);
                if (Directory.Exists(dir))
                    throw new InvalidOperationException($"{dir} 已存在，换个 id 或去列表里编辑它");
            }
            else
            {
                dir = Checked(item.Directory, workspace)
                      ?? throw new InvalidOperationException("只能改工作区或全局插件目录下的插件");
            }

            string file = Path.Combine(dir, "plugin.json");
            var doc = File.Exists(file) ? Parse(File.ReadAllText(file)) : Template();
            var provider = ProviderAt(doc, item.ProviderIndex) ?? throw new InvalidOperationException("清单里没有这个 provider");

            doc["id"] = id;
            if (string.IsNullOrWhiteSpace(provider["id"]?.ToString())) provider["id"] = id;
            Set(provider, "name", item.Name);
            Set(provider, "baseUrl", item.BaseUrl);
            Set(provider, "modelsUrl", item.ModelsUrl);
            Set(provider, "modelPrefix", item.ModelPrefix);

            if (item.ModelList != null)
            {
                var models = new JsonArray();
                foreach (string modelId in item.ModelList
                             .Where(m => !string.IsNullOrWhiteSpace(m))
                             .Select(m => m.Trim())
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                    models.Add(modelId);
                provider["models"] = models;
            }

            string key = (item.Key ?? "").Trim();
            if (key.Length > 0)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "key.txt"), key);
                provider["keyFile"] = "{root}/key.txt";
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(file, doc.ToJsonString(Write));
            return dir;
        }

        /// <summary>卸载：把目录整个搬进 <see cref="TrashRoot"/>，不是直接抹掉。</summary>
        public static void Remove(string workspace, string dir)
        {
            string full = Checked(dir, workspace)
                          ?? throw new InvalidOperationException("只能卸载工作区或全局插件目录下的插件");
            if (!Directory.Exists(full)) throw new InvalidOperationException("目录不存在：" + full);

            Directory.CreateDirectory(TrashRoot);
            string target = Path.Combine(TrashRoot,
                Path.GetFileName(full) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Move(full, target);
        }

        // ---------------- 内部 ----------------

        private static readonly JsonSerializerOptions Write = new() { WriteIndented = true };
        private static readonly JsonDocumentOptions Read = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static IEnumerable<(string Root, string From)> Roots(string workspace)
        {
            if (!string.IsNullOrWhiteSpace(workspace))
                yield return (Path.Combine(workspace, ".openloop", "plugins"), "工作区");
            yield return (GlobalRoot, "全局");
        }

        /// <summary>目录必须是某一处插件根的直接子目录，否则不许改/删（防止越界写盘）。</summary>
        private static string Checked(string dir, string workspace)
        {
            if (string.IsNullOrWhiteSpace(dir)) return null;

            string full;
            try { full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar); }
            catch { return null; }

            foreach (var (root, _) in Roots(workspace))
            {
                string fullRoot;
                try { fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); }
                catch { continue; }
                if (string.Equals(Path.GetDirectoryName(full), fullRoot, StringComparison.OrdinalIgnoreCase))
                    return full;
            }
            return null;
        }

        private static void Move(string from, string to)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException)
            {
                // 插件根和回收目录不在同一个卷上时 Directory.Move 会拒绝：拷完再删
            }

            Copy(from, to);
            Directory.Delete(from, true);
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.EnumerateFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
            foreach (string dir in Directory.EnumerateDirectories(from))
                Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        private static JsonObject Parse(string text)
            => JsonNode.Parse(text, null, Read) as JsonObject
               ?? throw new InvalidOperationException("plugin.json 顶层不是对象");

        private static JsonObject ProviderAt(JsonObject doc, int index)
        {
            if (doc?["contributions"] is not JsonObject contributions) return null;
            if (contributions["providers"] is not JsonArray { Count: > 0 } providers) return null;
            if (index <= 0) return providers[0] as JsonObject;
            return index < providers.Count ? providers[index] as JsonObject : null;
        }

        private static JsonObject Template() => new()
        {
            ["id"] = "",
            ["version"] = "1.0.0",
            ["contributions"] = new JsonObject
            {
                ["providers"] = new JsonArray(new JsonObject
                {
                    ["id"] = "",
                    ["name"] = "",
                    ["baseUrl"] = "",
                    ["models"] = new JsonArray()
                })
            }
        };

        private static void Set(JsonObject target, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) target.Remove(name);
            else target[name] = value.Trim();
        }

        private static string Describe(PluginManifest manifest)
        {
            var parts = new List<string>();
            int providers = manifest.Contributions?.Providers?.Count ?? 0;
            int tools = manifest.Contributions?.Tools?.Count ?? 0;
            if (providers > 0) parts.Add($"模型 {providers}");
            if (tools > 0) parts.Add($"工具 {tools}");
            if (manifest.Mcp != null) parts.Add("MCP");
            if (manifest.Service != null) parts.Add("自带服务");
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }
    }
}
