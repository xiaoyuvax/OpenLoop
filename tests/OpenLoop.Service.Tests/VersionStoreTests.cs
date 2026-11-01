using OpenLoop.Service.Shell;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>版本目录：current.json 是所有者的唯一真相，剪枝只删旧版本。</summary>
    public class VersionStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-versions-" + Guid.NewGuid().ToString("N"));

        public VersionStoreTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string MakeVersion(string role, string name, bool withExe = true)
        {
            string dir = Path.Combine(_root, role, name);
            Directory.CreateDirectory(dir);
            if (withExe) File.WriteAllText(VersionStore.ExePath(dir, role), "stub");
            return dir;
        }

        [Fact]
        public void Fresh_store_has_no_current_version()
        {
            var store = new VersionStore(_root);

            Assert.Null(store.Current("a"));
            Assert.Null(store.Exe("a"));
        }

        [Fact]
        public void Activate_is_visible_to_a_new_reader_and_points_at_the_exe()
        {
            string dir = MakeVersion("a", "1.0.1-abc");
            new VersionStore(_root).Activate("a", new VersionEntry { Version = "1.0.1", Dir = dir, Sha256 = "x" });

            var store = new VersionStore(_root);
            Assert.Equal("1.0.1", store.Current("a")?.Version);
            Assert.Equal(VersionStore.ExePath(dir, "a"), store.Exe("a"));
        }

        [Fact]
        public void Missing_exe_falls_back_to_null()
        {
            string dir = MakeVersion("a", "1.0.2-def", withExe: false);
            new VersionStore(_root).Activate("a", new VersionEntry { Version = "1.0.2", Dir = dir });

            Assert.Null(new VersionStore(_root).Exe("a"));
        }

        [Fact]
        public void Prune_keeps_the_current_and_the_newest_other()
        {
            string old1 = MakeVersion("a", "0.9.0-aaa");
            string old2 = MakeVersion("a", "1.0.0-bbb");
            string current = MakeVersion("a", "1.0.1-ccc");
            Directory.SetLastWriteTimeUtc(old1, DateTime.UtcNow.AddDays(-2));
            Directory.SetLastWriteTimeUtc(old2, DateTime.UtcNow.AddDays(-1));

            var store = new VersionStore(_root);
            store.Activate("a", new VersionEntry { Version = "1.0.1", Dir = current });
            store.Prune("a", keep: 2);

            Assert.False(Directory.Exists(old1));
            Assert.True(Directory.Exists(old2));
            Assert.True(Directory.Exists(current));
        }
    }
}
