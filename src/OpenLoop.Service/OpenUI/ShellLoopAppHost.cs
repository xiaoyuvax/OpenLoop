namespace OpenLoop.Service.OpenUI
{
    /// <summary>
    /// agent 侧宿主：把动作送到壳里「某个会话自己的浏览器」。
    ///
    /// 走壳 <c>MainForm</c> 上的 <c>LoopApp*For</c> 动作，而不是会话级实例 <c>LoopApp-&lt;id&gt;</c>：
    /// 后者只在那个会话的「网页」页建出来后存在，而 agent 的第一次 open 恰恰发生在页面还不存在的时候。
    /// 壳按 sessionId 找到会话、建页、接线，再转给它的实例。
    ///
    /// 这里传的 sessionId 是**本 agent 自己的会话 id**（工具构造时注入），不是模型给的参数，
    /// 所以跨会话依旧不可表达。
    /// </summary>
    public sealed class ShellLoopAppHost : ILoopAppHost
    {
        private readonly string _owner;

        public ShellLoopAppHost(string owner) => _owner = owner ?? "";

        public Task<string> OpenAsync(string owner, string url, CancellationToken ct)
            => CallAsync("LoopAppOpenFor", ct, _owner, url);

        public Task<string> EvalAsync(string owner, string js, CancellationToken ct)
            => CallAsync("LoopAppEvalFor", ct, _owner, js);

        public Task<string> ReloadAsync(string owner, CancellationToken ct)
            => CallAsync("LoopAppReloadFor", ct, _owner);

        public Task<string> ShotAsync(string owner, CancellationToken ct)
            => CallAsync("LoopAppShotFor", ct, _owner);

        public Task<string> StateAsync(string owner, CancellationToken ct)
            => CallAsync("LoopAppStateFor", ct, _owner);

        public Task<string> UndockAsync(string owner, CancellationToken ct)
            => CallAsync("LoopAppUndockFor", ct, _owner);

        public Task<string> DockAsync(string owner, CancellationToken ct)
            => CallAsync("LoopAppDockFor", ct, _owner);

        public Task<string> InteractAsync(string owner, bool enabled, CancellationToken ct)
            => CallAsync("LoopAppInteractFor", ct, _owner, enabled ? "true" : "false");

        /// <summary>会话级实例名（页面直接调的 <c>LoopApp-&lt;owner&gt;</c>）；这里保留给状态核对用。</summary>
        public static string InstanceName(string owner)
            => "LoopApp-" + (string.IsNullOrWhiteSpace(owner) ? "unknown" : owner.Trim());

        private static Task<string> CallAsync(string method, CancellationToken ct, params object[] args)
            => ShellPuppet.InvokeAsync("MainForm", method, args, ct);
    }
}
