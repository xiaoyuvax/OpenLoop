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
    public void Eval_mode_stages_candidate_and_only_commit_promotes_it()
    {
        Directory.CreateDirectory(_root);
        PromptCatalog.EnsureFile(_root);
        File.WriteAllText(PromptCatalog.FilePath(_root), "正式模板 {{role}}");
        var ledger = Ledger.Open(_root);
        PromptCatalog.Sync(ledger, _root);

        HarnessMode.Set(_root, HarnessMode.Eval);
        Assert.True(HarnessMode.IsEval(_root));

        // 编辑面换成候选包（首次从正式文件播种），正式文件不动
        string editing = PromptCatalog.EditingPath(_root, PromptCatalog.SystemKind);
        Assert.Equal(PromptCatalog.CandidatePath(_root, PromptCatalog.SystemKind), editing);
        Assert.Equal("正式模板 {{role}}", File.ReadAllText(editing).Trim());
        Assert.Empty(PromptCatalog.PendingKinds(_root));

        File.WriteAllText(editing, "候选模板 {{role}}");
        Assert.Single(PromptCatalog.PendingKinds(_root));
        // 未过考核：正式文件与当前生效模板都不变
        Assert.Equal("正式模板 {{role}}", File.ReadAllText(PromptCatalog.FilePath(_root)).Trim());
        Assert.Equal("正式模板 {{role}}", PromptCatalog.ActiveTemplate(_root));

        // 候选包叠到候选激活集上，评测时按候选文本跑
        var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
        var candidate = PromptCatalog.OverlayPending(ledger, _root, ledger.Current, features);
        Assert.Equal("候选模板 {{role}}", PromptCatalog.ResolveTemplate(_root, candidate, features));

        PromptCatalog.CommitPending(_root);
        Assert.Equal("候选模板 {{role}}", File.ReadAllText(PromptCatalog.FilePath(_root)).Trim());
        Assert.Empty(PromptCatalog.PendingKinds(_root));

        HarnessMode.Set(_root, HarnessMode.Direct);
        Assert.Equal(PromptCatalog.FilePath(_root),
            PromptCatalog.EditingPath(_root, PromptCatalog.SystemKind));
    }

    [Fact]
    public void Eval_candidate_can_be_seeded_from_another_directory()
    {
        // Loop 的正式 AGENTS.md 在源码根，候选包落在会话工作区：播种来源要能另外指定
        Directory.CreateDirectory(_root);
        HarnessMode.Set(_root, HarnessMode.Eval);
        string source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "AGENTS.md"), "源码根的约定");

        string editing = PromptCatalog.EditingPath(
            _root, PromptCatalog.AgentsKind, PromptCatalog.AgentsPath(source));

        Assert.Equal(PromptCatalog.CandidatePath(_root, PromptCatalog.AgentsKind), editing);
        Assert.Equal("源码根的约定", File.ReadAllText(editing).Trim());
        // 本工作区的正式文件没有被顺手创建
        Assert.False(File.Exists(Path.Combine(_root, "AGENTS.md")));
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
