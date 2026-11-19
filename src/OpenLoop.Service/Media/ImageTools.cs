using System.Text.RegularExpressions;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Config;

namespace OpenLoop.Service.Media
{
    /// <summary>
    /// 给当前会话注入的生图/图编辑工具。模型必须传「主题文件名 + 时间戳」，
    /// 落盘到工作区 .openloop/tmp/images，并把提示词写进 EXIF。
    /// </summary>
    public static class ImageTools
    {
        public static IEnumerable<ITool> For(string workspace, AppSettings settings, string selectedModel,
            IReadOnlyList<string> attachments)
        {
            var available = MediaModels.ImageModels(settings ?? AppSettings.Load()).Select(m => m.Id).ToList();
            if (available.Count == 0) return [];

            string model = MediaModels.IsImageModel(selectedModel) ? selectedModel : available[0];
            return
            [
                new GenerateTool(workspace, settings ?? AppSettings.Load(), model, available),
                new EditTool(workspace, settings ?? AppSettings.Load(), model, available, attachments ?? [])
            ];
        }

        private sealed class GenerateTool : ITool
        {
            private readonly string _workspace;
            private readonly AppSettings _settings;
            private readonly string _model;
            private readonly IReadOnlyList<string> _available;

            public GenerateTool(string workspace, AppSettings settings, string model, IReadOnlyList<string> available)
            {
                _workspace = workspace;
                _settings = settings;
                _model = model;
                _available = available;
            }

            public string Name => "image_generate";
            public string Description =>
                "文生图：按提示词调用图像模型生成一张图片，保存到工作区 .openloop/tmp/images 并写入 EXIF（提示词、模型）。"
                + "filename 必须是「与画面主体相关的词 + 时间戳」（如 白鹿_20261119_153000），不要用纯数字或单字符。";
            public string ParamsSchema =>
                "prompt: string（完整提示词）；filename: string（主题文件名，含时间戳，不需扩展名）；modelId?: string（可选，默认用户当前选择的生图模型）";
            public bool IsReadOnly => false;

            public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
            {
                string prompt = args.Str("prompt");
                if (string.IsNullOrWhiteSpace(prompt)) return AgentToolResult.Failure("缺少 prompt 参数");
                string name = Names.Normalize(args.Str("filename"), out string nameError);
                if (name == null) return AgentToolResult.Failure(nameError);

                string model = Resolve(args.Str("modelId"), _model, _available, edit: false);
                var result = await ImageClient.GenerateAsync(_settings, model, prompt, null, ct);
                return Save(_workspace, model, prompt, name, result);
            }
        }

        private sealed class EditTool : ITool
        {
            private readonly string _workspace;
            private readonly AppSettings _settings;
            private readonly string _model;
            private readonly IReadOnlyList<string> _available;
            private readonly IReadOnlyList<string> _attachments;

            public EditTool(string workspace, AppSettings settings, string model, IReadOnlyList<string> available,
                IReadOnlyList<string> attachments)
            {
                _workspace = workspace;
                _settings = settings;
                _model = model;
                _available = available;
                _attachments = attachments;
            }

            public string Name => "image_edit";
            public string Description =>
                "图像编辑：以用户上传的图片（默认附件）或工作区内图片路径为源图，按提示词重绘/修改，结果同 image_generate 落盘并写 EXIF。"
                + "用户发了图片附件时优先用它；没有附件时才用 path/paths。";
            public string ParamsSchema =>
                "prompt: string（编辑指令）；filename: string（主题文件名，含时间戳）；path?: string（源图路径）；paths?: string[]（多张源图）；modelId?: string（可选）";
            public bool IsReadOnly => false;

            public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
            {
                string prompt = args.Str("prompt");
                if (string.IsNullOrWhiteSpace(prompt)) return AgentToolResult.Failure("缺少 prompt 参数");
                string name = Names.Normalize(args.Str("filename"), out string nameError);
                if (name == null) return AgentToolResult.Failure(nameError);

                var sources = new List<string>();
                string one = args.Str("path");
                if (!string.IsNullOrWhiteSpace(one)) sources.Add(ResolvePath(one));
                foreach (string path in args.StrList("paths")) sources.Add(ResolvePath(path));
                if (sources.Count == 0) sources.AddRange(_attachments);
                sources = sources.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).Distinct().ToList();
                if (sources.Count == 0)
                    return AgentToolResult.Failure("缺少源图：请用户上传图片附件，或提供工作区内的图片路径");

                string model = Resolve(args.Str("modelId"), _model, _available, edit: true);
                if (model == null)
                    return AgentToolResult.Failure($"没有可用于编辑的模型（当前选择 {_model}，也没有同系列编辑模型）");

                var result = await ImageClient.GenerateAsync(_settings, model, prompt, sources, ct);
                return Save(_workspace, model, prompt, name, result);
            }

            private string ResolvePath(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                string value = path.Trim();
                if (File.Exists(value)) return value;
                string combined = Path.Combine(_workspace, value);
                return File.Exists(combined) ? combined : value;
            }
        }

        private static string Resolve(string requested, string selected, IReadOnlyList<string> available, bool edit)
        {
            string wanted = string.IsNullOrWhiteSpace(requested) ? selected : requested.Trim();
            if (!edit) return MediaModels.IsImageModel(wanted) ? wanted : available[0];
            return MediaModels.ResolveEditModel(wanted, available);
        }

        private static AgentToolResult Save(string workspace, string model, string prompt, string name,
            ImageClient.Result result)
        {
            if (result?.Data == null || result.Data.Length == 0)
                return AgentToolResult.Failure("生图失败：" + (result?.Error ?? "接口未返回图像"));

            string dir = Path.Combine(workspace, ".openloop", "tmp", "images");
            Directory.CreateDirectory(dir);
            string ext = result.Data.Length > 3 && result.Data[0] == 0xFF && result.Data[1] == 0xD8 ? ".jpg" : ".png";
            string path = Unique(Path.Combine(dir, name + ext));
            File.WriteAllBytes(path, result.Data);
            ImageExif.Write(path, prompt, model);

            return AgentToolResult.Success(
                $"已保存（模型 {model}）：{path}\n尺寸/文件：{result.Data.Length / 1024} KB\n提示词已写入 EXIF");
        }

        private static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path) ?? ".";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
        }

        private static class Names
        {
            /// <summary>主题词 + 时间戳：纯数字/短名直接拒绝，缺时间戳就补当前时间。</summary>
            public static string Normalize(string raw, out string error)
            {
                error = null;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    error = "缺少 filename：请用与画面主体相关的词 + 时间戳命名";
                    return null;
                }

                var chars = raw.Trim()
                    .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)
                    .ToArray();
                string value = new string(chars).Trim('.', ' ');
                if (value.Length < 2 || value.All(char.IsDigit))
                {
                    error = "filename 太笼统：请用与画面主体相关的词（可带时间戳），不要用纯数字或单字符";
                    return null;
                }

                string withoutExt = Path.GetFileNameWithoutExtension(value);
                if (!Regex.IsMatch(withoutExt, "\\d{8}|\\d{12,}"))
                    withoutExt += "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                return withoutExt.Length <= 80 ? withoutExt : withoutExt[..80];
            }
        }
    }
}
