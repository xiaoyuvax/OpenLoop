using System.Net;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class WebFetchToolTests
    {
        private static ToolArgs Url(string url)
            => new(new Dictionary<string, object> { ["url"] = url });

        [Theory]
        [InlineData("file:///etc/passwd")]
        [InlineData("ftp://example.com/a")]
        [InlineData("not a url")]
        public async Task RejectsNonHttpUrls(string url)
        {
            var tool = new WebFetchTool();
            var result = await tool.ExecuteAsync(Url(url), CancellationToken.None);

            Assert.False(result.Ok);
        }

        [Theory]
        [InlineData("http://127.0.0.1/")]
        [InlineData("http://localhost/")]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        [InlineData("http://192.168.1.1/")]
        [InlineData("http://10.0.0.1/")]
        [InlineData("http://[::1]/")]
        public async Task RejectsPrivateAndLoopbackTargets(string url)
        {
            var tool = new WebFetchTool();
            var result = await tool.ExecuteAsync(Url(url), CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Contains("内网", result.Error);
        }

        [Fact]
        public async Task RequiresApprovalWhenConfigured()
        {
            var tool = new WebFetchTool(approve: null, requireApproval: true);
            var result = await tool.ExecuteAsync(Url("http://example.com"), CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Contains("审批通道", result.Error);
        }

        [Fact]
        public async Task DeniedApprovalStopsBeforeNetwork()
        {
            var asked = 0;
            var tool = new WebFetchTool(_ => { asked++; return OpenLoop.Core.Safety.ApprovalDecision.Deny; },
                requireApproval: true);
            var result = await tool.ExecuteAsync(Url("http://example.com"), CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Equal(1, asked);
            Assert.Contains("拒绝", result.Error);
        }

        [Fact]
        public async Task CapsFetchesPerRun()
        {
            var tool = new WebFetchTool();
            for (int i = 0; i < 8; i++)
                await tool.ExecuteAsync(Url("http://127.0.0.1/"), CancellationToken.None);

            var result = await tool.ExecuteAsync(Url("http://127.0.0.1/"), CancellationToken.None);
            Assert.False(result.Ok);
            Assert.Contains("用满", result.Error);
        }

        [Theory]
        [InlineData("10.1.2.3", true)]
        [InlineData("172.16.0.1", true)]
        [InlineData("192.168.0.1", true)]
        [InlineData("169.254.1.1", true)]
        [InlineData("100.64.0.1", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("0.0.0.0", true)]
        [InlineData("224.0.0.1", true)]
        [InlineData("::1", true)]
        [InlineData("fc00::1", true)]
        [InlineData("8.8.8.8", false)]
        [InlineData("1.1.1.1", false)]
        public void ClassifiesAddresses(string ip, bool blocked)
            => Assert.Equal(blocked, WebFetchTool.IsBlocked(IPAddress.Parse(ip)));

        [Fact]
        public void ConvertsHtmlToPlainText()
        {
            const string html = """
                <html><head><title> Hello &amp; World </title>
                <style>body{color:red}</style></head>
                <body><script>alert('x')</script>
                <h1>Title</h1><p>First &lt;line&gt;</p><p>Second</p></body></html>
                """;

            Assert.Equal("Hello & World", WebFetchTool.TitleOf(html));

            string text = WebFetchTool.HtmlToText(html);
            Assert.DoesNotContain("alert", text);
            Assert.DoesNotContain("color:red", text);
            Assert.DoesNotContain("<p>", text);
            Assert.Contains("First <line>", text);
            Assert.Contains("Second", text);
        }

        [Theory]
        [InlineData("off", "off")]
        [InlineData("ASK", "ask")]
        [InlineData("allow", "allow")]
        [InlineData(null, "allow")]
        [InlineData("bogus", "allow")]
        public void NormalizesMode(string mode, string expected)
            => Assert.Equal(expected, WebFetchTool.NormalizeMode(mode));

        [Fact]
        public void RegistryExposesWebFetchUnlessDisabled()
        {
            string root = Path.Combine(Path.GetTempPath(), "openloop-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var scope = new WorkspaceScope(root);
                Assert.NotNull(ToolRegistry.Workspace(scope).Find("WebFetch"));
                Assert.Null(ToolRegistry.Workspace(scope, webFetch: "off").Find("WebFetch"));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
