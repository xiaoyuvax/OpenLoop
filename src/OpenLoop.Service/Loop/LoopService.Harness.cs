using System.Text.Json;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Store;
using OpenLoop.Service.Config;

namespace OpenLoop.Service.Loop
{
    /// <summary>
    /// LoopService 的 Harness 配置面：工作区 Harness 配置窗体（技能/规则/限制/护栏四个页）的读写。
    /// 账本的写入口仍然只在这里——界面与 Web 都调 I，不自己写文件。
    /// </summary>
    public sealed partial class LoopService
    {
        /// <summary>
        /// 配置页的列表：某类对象当前是否在激活集里、生效参数与默认参数。
        /// 会确保账本存在（配置页本身就是写入口）。
        /// </summary>
        public IReadOnlyList<HarnessItem> ListObjects(string workspace, string kind)
        {
            var ledger = Ledger.Open(workspace);
            ledger.EnsureInitialized();
            if (string.Equals(kind, HarnessObjects.Skill, StringComparison.Ordinal))
            {
                // 技能要先发现：项目里新装的技能也得出现在这一页
                SkillCatalog.Sync(ledger,
                    SkillCatalog.Load(ledger.Workspace, AppSettings.GlobalSkillsPath), out _,
                    SkillStore.LoadDisabled(AppSettings.GlobalSkillsStatePath));
            }
            return HarnessObjects.List(ledger, kind);
        }

        /// <summary>
        /// 改一件对象：enable / disable / params（改参数）/ default（参数恢复默认）。
        /// 每次都写一个新的激活版本——激活集只追加，旧版本永远可回滚。
        /// 参数非法或对象不在激活集里则抛 InvalidOperationException（界面直接显示原因）。
        /// </summary>
        public int SetObject(string workspace, string kind, string id, string action, string paramsText)
        {
            var ledger = Ledger.Open(workspace);
            ledger.EnsureInitialized();
            string act = (action ?? "").Trim().ToLowerInvariant();

            if (string.Equals(kind, HarnessObjects.Skill, StringComparison.Ordinal))
            {
                var catalog = SkillCatalog.Load(ledger.Workspace, AppSettings.GlobalSkillsPath);
                var skill = catalog.FirstOrDefault(s => string.Equals(s.Name, id, StringComparison.OrdinalIgnoreCase));
                if (skill == null) throw new InvalidOperationException($"没有技能：{id}");
                if (act is not ("enable" or "disable"))
                    throw new InvalidOperationException("技能只有启用与停用两种操作");
                SkillCatalog.SetEnabled(ledger, catalog, skill.Name, act == "enable");
                return ledger.Current?.Version ?? 0;
            }

            var feature = ledger.FindFeature(id) ?? throw new InvalidOperationException($"没有这个对象：{id}");
            if (!string.Equals(feature.Kind, kind, StringComparison.Ordinal))
                throw new InvalidOperationException($"{id} 属于 {feature.Kind}，不是 {kind}");

            var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
            var current = ledger.Current;
            var refs = HarnessObjects.Replace(current, features, kind, []);
            var existing = (current?.Features ?? []).FirstOrDefault(r => r?.Id == id);

            bool keep = existing != null;
            Dictionary<string, JsonElement> parameters;
            switch (act)
            {
                case "disable":
                    keep = false;
                    parameters = existing?.Params;
                    break;
                case "enable":
                    keep = true;
                    parameters = existing?.Params;
                    break;
                case "params":
                    if (existing == null)
                        throw new InvalidOperationException("这个对象还没启用：先启用再改参数");
                    parameters = HarnessObjects.ParseParams(feature, paramsText, out string error);
                    if (parameters == null) throw new InvalidOperationException(error);
                    break;
                case "default":
                    parameters = null;   // 引用里不带参数 = 用特征里的默认值
                    keep = existing != null || HarnessObjects.Defaults(kind).Any(r => r?.Id == id);
                    break;
                default:
                    throw new InvalidOperationException($"不支持的操作：{action}");
            }

            if (keep)
            {
                refs.Add(new FeatureRef
                {
                    Id = id,
                    Params = parameters is { Count: > 0 } ? parameters : null
                });
            }

            if (refs.Count == 0) throw new InvalidOperationException("激活集不能空：至少留一件对象");
            return ledger.Activate(refs, "user", DescribeChange(kind, id, act)).Version;
        }

        /// <summary>
        /// 某一类整体恢复默认：规则/限制/护栏换回种子集里的同类条目；技能没有出厂默认。
        /// </summary>
        public int ResetKind(string workspace, string kind)
        {
            if (string.Equals(kind, HarnessObjects.Skill, StringComparison.Ordinal))
                throw new InvalidOperationException("技能没有出厂默认，只能逐个启用或停用");

            var ledger = Ledger.Open(workspace);
            ledger.EnsureInitialized();
            var defaults = HarnessObjects.Defaults(kind);
            if (defaults.Count == 0) throw new InvalidOperationException("这一类没有默认项");

            // 旧工作区的账本可能还没登记新的种子特征：先补登记，否则激活引用会指向不存在的 id
            var seeds = FeatureCatalog.SeedFeatures();
            foreach (string id in defaults.Select(r => r.Id))
                if (ledger.FindFeature(id) == null && seeds.FirstOrDefault(f => f.Id == id) is { } seed)
                    ledger.AppendFeature(seed);

            var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
            var refs = HarnessObjects.Replace(ledger.Current, features, kind, defaults);
            if (refs.Count == 0) throw new InvalidOperationException("激活集不能空：至少留一件对象");
            return ledger.Activate(refs, "user", $"恢复{KindName(kind)}默认值").Version;
        }

        private static string KindName(string kind) => kind switch
        {
            HarnessObjects.Skill => "技能",
            HarnessObjects.Rule => "规则",
            HarnessObjects.Limit => "限制",
            HarnessObjects.Guard => "护栏",
            _ => kind
        };

        private static string DescribeChange(string kind, string id, string action)
        {
            string verb = action switch
            {
                "enable" => "启用",
                "disable" => "停用",
                "params" => "改参数",
                "default" => "参数恢复默认",
                _ => action
            };
            return $"{verb}{KindName(kind)} {id}";
        }
    }
}
