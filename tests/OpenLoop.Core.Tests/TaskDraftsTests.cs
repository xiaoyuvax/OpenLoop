using System.Text.Json;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class TaskDraftsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-draft-tests", Guid.NewGuid().ToString("N"));

    public TaskDraftsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string EvalDir => Path.Combine(_root, ".openloop", "eval");

    private static EvalTask Task(string id = "batch-write") => new()
    {
        Id = id,
        Problem = "批量写入",
        Prompt = "创建 f01.txt，内容写 1。",
        Checks = [new TaskCheck { Type = "file_equals", Path = "f01.txt", Text = "1" }]
    };

    private static TaskDraftSource Source() => new() { Trace = "traces/T-1", Finding = "F-1" };

    [Fact]
    public void Draft_stays_out_of_task_set_until_accepted()
    {
        var draft = TaskDrafts.Add(_root, Task(), "防止谎报完成", Source());

        Assert.Equal(TaskDrafts.StatusDraft, draft.Status);
        Assert.Equal(1, TaskDrafts.PendingCount(_root));
        Assert.False(Directory.Exists(Path.Combine(EvalDir, "tasks")));

        string path = TaskDrafts.Accept(_root, draft.Id);

        Assert.True(File.Exists(path));
        var tasks = TaskSet.Load(EvalDir);
        var task = Assert.Single(tasks.Tasks);
        Assert.Equal("batch-write", task.Id);
        Assert.Equal("user", task.Source);
        Assert.Equal(TaskDrafts.StatusAccepted, TaskDrafts.LoadOne(_root, draft.Id).Status);
        Assert.Equal(0, TaskDrafts.PendingCount(_root));
    }

    [Fact]
    public void Draft_requires_source()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TaskDrafts.Add(_root, Task(), "无来源", new TaskDraftSource()));
    }

    [Fact]
    public void Invalid_task_is_rejected()
    {
        var bad = new EvalTask
        {
            Id = "no-prompt",
            Problem = "缺少提示",
            Checks = [new TaskCheck { Type = "file_equals", Path = "f01.txt", Text = "1" }]
        };
        Assert.Throws<InvalidDataException>(() => TaskDrafts.Add(_root, bad, "x", Source()));
        Assert.Empty(TaskDrafts.Load(_root));
    }

    [Fact]
    public void Duplicate_drafts_are_rejected()
    {
        TaskDrafts.Add(_root, Task(), "第一次", Source());
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TaskDrafts.Add(_root, Task(), "第二次", Source()));
        Assert.Contains("已有相同草稿", ex.Message);
    }

    [Fact]
    public void Duplicate_of_existing_task_is_rejected()
    {
        Directory.CreateDirectory(Path.Combine(EvalDir, "tasks"));
        File.WriteAllText(Path.Combine(EvalDir, "tasks", "batch-write.json"),
            JsonSerializer.Serialize(Task(), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }), new System.Text.UTF8Encoding(false));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TaskDrafts.Add(_root, Task(), "重复", Source()));
        Assert.Contains("题集里已有", ex.Message);
    }

    [Fact]
    public void Reject_keeps_draft_but_not_task_set()
    {
        var draft = TaskDrafts.Add(_root, Task(), "x", Source());
        TaskDrafts.Reject(_root, draft.Id);

        Assert.Equal(TaskDrafts.StatusRejected, TaskDrafts.LoadOne(_root, draft.Id).Status);
        Assert.False(Directory.Exists(Path.Combine(EvalDir, "tasks")));
        Assert.DoesNotContain(TaskDrafts.Load(_root), d => d.Status == TaskDrafts.StatusDraft);
    }

    [Fact]
    public async Task Propose_task_tool_is_loop_only_and_writes_draft()
    {
        using var scope = new WorkspaceScopeForTest(_root);
        Assert.Null(ToolRegistry.Workspace(scope.Scope).Find("propose_task"));
        var tool = ToolRegistry.Loop(scope.Scope, _root).Find("propose_task");
        Assert.NotNull(tool);

        var args = new ToolArgs(new Dictionary<string, object>
        {
            ["task"] = JsonSerializer.SerializeToElement(Task()),
            ["reason"] = "观察到的失败",
            ["source"] = JsonSerializer.SerializeToElement(new { trace = "traces/T-1" })
        });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        var draft = Assert.Single(TaskDrafts.Load(_root));
        Assert.Equal("traces/T-1", draft.Source.Trace);
        Assert.Equal("观察到的失败", draft.Reason);
        Assert.False(Directory.Exists(Path.Combine(EvalDir, "tasks")));
    }

    [Fact]
    public void Eval_report_roundtrip()
    {
        var saved = EvalReportStore.Save(_root, new EvalReport
        {
            BaselineVersion = 1,
            CandidateVersion = 2,
            Adopted = true,
            Reason = "更好",
            BaselinePassed = 1,
            CandidatePassed = 2,
            Tasks =
            [
                new EvalReportTask
                {
                    Id = "a",
                    BaselinePassed = false,
                    CandidatePassed = true,
                    BaselineSteps = 3,
                    CandidateSteps = 4,
                    BaselineEvidence = "eval/runs/a-v1",
                    CandidateEvidence = "eval/runs/a-v2"
                }
            ]
        });

        Assert.NotNull(saved.DirectoryPath);
        Assert.True(File.Exists(Path.Combine(_root, saved.DirectoryPath, "report.json")));
        string markdown = File.ReadAllText(Path.Combine(_root, saved.DirectoryPath, "report.md"));
        Assert.Contains("a", markdown);
        Assert.Contains("采纳", markdown);

        var listed = Assert.Single(EvalReportStore.List(_root));
        Assert.Equal(saved.Id, listed.Id);
        Assert.Equal(1, listed.Total);
        var task = Assert.Single(listed.Tasks);
        Assert.Equal(4, task.CandidateSteps);
    }

    private sealed class WorkspaceScopeForTest : IDisposable
    {
        public WorkspaceScopeForTest(string root) => Scope = new WorkspaceScope(root);

        public WorkspaceScope Scope { get; }

        public void Dispose() { }
    }
}
