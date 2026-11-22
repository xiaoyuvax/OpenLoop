namespace OpenLoop.Service.OpenUI
{
    /// <summary>
    /// 把 LoopApp 的浏览器操作送到「拥有该页面的那个浏览器」。
    /// Worker 在 A 进程里，只能经 Puppet 打到壳；Loop 跑在壳进程里，直接走进程内 Hub。
    /// 两者都按 owner 定位，不认「当前可见标签页」——后台会话调工具不会打到别人的窗口上。
    /// </summary>
    public interface ILoopAppHost
    {
        Task<string> OpenAsync(string owner, string url, CancellationToken ct);
        Task<string> EvalAsync(string owner, string js, CancellationToken ct);
        Task<string> ReloadAsync(string owner, CancellationToken ct);
        Task<string> ShotAsync(string owner, CancellationToken ct);
        Task<string> StateAsync(string owner, CancellationToken ct);
        Task<string> UndockAsync(string owner, CancellationToken ct);
        Task<string> DockAsync(string owner, CancellationToken ct);
        Task<string> InteractAsync(string owner, bool enabled, CancellationToken ct);
    }
}
