namespace OpenLoop.Core.Safety
{
    public enum CommandRisk
    {
        Safe,
        Risky,
        Denied
    }

    public sealed class CommandVerdict
    {
        public CommandVerdict(CommandRisk risk, string reason = "")
        {
            Risk = risk;
            Reason = reason;
        }

        public CommandRisk Risk { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// 命令风险分类。只做静态判断，不执行任何东西；真正的隔离在平台适配层（P1）。
    /// Denied = 直接拒绝；Risky = 必须经用户批准。
    /// </summary>
    public static class CommandPolicy
    {
        private static readonly string[] DeniedNames =
        [
            "shutdown", "reboot", "halt", "poweroff", "format", "diskpart", "bcdedit",
            "mkfs", "mkfs.ext4", "mkfs.xfs", "fdisk", "parted", "regedit"
        ];

        private static readonly string[] RiskyNames =
        [
            "rm", "rmdir", "del", "erase", "rd", "mv", "move", "taskkill", "sc", "net",
            "netsh", "reg", "icacls", "takeown", "chmod", "chown", "attrib",
            "sudo", "su", "runas", "passwd", "useradd", "userdel", "usermod",
            "curl", "wget", "iwr", "invoke-webrequest", "invoke-restmethod",
            "docker", "podman", "ssh", "scp", "nc", "ncat"
        ];

        private static readonly string[] ShellNames =
        [
            "cmd", "cmd.exe", "powershell", "powershell.exe", "pwsh", "pwsh.exe",
            "sh", "bash", "zsh", "fish", "dash", "ksh", "csh"
        ];

        public static CommandVerdict Classify(IReadOnlyList<string> argv)
        {
            if (argv == null || argv.Count == 0)
                return new CommandVerdict(CommandRisk.Denied, "缺少 argv");

            string exe = Path.GetFileName(argv[0].Replace('\\', '/')).ToLowerInvariant();
            if (exe.Length == 0)
                return new CommandVerdict(CommandRisk.Denied, "无法识别可执行文件");

            if (DeniedNames.Contains(exe))
                return new CommandVerdict(CommandRisk.Denied, $"禁止的命令：{exe}");

            if (ShellNames.Contains(exe))
                return new CommandVerdict(CommandRisk.Risky, $"shell 解释器：{exe}");

            if (argv.Any(a => a.Contains(".openloop", StringComparison.OrdinalIgnoreCase)))
                return new CommandVerdict(CommandRisk.Risky, "涉及受保护的运行态目录 .openloop");

            if (exe == "dd")
            {
                foreach (string arg in argv.Skip(1))
                    if (arg.StartsWith("of=/dev/", StringComparison.OrdinalIgnoreCase) ||
                        arg.StartsWith("of=\\\\.\\", StringComparison.OrdinalIgnoreCase))
                        return new CommandVerdict(CommandRisk.Denied, "dd 直接写设备");
                return new CommandVerdict(CommandRisk.Risky, "dd");
            }

            if (exe == "git")
            {
                string sub = argv.Count > 1 ? argv[1].ToLowerInvariant() : "";
                if (sub is "reset" && argv.Any(a => a == "--hard"))
                    return new CommandVerdict(CommandRisk.Risky, "git reset --hard");
                if (sub == "clean" && argv.Any(a => a.StartsWith("-f", StringComparison.Ordinal)))
                    return new CommandVerdict(CommandRisk.Risky, "git clean -f");
                if (sub == "push")
                    return new CommandVerdict(CommandRisk.Risky, "git push");
                if (sub == "checkout" && argv.Any(a => a == "--")) return new CommandVerdict(CommandRisk.Risky, "git checkout --（覆盖本地改动）");
            }

            if (exe is "npm" or "pnpm" or "yarn" or "pip" or "pip3" or "dotnet")
            {
                string sub = argv.Count > 1 ? argv[1].ToLowerInvariant() : "";
                if (sub is "install" or "add" or "publish" or "push")
                    return new CommandVerdict(CommandRisk.Risky, $"{exe} {sub}");
            }

            if (exe is "rm" or "del" or "rmdir" or "rd" or "erase")
            {
                foreach (string arg in argv.Skip(1))
                    if (IsRootTarget(arg))
                        return new CommandVerdict(CommandRisk.Denied, "删除根/家目录");
            }

            if (IsRiskyName(exe))
                return new CommandVerdict(CommandRisk.Risky, exe);

            return new CommandVerdict(CommandRisk.Safe);
        }

        private static bool IsRootTarget(string arg)
        {
            string a = arg.Trim();
            if (a is "/" or "\\" or "/*" or "~" or "~/*" or "*") return true;
            return a.Length is 2 or 3 && char.IsLetter(a[0]) && a[1] == ':' && (a.Length == 2 || a[2] is '\\' or '/');
        }

        private static bool IsRiskyName(string exe)
        {
            foreach (string name in RiskyNames)
                if (exe == name || exe == name + ".exe") return true;
            return false;
        }
    }
}
