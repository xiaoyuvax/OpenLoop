using OpenLoop.Core.Store;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class PromptCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-prompt-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Unregistered_template_falls_back_to_builtin_default()
    {
        Directory.CreateDirectory(_root);

        // 只读方法不建目录、不落默认
        Assert.Null(PromptCatalog.ReadFile(_root));
        Assert.Null(PromptCatalog.ActiveTemplate(_root));
        Assert.False(File.Exists(PromptCatalog.FilePath(_root)));

        PromptCatalog.EnsureFile(_root);
        Assert.True(File.Exists(PromptCatalog.FilePath(_root)));

        // 文件有了但没登记：不悄悄采用未登记的文本
        Assert.Null(PromptCatalog.ActiveTemplate(_root));
    }

    [Fact]
    public void Sync_registers_and_activates_once_per_content()
    {
        Directory.CreateDirectory(_root);
        PromptCatalog.EnsureFile(_root);
        File.WriteAllText(PromptCatalog.FilePath(_root), "自定义模板 {{role}}");
        var ledger = Ledger.Open(_root);

        string id = PromptCatalog.Sync(ledger, _root);
        Assert.StartsWith(PromptCatalog.FeaturePrefix, id);
        Assert.Equal("自定义模板 {{role}}", PromptCatalog.ActiveTemplate(_root));

        int versions = ledger.Activations().Count;
        Assert.Equal(id, PromptCatalog.Sync(ledger, _root));
        Assert.Equal(versions, ledger.Activations().Count);

        // 改了内容但没再登记：回退默认，旧版本仍在账本里可回指
        File.WriteAllText(PromptCatalog.FilePath(_root), "改过的模板 {{role}}");
        Assert.Null(PromptCatalog.ActiveTemplate(_root));
        string next = PromptCatalog.Sync(ledger, _root);
        Assert.NotEqual(id, next);
        Assert.Equal("改过的模板 {{role}}", PromptCatalog.ActiveTemplate(_root));
    }
}
