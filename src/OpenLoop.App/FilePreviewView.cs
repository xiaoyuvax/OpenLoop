using System.ComponentModel;
using System.Text;
using CDS.Markdown;
using FastColoredTextBoxNS;
using FastColoredTextBoxNS.Text;

namespace OpenLoop.App
{
    /// <summary>
    /// 右侧「文件」页：同时只显示一个文件，点别的路径就整体换内容。
    /// 按扩展名挑控件：md → MarkdownTextBox，rtf → RichTextBox，代码 → FCTB（能识别的语言顺手着色），其余 → 多行文本框。
    /// </summary>
    public partial class FilePreviewView : UserControl
    {
        /// <summary>超过这个大小的文件不预览：整份读进控件会把界面卡住。</summary>
        private const int MaxBytes = 4 * 1024 * 1024;

        private static readonly HashSet<string> CodeExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csx", ".csproj", ".props", ".targets", ".sln", ".slnx", ".xaml",
            ".js", ".mjs", ".ts", ".jsx", ".tsx", ".json", ".html", ".htm", ".xml",
            ".css", ".scss", ".sql", ".vb", ".php", ".lua", ".java",
            ".c", ".h", ".cpp", ".hpp", ".cc", ".go", ".rs", ".py", ".rb", ".kt", ".swift",
            ".sh", ".ps1", ".bat", ".cmd", ".yaml", ".yml", ".toml", ".ini", ".gradle",
            ".mk", ".diff", ".patch"
        };

        private Control _content;

        public FilePreviewView()
        {
            InitializeComponent();
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string FilePath { get; private set; } = "";

        /// <summary>换一个文件；相对路径按工作区解析。失败时返回原因，视图保持原样。</summary>
        public bool TryShowFile(string path, string workspace, out string error)
        {
            error = null;
            string full;
            try
            {
                full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(workspace ?? "", path));
            }
            catch (Exception ex)
            {
                error = "路径无效：" + ex.Message;
                return false;
            }
            if (!File.Exists(full))
            {
                error = "文件不存在：" + full;
                return false;
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(full); }
            catch (Exception ex)
            {
                error = "读取失败：" + ex.Message;
                return false;
            }
            if (bytes.Length > MaxBytes)
            {
                error = $"文件太大（{bytes.Length / 1024 / 1024.0:0.#} MB），不预览。";
                return false;
            }
            if (IsBinary(bytes))
            {
                error = "这是二进制文件，不预览。";
                return false;
            }

            string text;
            try
            {
                using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
                text = reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                error = "解码失败：" + ex.Message;
                return false;
            }

            FilePath = full;
            _lblPath.Text = full;
            SetContent(CreateContent(full, text));
            return true;
        }

        private void SetContent(Control content)
        {
            _host.SuspendLayout();
            try
            {
                _host.Controls.Clear();
                _content?.Dispose();
                _content = content;
                _host.Controls.Add(content);
            }
            finally
            {
                _host.ResumeLayout();
            }
        }

        private Control CreateContent(string path, string text)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".md" or ".markdown")
                return CreateMarkdown(text);

            if (ext == ".rtf")
            {
                var rich = new RichTextBox
                {
                    Name = "fileRtf",
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    BorderStyle = BorderStyle.None,
                    BackColor = UiStyle.Surface,
                    ForeColor = UiStyle.Text,
                    Font = new Font("Segoe UI", 10F),
                };
                try { rich.Rtf = text; }
                catch (ArgumentException) { rich.Text = text; }
                return rich;
            }

            if (CodeExts.Contains(ext))
            {
                var box = new FastColoredTextBox
                {
                    Name = "fileCode",
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    ShowLineNumbers = true,
                    WordWrap = false,
                    BackColor = UiStyle.Surface,
                    ForeColor = UiStyle.Text,
                    Font = new Font("Consolas", 10F),
                };
                // 先设语言再赋文本：setter 会按语言重建整段样式
                box.Language = LanguageDetector.StringToLanguage(LanguageKey(ext));
                box.Text = text;
                return box;
            }

            return new AntdUI.Input
            {
                Name = "fileText",
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = UiStyle.Surface,
                Font = new Font("Consolas", 10F),
                Text = text,
            };
        }

        /// <summary>
        /// MarkdownTextBox 只按自身高度渲染、不负责滚动：包一层可滚动面板，按可用宽度重算内容高度。
        /// </summary>
        private static Control CreateMarkdown(string text)
        {
            var host = new Panel
            {
                Name = "fileMarkdownHost",
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiStyle.Surface,
            };
            var markdown = new MarkdownTextBox
            {
                Name = "fileMarkdown",
                TabStop = false,
                BackColor = UiStyle.Surface,
            };
            host.Controls.Add(markdown);
            void Fit()
            {
                markdown.Width = Math.Max(100, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
                markdown.Height = Math.Max(host.ClientSize.Height, markdown.GetPreferredContentHeight());
            }
            host.Resize += (_, _) => Fit();
            markdown.SetMarkdown(text);
            Fit();
            return host;
        }

        /// <summary>FCTB 的语言名（少数扩展名映射到它能识别的方言，其余原样交给检测器）。</summary>
        private static string LanguageKey(string ext) => ext switch
        {
            ".csproj" or ".props" or ".targets" or ".sln" or ".slnx" or ".xaml" or ".xml" => "xml",
            ".ts" or ".tsx" or ".jsx" or ".mjs" => "js",
            ".java" => "java",
            _ => ext.TrimStart('.'),
        };

        private static bool IsBinary(byte[] bytes)
        {
            int count = Math.Min(bytes.Length, 8000);
            for (int i = 0; i < count; i++)
                if (bytes[i] == 0) return true;
            return false;
        }
    }
}
