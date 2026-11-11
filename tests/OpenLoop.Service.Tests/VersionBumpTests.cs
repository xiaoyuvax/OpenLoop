using OpenLoop.Service.Source;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class VersionBumpTests
    {
        [Fact]
        public void NextVersion_bumps_running_patch_when_source_is_not_newer()
            => Assert.Equal("0.1.1", VersionBump.NextVersion("0.1.0", "0.1.0"));

        [Fact]
        public void WriteVersion_keeps_file_parseable()
        {
            // 回归：替换串写成 "$1"+版本号 会拼出 "$10.1.1"，把 <Version> 开标签一起毁掉
            string root = Path.Combine(Path.GetTempPath(), "ol-vb-" + Guid.NewGuid().ToString("N"));
            string dir = Path.Combine(root, "src", "OpenLoop.A");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "OpenLoop.A.csproj");
            File.WriteAllText(file, "<Project>\n  <PropertyGroup>\n    <Version>0.1.0</Version>\n  </PropertyGroup>\n</Project>\n");
            try
            {
                Assert.True(VersionBump.WriteVersion("a", root, "0.1.1"));

                string text = File.ReadAllText(file);
                Assert.Contains("<Version>0.1.1</Version>", text);
                Assert.DoesNotContain("$1", text);
                Assert.Equal("0.1.1", SourceSync.ParseVersion(text));
                Assert.NotEqual(0xEF, File.ReadAllBytes(file)[0]);   // 不写 BOM
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void WriteVersion_only_touches_the_given_role()
        {
            // 七个节点各自独立：只升 A 时 I 和壳的版本文件必须原样不动
            string root = Path.Combine(Path.GetTempPath(), "ol-vb-" + Guid.NewGuid().ToString("N"));
            string[] files = [VersionBump.FileFor("core"), VersionBump.FileFor("service"), VersionBump.FileFor("a"),
                              VersionBump.FileFor("i"), VersionBump.FileFor("shell")];
            foreach (string relative in files)
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, relative)));
            try
            {
                foreach (string relative in files)
                    File.WriteAllText(Path.Combine(root, relative),
                        "<Project>\n  <PropertyGroup>\n    <Version>0.1.0</Version>\n  </PropertyGroup>\n</Project>\n");

                Assert.True(VersionBump.WriteVersion("a", root, "0.1.1"));

                Assert.Equal("0.1.1", SourceSync.RoleSourceVersion("a", root));
                Assert.Equal("0.1.0", SourceSync.RoleSourceVersion("i", root));
                Assert.Equal("0.1.0", SourceSync.RoleSourceVersion("shell", root));
                Assert.Equal("0.1.0", SourceSync.RoleSourceVersion("core", root));
                Assert.Equal("0.1.0", SourceSync.RoleSourceVersion("service", root));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Shell_version_has_a_single_declaration_file()
        {
            // 两种壳产物共用一个号：版本只写在 ShellVersion.props
            Assert.Equal("ShellVersion.props", VersionBump.FileFor("shell"));
            Assert.Equal("src/OpenLoop.Core/OpenLoop.Core.csproj", VersionBump.FileFor("core"));
            Assert.Equal("src/OpenLoop.Service/OpenLoop.Service.csproj", VersionBump.FileFor("service"));
        }

        [Fact]
        public void Version_line_does_not_change_the_node_fingerprint_input()
        {
            // 回归：把新号写回版本文件后，如果指纹跟着变，下一轮就会再升一版（无限跳号）
            string root = Path.Combine(Path.GetTempPath(), "ol-vn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string older = Path.Combine(root, "old.csproj");
                string newer = Path.Combine(root, "new.csproj");
                File.WriteAllText(older, "<Project>\n  <PropertyGroup>\n    <Version>0.1.8</Version>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
                File.WriteAllText(newer, "<Project>\n  <PropertyGroup>\n    <Version>0.1.9</Version>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");

                Assert.Equal(VersionScan.VersionNeutralHash(older), VersionScan.VersionNeutralHash(newer));

                File.WriteAllText(newer, "<Project>\n  <PropertyGroup>\n    <Version>0.1.9</Version>\n    <TargetFramework>net9.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
                Assert.NotEqual(VersionScan.VersionNeutralHash(older), VersionScan.VersionNeutralHash(newer));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void ParseVersion_refuses_whole_file_text()
            => Assert.Equal("", SourceSync.ParseVersion(
                "<Project>\n  <PropertyGroup>\n    $10.1.1</Version>\n  </PropertyGroup>\n</Project>"));
    }
}
