using OpenLoop.Service.Config;
using OpenLoop.Service.Models;

namespace OpenLoop.Service.Media
{
    /// <summary>
    /// 生图模型识别与同系列互指（实践来自 VeniceMan）：模型目录不区分「文生图 / 编辑」，
    /// 只能从 id 判断；同系列编辑模型按去掉变体后缀后的前缀匹配。
    /// </summary>
    public static class MediaModels
    {
        private static readonly string[] ImageMarkers =
        [
            "image", "imagen", "flux", "stable-diffusion", "sdxl", "sd3", "diffusion",
            "dall-e", "dalle", "seedream", "gpt-image", "qwen-image", "grok-imagine",
            "ideogram", "recraft", "nano-banana"
        ];

        private static readonly string[] EditMarkers = ["-edit", "_edit", "inpaint", "kontext", "-edit-"];

        /// <summary>模型 id 是否用于出图（含编辑变体）。</summary>
        public static bool IsImageModel(string id)
        {
            string leaf = Leaf(id);
            return leaf.Length > 0 && ImageMarkers.Any(m => leaf.Contains(m, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsEditModel(string id)
        {
            string leaf = Leaf(id);
            return IsImageModel(id) && EditMarkers.Any(m => leaf.Contains(m, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>同系列基名：去掉提供商前缀与变体后缀，用于「文生图 ↔ 编辑」互指。</summary>
        public static string Family(string id)
        {
            string leaf = Leaf(id);
            foreach (string suffix in new[] { "-text-to-image", "_t2i", "-t2i", "-image-preview", "-image", "-preview", "-edit", "_edit", "-inpaint", "-kontext" })
            {
                if (leaf.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    leaf = leaf[..^suffix.Length];
                    break;
                }
            }
            return leaf;
        }

        /// <summary>
        /// 把用户选的模型解析成能执行编辑的模型：本身就是编辑模型 → 原样；
        /// 同系列有编辑变体 → 用它（如 seedream-v5-pro → seedream-v5-pro-edit）；
        /// 没有独立变体的统一模型（gemini-*-image / gpt-image）→ 原样，它们自带编辑能力。
        /// </summary>
        public static string ResolveEditModel(string requested, IEnumerable<string> available)
        {
            if (string.IsNullOrWhiteSpace(requested)) return null;
            string id = requested.Trim();
            if (IsEditModel(id)) return id;

            string family = Family(id);
            if (family.Length == 0) return null;
            foreach (string candidate in available ?? [])
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                if (IsEditModel(candidate)
                    && string.Equals(Family(candidate), family, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            // 统一模型（文生图与编辑走同一 id）
            return IsImageModel(id) && !LooksDedicatedT2i(id) ? id : null;
        }

        /// <summary>带 -t2i / text-to-image 后缀的是纯文生图端点，不能直接拿去编辑。</summary>
        private static bool LooksDedicatedT2i(string id)
            => id.Contains("text-to-image", StringComparison.OrdinalIgnoreCase)
               || id.Contains("t2i", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 附件数量上限：服务商接口不报这个值，按模型类别给通用值。
        /// 生图/编辑模型只需要少量参考图，文本模型允许多份资料。
        /// </summary>
        public static int AttachmentLimit(string model)
            => IsImageModel(model) ? 4 : 8;

        /// <summary>当前可用模型里的生图模型（提供商清单 + OpenCode 目录 + 免费目录），按 id 去重。</summary>
        public static List<ModelInfo> ImageModels(AppSettings settings)
        {
            var result = new List<ModelInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string id, string name, bool free)
            {
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id) || !IsImageModel(id)) return;
                result.Add(new ModelInfo { Id = id, Name = name ?? id, IsFree = free, SupportsTools = true });
            }

            foreach (var provider in settings?.Providers ?? [])
            {
                foreach (string id in provider.Models ?? [])
                {
                    var known = ModelCatalog.All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                    Add(id, known?.Name ?? id, known?.IsFree ?? LooksFree(id));
                }
            }
            foreach (var model in OpenCodeHub.Models)
                Add(model.Id, model.Name, model.IsFree);
            foreach (var model in ModelCatalog.All)
                Add(model.Id, model.Name, model.IsFree);

            return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool LooksFree(string id)
            => id.Contains("free", StringComparison.OrdinalIgnoreCase);

        private static string Leaf(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            string value = id.Trim();
            int at = value.LastIndexOf('@');
            if (at > 0) value = value[..at];
            int slash = value.LastIndexOf('/');
            if (slash >= 0) value = value[(slash + 1)..];
            return value;
        }
    }
}
