using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Safety;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// 按锚点替换：把文件里的 old 换成 new，old 必须唯一命中。
    /// 存在的意义是不必为改几行而整文件覆盖——WriteFile 覆盖误伤源文件的事故都出在这里。
    /// </summary>
    public sealed class EditFileTool : ITool
    {
        private const long MaxBytes = 2 * 1024 * 1024;

        private readonly WorkspaceScope _scope;

        public EditFileTool(WorkspaceScope scope) => _scope = scope;

        public string Name => "EditFile";
        public string Description =>
            "改已有文件的一小段：把 old 原文替换成 new（old 必须在文件里恰好出现一次，否则不改动直接报错）。" +
            "大文件改几行一律用它，不要用 WriteFile 整文件重写。";
        public string ParamsSchema =>
            "path: string（相对工作目录的路径）; old: string（要替换的原文，带上足够上下文保证唯一）; " +
            "new: string（替换成什么，可为空=删掉这段）";
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string path = args.Str("path");
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(AgentToolResult.Failure("缺少参数 path"));

            if (!_scope.TryResolve(path, out string full, out string error))
                return Task.FromResult(AgentToolResult.Failure(error));

            if (!File.Exists(full))
                return Task.FromResult(AgentToolResult.Failure($"文件不存在：{path}（新建文件请用 WriteFile）"));

            string oldText = args.StrRaw("old");
            if (string.IsNullOrEmpty(oldText))
                return Task.FromResult(AgentToolResult.Failure(
                    "缺少参数 old（要替换的原文；整文件重写请用 WriteFile）"));
            string newText = args.StrRaw("new") ?? "";

            if (new FileInfo(full).Length > MaxBytes)
                return Task.FromResult(AgentToolResult.Failure($"文件过大（上限 {MaxBytes} 字节）：{path}"));

            //读-改-写整段持锁：并发写入下整文件回写会互相覆盖（TOCTOU）
            if (!FileWriteGate.TryEnter(full, out var lease, out string gateError))
                return Task.FromResult(AgentToolResult.Failure(gateError));
            using (lease)
                return Task.FromResult(ReplaceLocked(full, oldText, newText));
        }

        /// <summary>持锁内的读-改-写：文件在读取后被别的程序换过就不写，避免覆盖别人的改动。</summary>
        private AgentToolResult ReplaceLocked(string full, string oldText, string newText)
        {
            string text;
            Encoding encoding;
            bool crlf;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(full);
                var decoded = TextCodec.Decode(bytes);
                text = decoded.Text;
                encoding = decoded.Encoding;
                //ReadFile 是按行输出的、不带 \r，模型给的 old 多半用 \n；两边都归一后再匹配
                //shortcut: 按「全文是否含 CRLF」统一写回行尾，混用行尾的文件会被整体归一；要保留混合行尾再细化
                crlf = text.Contains("\r\n");
                text = text.Replace("\r\n", "\n");
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"读取失败：{ex.Message}");
            }

            string anchor = oldText.Replace("\r\n", "\n");
            int at = text.IndexOf(anchor, StringComparison.Ordinal);
            if (at < 0)
                return AgentToolResult.Failure(
                    $"old 没在 {_scope.Relative(full)} 里找到（未改动）。先 ReadFile 核对原文，注意缩进与空格。");

            int second = text.IndexOf(anchor, at + 1, StringComparison.Ordinal);
            if (second >= 0)
                return AgentToolResult.Failure(
                    $"old 在 {_scope.Relative(full)} 里出现多次（第 {LineOf(text, at)} 行、第 {LineOf(text, second)} 行起，" +
                    "未改动）；请在 old 里带上更多上下文让它唯一命中。");

            string updated = text[..at] + newText.Replace("\r\n", "\n") + text[(at + anchor.Length)..];
            if (crlf) updated = updated.Replace("\n", "\r\n");
            int line = LineOf(text, at);

            try
            {
                //Shell 命令与进程外编辑器不认上面的锁：内容变过就放弃，不覆盖别人的改动
                if (!File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes))
                    return AgentToolResult.Failure(
                        $"{_scope.Relative(full)} 在读取后被其他程序改写（未改动）。先 ReadFile 核对原文再改。");
                _scope.Undo?.Record(full);
                File.WriteAllBytes(full, Encode(updated, encoding));
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"写入失败：{ex.Message}");
            }

            return AgentToolResult.Success(
                $"已替换 第 {line} 行附近 {anchor.Length} 字符 → {newText.Length} 字符：{_scope.Relative(full)}");
        }

        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>编码判定与写回共用 <see cref="TextCodec"/>：只认 BOM 会把 GBK 文件当 UTF-8 读成乱码，
        /// 改完再按 UTF-8 写回等于把整个文件改坏。</summary>
        private static byte[] Encode(string text, Encoding encoding) => TextCodec.Encode(text, encoding);
    }
}
