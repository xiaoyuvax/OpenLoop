using System.Text;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests
{
    /// <summary>
    /// 编码兜底的回归：读 GBK 不再吐乱码、编辑 GBK 不把文件改坏、子进程两种输出编码都能读对。
    /// 这些是「能否删掉系统提示词里那条 PowerShell 编码警告」的前提，任何一条回归都会让提示词变成必须。
    /// </summary>
    public class EncodingFallbackTests : IDisposable
    {
        private readonly string _root;

        public EncodingFallbackTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "openloop-encoding", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static ToolArgs Args(params (string Key, object Value)[] pairs)
        {
            var dict = new Dictionary<string, object>();
            foreach (var (key, value) in pairs) dict[key] = value;
            return new ToolArgs(dict);
        }

        private static bool HasChineseAnsi => TextCodec.SystemAnsi?.CodePage == 936;

        [Fact]
        public void Utf8_without_bom_roundtrips_without_bom()
        {
            byte[] bytes = [0xE4, 0xB8, 0xAD, 0xE6, 0x96, 0x87];   // 中文

            var decoded = TextCodec.Decode(bytes);

            Assert.Equal("中文", decoded.Text);
            Assert.True(decoded.IsUtf8);
            Assert.Equal(bytes, TextCodec.Encode(decoded.Text, decoded.Encoding));
        }

        [Fact]
        public void Utf8_bom_and_utf16_bom_are_kept_on_write_back()
        {
            byte[] utf8Bom = [0xEF, 0xBB, 0xBF, 0xE4, 0xB8, 0xAD];
            byte[] utf16 = [0xFF, 0xFE, 0x2D, 0x4E];

            Assert.Equal("中", TextCodec.Decode(utf8Bom).Text);
            Assert.Equal(utf8Bom, TextCodec.Encode("中", TextCodec.Decode(utf8Bom).Encoding));
            Assert.Equal("中", TextCodec.Decode(utf16).Text);
            Assert.Equal(utf16, TextCodec.Encode("中", TextCodec.Decode(utf16).Encoding));
        }

        /// <summary>关键回归：GBK 不是合法 UTF-8，按 UTF-8 硬读会整篇乱码；必须走 ANSI 回退并原样写回。</summary>
        [Fact]
        public void Gbk_falls_back_to_ansi_and_writes_back_in_the_same_codepage()
        {
            if (!HasChineseAnsi) return;

            byte[] gbk = [0xD6, 0xD0, 0xCE, 0xC4];   // 中文

            var decoded = TextCodec.Decode(gbk);

            Assert.Equal("中文", decoded.Text);
            Assert.False(decoded.IsUtf8);
            Assert.Contains("936", decoded.Name);
            Assert.Equal(gbk, TextCodec.Encode(decoded.Text, decoded.Encoding));
        }

        [Fact]
        public void Binary_detected_by_nul_byte_but_utf16_bom_is_text()
        {
            Assert.True(TextCodec.IsBinary([0x50, 0x4B, 0x00, 0x04]));
            Assert.False(TextCodec.IsBinary([0xFF, 0xFE, 0x2D, 0x4E]));
            Assert.False(TextCodec.IsBinary(Encoding.UTF8.GetBytes("纯文本")));
        }

        [Fact]
        public async Task ReadFile_reads_gbk_without_mojibake_and_says_which_encoding()
        {
            if (!HasChineseAnsi) return;

            byte[] gbk = [0xB5, 0xDA, 0xD2, 0xBB, 0xD0, 0xD0, 0xA3, 0xBA, 0xD6, 0xD0, 0xCE, 0xC4];  // 第一行：中文
            File.WriteAllBytes(Path.Combine(_root, "gbk.txt"), gbk);

            var result = await new ReadFileTool(new WorkspaceScope(_root))
                .ExecuteAsync(Args(("path", "gbk.txt")), CancellationToken.None);

            Assert.True(result.Ok, result.Error);
            Assert.Contains("第一行：中文", result.Data);
            Assert.Contains("936", result.Data);      // 头部标出编码，模型才知道写回不能整文件转存
        }

        [Fact]
        public async Task ReadFile_says_nothing_about_encoding_for_plain_utf8()
        {
            File.WriteAllText(Path.Combine(_root, "utf8.txt"), "第一行\n第二行", new UTF8Encoding(false));

            var result = await new ReadFileTool(new WorkspaceScope(_root))
                .ExecuteAsync(Args(("path", "utf8.txt")), CancellationToken.None);

            Assert.True(result.Ok, result.Error);
            Assert.Contains("第一行", result.Data);
            Assert.DoesNotContain("编码", result.Data);
        }

        /// <summary>旧实现按 UTF-8 读 GBK 得到乱码，再按 UTF-8 写回，等于把整个文件改成乱码。</summary>
        [Fact]
        public async Task EditFile_keeps_gbk_bytes_intact_around_the_edit()
        {
            if (!HasChineseAnsi) return;

            byte[] before = [0xB5, 0xDA, 0xD2, 0xBB, 0xD0, 0xD0, 0x0D, 0x0A, 0xB5, 0xDA, 0xB6, 0xFE];  // 第一行\r\n第二
            File.WriteAllBytes(Path.Combine(_root, "gbk.txt"), before);

            var result = await new EditFileTool(new WorkspaceScope(_root)).ExecuteAsync(
                Args(("path", "gbk.txt"), ("old", "第一行"), ("new", "第三行")), CancellationToken.None);

            Assert.True(result.Ok, result.Error);
            byte[] expected = [0xB5, 0xDA, 0xC8, 0xFD, 0xD0, 0xD0, 0x0D, 0x0A, 0xB5, 0xDA, 0xB6, 0xFE];  // 第三行\r\n第二
            Assert.Equal(expected, File.ReadAllBytes(Path.Combine(_root, "gbk.txt")));
        }

        /// <summary>agent 按以前的提示词给 PowerShell 设 UTF-8 输出：旧实现按 936 解码，得到「涓枃」。</summary>
        [Fact]
        public async Task Shell_output_is_utf8_when_the_child_emits_utf8()
        {
            if (!OperatingSystem.IsWindows()) return;

            var argv = new[]
            {
                "powershell", "-NoProfile", "-Command",
                "[Console]::OutputEncoding=[Text.Encoding]::UTF8; '中文UTF8输出'"
            };

            var result = await ProcessRunner.RunAsync(argv, _root, 60, CancellationToken.None);

            Assert.True(result.Ok, result.Error);
            Assert.Contains("中文UTF8输出", result.Data);
        }

        /// <summary>不设 OutputEncoding 的默认 PowerShell 按 936 输出：防止修好一边坏了另一边。</summary>
        [Fact]
        public async Task Shell_output_is_ansi_when_the_child_emits_the_system_codepage()
        {
            if (!OperatingSystem.IsWindows() || !HasChineseAnsi) return;

            var argv = new[] { "powershell", "-NoProfile", "-Command", "'中文默认输出'" };

            var result = await ProcessRunner.RunAsync(argv, _root, 60, CancellationToken.None);

            Assert.True(result.Ok, result.Error);
            Assert.Contains("中文默认输出", result.Data);
        }
    }
}
