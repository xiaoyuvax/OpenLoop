using OpenLoop.Service.Bus;
using OpenLoop.Service.Shell;
using OpenLoop.Service.Source;

namespace OpenLoop.App
{
    /// <summary>
    /// 关于窗口：左侧程序图标（最大分辨率帧）与标题，右侧只读文本框列出各组件版本；
    /// 下方「退回上一个版本」把 A/I 换回上一版本并重启（壳不动）——
    /// Loop 页打不开时从这里退，仍然走确认框 + 显式重启，不静默换版。
    /// </summary>
    public partial class AboutForm : Form
    {
        private bool _rollingBack;

        public AboutForm()
        {
            InitializeComponent();
            Initialize();
        }

        private void Initialize()
        {
            Icon = UiIcons.App;
            _picLogo.Image = LargestIconFrame(UiIcons.App);
            _lblTitle.Text = Localization.T("AppTitle");
            _lblSub.Text = Localization.T("AppSubtitle");
            _lblVersions.Text = Localization.T("AboutVersions");
            _txtVersions.Text = VersionSummary();
            _btnRollback.Text = Localization.T("退回上一个版本");
            _tips.SetToolTip(_btnRollback,
                Localization.T("把 A/I 退回上一版本，壳保持当前版本（没有上一版本时不可用）"));
            SyncRollback();
        }

        /// <summary>各组件版本行：壳取自身 exe，A/I 取运行中的 runtime/&lt;role&gt;.json，没跑就退回 exe。</summary>
        internal static string[] VersionLines() =>
        [
            Localization.T("壳：") + OrUnknown(ExeVersion(Application.ExecutablePath)),
            "Worker：" + OrUnknown(RoleVersion(BusRoles.A)),
            "Loop：" + OrUnknown(RoleVersion(BusRoles.I))
        ];

        /// <summary>多行版本文本；MainForm.Versions（Puppet）仍用「 | 」连成一行。</summary>
        internal static string VersionSummary() => string.Join(Environment.NewLine, VersionLines());

        private static string RoleVersion(string role)
        {
            var runtime = OpenLoop.Service.Runtime.RuntimeInfo.TryLoad(
                OpenLoop.Service.Runtime.AppPaths.RuntimeFile(role));
            if (!string.IsNullOrWhiteSpace(runtime?.Version)) return runtime.Version;

            var store = new VersionStore();
            string exe = store.Exe(role);
            if (string.IsNullOrWhiteSpace(exe))
                exe = System.IO.Path.Combine(AppContext.BaseDirectory, "backends", VersionStore.ExeName(role));
            return ExeVersion(exe);
        }

        private static string ExeVersion(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return "";
                string raw = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
                return SourceSync.ParseVersion(raw);
            }
            catch
            {
                return "";
            }
        }

        private static string OrUnknown(string version)
            => string.IsNullOrWhiteSpace(version) ? Localization.T("未知") : version;

        /// <summary>可退回的上一版本：previous.json 有登记、目录还在、且不是当前跑的这份（与 LoopView 同一口径）。</summary>
        private static Dictionary<string, VersionEntry> RollbackTargets()
        {
            var store = new VersionStore();
            var map = new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (string role in new[] { BusRoles.A, BusRoles.I })
            {
                var entry = store.Previous(role);
                if (string.IsNullOrWhiteSpace(entry?.Dir) || !Directory.Exists(entry.Dir)) continue;
                if (string.Equals(store.Current(role)?.Dir, entry.Dir, StringComparison.OrdinalIgnoreCase)) continue;
                map[role] = entry;
            }
            return map;
        }

        private void SyncRollback() => _btnRollback.Enabled = !_rollingBack && RollbackTargets().Count > 0;

        private async void OnRollback(object sender, EventArgs e)
        {
            if (_rollingBack) return;
            var targets = RollbackTargets();
            if (targets.Count == 0)
            {
                _btnRollback.Enabled = false;
                _lblStatus.Text = Localization.T("状态: 当前已经是上一轮之前的版本");
                return;
            }

            string where = string.Join(Localization.T("ListSep"),
                targets.Select(t => $"{t.Key.ToUpperInvariant()} {t.Value.Version}"));
            if (MessageBox.Show(this, string.Format(Localization.T("ConfirmRevert"), where),
                    Localization.T("退回上一版本"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _rollingBack = true;
            SyncRollback();
            _lblStatus.Text = Localization.T("状态: 正在退回…");
            try
            {
                var steps = await IterationRunner.RollbackAsync(targets, _ => { });
                if (steps.Count > 0 && steps.All(s => s.Ok))
                {
                    // 下一轮把这两个角色当「未发布」，重编当前源码版本；与 LoopView 的退回同一处理
                    VersionState.Forget(targets.Keys);
                    _lblStatus.Text = Localization.T("状态: 已退回上一版本（壳未变）");
                }
                else
                {
                    _lblStatus.Text = Localization.T("状态: 退回未全部成功")
                        + Localization.T("worker 忙就等这一轮跑完再试。");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("退回失败", ex);
                _lblStatus.Text = Localization.T("状态: 退回失败");
                AntdUI.Notification.error(this, Localization.T("退回失败"), ex.Message,
                    AntdUI.TAlignFrom.TR, null, 4);
            }
            finally
            {
                _rollingBack = false;
                SyncRollback();
                _txtVersions.Text = VersionSummary();
            }
        }
    }
}
