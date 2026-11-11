using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Source
{
    /// <summary>
    /// A 启动后的交接：壳在换 A 之前写下「更新 I」的请求，A 起来后负责发布、登记并重启 I。
    /// I 不能换自己，只能由 A 代劳；换成没换成以「新 pid 报到」为准。
    /// </summary>
    public static class LoopUpdater
    {
        public static Task StartAsync() => Task.Run(async () =>
        {
            var (version, sourceTree, present) = IterationRunner.TakeLoopUpdate();
            if (!present) return;

            await Task.Delay(3000);   // 等 A 自己的 bus / runtime 文件就位
            try
            {
                var publish = await Iteration.PublishAsync(BusRoles.I, version);
                if (!publish.Ok)
                {
                    IterationRunner.WriteLoopResult(publish, version);
                    return;
                }
                Iteration.PinSource(publish.Detail, BusRoles.I, version, sourceTree);

                var stage = await Iteration.StageAsync(BusRoles.I, publish.Detail, version);
                if (!stage.Ok)
                {
                    IterationRunner.WriteLoopResult(stage, version);
                    return;
                }

                int previousPid = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(BusRoles.I))?.Pid ?? 0;
                var restart = await Iteration.RestartAsync(BusRoles.I);
                if (!restart.Ok)
                {
                    IterationRunner.WriteLoopResult(restart, version);
                    return;
                }

                var back = await IterationRunner.WaitRestartedAsync(
                    BusRoles.I, previousPid, TimeSpan.FromSeconds(45));
                IterationRunner.WriteLoopResult(
                    back.Ok
                        ? new Iteration.Step("update i", true, $"已登记并重启 I（{back.Detail}）：{publish.Detail}")
                        : back,
                    version);
            }
            catch (Exception ex)
            {
                IterationRunner.WriteLoopResult(new Iteration.Step("update i", false, ex.Message), version);
            }
        });
    }
}
