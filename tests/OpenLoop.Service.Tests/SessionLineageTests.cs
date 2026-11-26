using OpenLoop.Service.Storage;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>衍生会话索引：一条衍生会话只记一条边，重建同名用途时按创建时间取最新一条。</summary>
    [Collection("chat-index")]
    public class SessionLineageTests : IDisposable
    {
        private readonly string _previous;
        private readonly string _dir;

        public SessionLineageTests()
        {
            _previous = ChatHistory.Root;
            _dir = Path.Combine(Path.GetTempPath(), "openloop-lineage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ChatHistory.Root = _dir;
        }

        public void Dispose()
        {
            ChatHistory.Root = _previous;
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Link_upserts_by_derived_id_and_finds_latest_by_purpose()
        {
            SessionLineage.Link(new SessionLink
            {
                Id = "side-1", Role = "worker", Workspace = @"C:\w",
                SourceId = "src", SourceRole = "worker", SourceWorkspace = @"C:\w",
                Purpose = SessionLineage.TargetEdit, Target = "prompt"
            });
            SessionLineage.Link(new SessionLink
            {
                Id = "side-1", Role = "worker", Workspace = @"C:\w",
                SourceId = "src", SourceRole = "worker", SourceWorkspace = @"C:\w",
                Purpose = SessionLineage.TargetEdit, Target = "agents"
            });

            Assert.Equal("agents", SessionLineage.Get("side-1").Target);
            Assert.Single(SessionLineage.Derived("src"));

            SessionLineage.Link(new SessionLink
            {
                Id = "side-2", Role = "worker", Workspace = @"C:\w",
                SourceId = "src", SourceRole = "worker", SourceWorkspace = @"C:\w",
                Purpose = SessionLineage.TargetEdit, CreatedAt = DateTime.UtcNow.AddSeconds(1)
            });

            Assert.Equal("side-2", SessionLineage.Find("src", SessionLineage.TargetEdit).Id);
            Assert.Equal(2, SessionLineage.Derived("src").Count);
            Assert.Null(SessionLineage.Find("src", SessionLineage.Fork));
        }

        [Fact]
        public void Missing_source_is_not_linked()
        {
            SessionLineage.Link(new SessionLink { Id = "side", SourceId = "" });

            Assert.Null(SessionLineage.Get("side"));
            Assert.Empty(SessionLineage.All());
        }
    }
}
