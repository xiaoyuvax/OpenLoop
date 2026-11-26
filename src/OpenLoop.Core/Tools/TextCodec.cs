using System.Globalization;
using System.Text;

namespace OpenLoop.Core.Tools
{
    /// <summary>
    /// 文本编码判定：BOM 优先 → 严格 UTF-8 → 系统 ANSI（中文 Windows 即 GBK/936）。
    /// 读工具的「解码」、写工具的「原样写回」、子进程输出共用这一份判定：按 UTF-8 硬读 GBK 文件会整篇乱码，
    /// 拿乱码再写回去等于改坏文件；子进程既可能按系统码页输出，也可能被调用方设成 UTF-8，
    /// 事先固定一种解码必然把另一种弄乱。
    /// </summary>
    public static class TextCodec
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Encoding Utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);

        //静态字段初始化器先于静态构造函数执行：直接在字段里调 ResolveAnsi 会在 RegisterProvider 之前跑，
        //GetEncoding(936) 抛异常被吞掉、Ansi 永远是 null（测试进程里别的代码先注册过才碰巧通过）。
        //所以这里用 Lazy 推迟到第一次真正取用，注册一定已经发生。
        private static readonly Lazy<Encoding> AnsiLazy =
            new(ResolveAnsi, LazyThreadSafetyMode.ExecutionAndPublication);

        static TextCodec()
        {
            // .NET 内置只有 UTF-8/UTF-16/ASCII/Latin1：GBK 等代码页要显式注册（共享框架里带着，不需要额外包）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>解码结果：Text 是内容；Encoding 是「原样写回」该用的编码（BOM 由编码自身的 Preamble 决定）。</summary>
        public readonly struct Decoded
        {
            public Decoded(string text, Encoding encoding, string name)
            {
                Text = text;
                Encoding = encoding;
                Name = name;
            }

            public string Text { get; }
            public Encoding Encoding { get; }

            /// <summary>给用户看的编码名，例如 <c>UTF-8</c> / <c>gb2312/936</c>。</summary>
            public string Name { get; }

            public bool IsUtf8 => Encoding.CodePage == Utf8NoBom.CodePage;
        }

        /// <summary>无 BOM 且不是合法 UTF-8 时按它解码（也用于把「编码未知」的子进程输出兜下来）。</summary>
        public static Encoding SystemAnsi => AnsiLazy.Value;

        public static Decoded Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new Decoded("", Utf8NoBom, "UTF-8");

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new Decoded(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3),
                    new UTF8Encoding(true), "UTF-8 BOM");

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return new Decoded(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2),
                    new UnicodeEncoding(false, true), "UTF-16LE BOM");

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return new Decoded(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2),
                    new UnicodeEncoding(true, true), "UTF-16BE BOM");

            if (TryStrictUtf8(bytes, out string utf8))
                return new Decoded(utf8, Utf8NoBom, "UTF-8");

            Encoding ansi = SystemAnsi;
            if (ansi != null)
                return new Decoded(ansi.GetString(bytes), ansi, $"{ansi.WebName}/{ansi.CodePage}");

            // 连 ANSI 都取不到（极少数平台）：退回替换字符，至少不抛
            return new Decoded(Utf8NoBom.GetString(bytes), Utf8NoBom, "UTF-8");
        }

        /// <summary>按 <paramref name="encoding"/> 写回，BOM 跟着编码走（UTF-8 无 BOM 编码就没有前导字节）。</summary>
        public static byte[] Encode(string text, Encoding encoding)
        {
            encoding ??= Utf8NoBom;
            byte[] body = encoding.GetBytes(text ?? "");
            byte[] head = encoding.GetPreamble();
            if (head.Length == 0) return body;

            byte[] all = new byte[head.Length + body.Length];
            head.CopyTo(all, 0);
            body.CopyTo(all, head.Length);
            return all;
        }

        /// <summary>无 BOM 却含 NUL 字节：按二进制处理，别把乱码当文本喂给模型。</summary>
        public static bool IsBinary(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;
            // UTF-16 的 BOM 后面必然有成对的 00，不能当成二进制
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) ||
                                      (bytes[0] == 0xFE && bytes[1] == 0xFF))) return false;
            return Array.IndexOf(bytes, (byte)0) >= 0;
        }

        private static bool TryStrictUtf8(byte[] bytes, out string text)
        {
            try
            {
                text = Utf8Strict.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = null;
                return false;
            }
        }

        /// <summary>系统 ANSI 代码页（zh-CN = 936）。取不到就返回 null，调用方退回 UTF-8。</summary>
        private static Encoding ResolveAnsi()
        {
            try
            {
                int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                if (codePage <= 0 || codePage == 65001) return null;
                return Encoding.GetEncoding(codePage);
            }
            catch
            {
                return null;
            }
        }
    }
}
