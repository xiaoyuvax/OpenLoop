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
        public void Activate_remembers_the_previous_version_for_crash_rollback()
        {
            string old = MakeVersion("a", "1.0.0-aaa");
            string next = MakeVersion("a", "1.0.1-bbb");
            var store = new VersionStore(_root);

            store.Activate("a", new VersionEntry { Version = "1.0.0", Dir = old });
            store.Activate("a", new VersionEntry { Version = "1.0.1", Dir = next });

            Assert.Equal(old, store.Previous("a")?.Dir);
        }

        [Fact]
        public void Crash_rollback_does_not_record_the_bad_version_as_previous()
        {
            string good = MakeVersion("a", "1.0.0-aaa");
            string bad = MakeVersion("a", "1.0.1-bbb");
            var store = new VersionStore(_root);
            store.Activate("a", new VersionEntry { Version = "1.0.0", Dir = good });
            store.Activate("a", new VersionEntry { Version = "1.0.1", Dir = bad });

            store.Activate("a", new VersionEntry { Version = "1.0.0", Dir = good }, rememberPrevious: false);

            Assert.Equal(good, store.Current("a")?.Dir);
            Assert.Equal(good, store.Previous("a")?.Dir);
        }

        [Fact]
        public void Corrupt_current_json_is_recovered_from_the_backup()
        {
            string a = MakeVersion("a", "1.0.0-aaa");
            string i = MakeVersion("i", "0.9.0-iii");
            var store = new VersionStore(_root);
            store.Activate("a", new VersionEntry { Version = "1.0.0", Dir = a });
            store.Activate("i", new VersionEntry { Version = "0.9.0", Dir = i });
            File.WriteAllText(store.CurrentPath, "{ broken");

            var all = store.ReadOrRecover(out bool recovered);

            Assert.True(recovered);
            Assert.Equal(a, all["a"].Dir);
            Assert.Equal(i, all["i"].Dir);
        }

        [Fact]
        public void Activate_after_corruption_keeps_the_other_role_and_keeps_the_bad_file()
        {
            string a = MakeVersion("a", "1.0.0-aaa");
            string i = MakeVersion("i", "0.9.0-iii");
            string next = MakeVersion("a", "1.0.1-bbb");
            var store = new VersionStore(_root);
            store.Activate("a", new VersionEntry { Version = "1.0.0", Dir = a });
            store.Activate("i", new VersionEntry { Version = "0.9.0", Dir = i });
            File.WriteAllText(store.CurrentPath, "{ broken");

            store.Activate("a", new VersionEntry { Version = "1.0.1", Dir = next });

            var all = new VersionStore(_root).Read();
            Assert.Equal(next, all["a"].Dir);
            Assert.Equal(i, all["i"].Dir);
        }

        [Fact]
        public void Corrupt_current_json_without_a_backup_is_preserved()
        {
            string a = MakeVersion("a", "1.0.0-aaa");
            var store = new VersionStore(_root);
            store.Activate("a", new VersionEntry { Version = "1.0.0", Dir = a });
            File.Delete(store.CurrentPath + ".bak");
            File.WriteAllText(store.CurrentPath, "{ broken");

            Assert.Empty(store.ReadOrRecover(out _));
            Assert.NotEmpty(Directory.GetFiles(_root, "current.json.bad-*"));
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

        [Fact]
        public void Prune_never_deletes_the_version_it_could_roll_back_to()
        {
            string previous = MakeVersion("a", "0.9.0-aaa");
            string middle = MakeVersion("a", "1.0.0-bbb");
            string current = MakeVersion("a", "1.0.1-ccc");
            Directory.SetLastWriteTimeUtc(previous, DateTime.UtcNow.AddDays(-3));
            Directory.SetLastWriteTimeUtc(middle, DateTime.UtcNow.AddDays(-2));
            Directory.SetLastWriteTimeUtc(current, DateTime.UtcNow.AddDays(-1));

            var store = new VersionStore(_root);
            store.Activate("a", new VersionEntry { Version = "0.9.0", Dir = previous });
            store.Activate("a", new VersionEntry { Version = "1.0.0", Dir = middle });
            store.Activate("a", new VersionEntry { Version = "1.0.1", Dir = current });
            store.Prune("a", keep: 2);

            Assert.True(Directory.Exists(previous));
            Assert.True(Directory.Exists(current));
        }
    }
}
