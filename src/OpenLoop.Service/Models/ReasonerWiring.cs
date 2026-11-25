using OpenLoop.Core.Agent;
using OpenLoop.Core.OpenCode;
using OpenLoop.Core.Reasoning;

namespace OpenLoop.Service.Models
{
    /// <summary>
    /// 宿主接线层：把 reasoner 的可选通道（状态通报、原生工具上报、模型进度、软停止）
    /// 统一接到宿主回调上。Worker（WorkerSessionService）、Loop（LoopView）、旁路（SideWorkView）
    /// 与演进 runner（LoopService）共用这一份——新增一种通知通道时只改这里，
    /// 各宿主不会再各自漂移（此前 Loop/旁路漏接 OnNotice/OnNativeTool，免费模型长等待时界面没有交代）。
    /// </summary>
    public static class ReasonerWiring
    {
        /// <summary>宿主侧接收点：为 null 的通道不接。</summary>
        public sealed class Host
        {
            /// <summary>即时状态通报（等待审批/压缩上下文/等待响应），null = 清除。</summary>
            public Action<string> Notice;

            /// <summary>推理通道内的原生工具调用（宿主写 trace 与步骤流）。</summary>
            public Action<NativeToolCall> NativeTool;

            /// <summary>模型进度（等待/重试），OpenAiCompatible 与 OpenCode 两通道都支持。</summary>
            public Action<OpenAiCompatibleReasoner.ReasonerProgress> ModelProgress;

            /// <summary>软停止：一次调用内部的多个原生工具之间也据此收尾。</summary>
            public Func<bool> ShouldStop;
        }

        /// <summary>接好所有可选通道；返回传入的 reasoner（便于内联使用）。</summary>
        public static IReasoner Attach(IReasoner reasoner, Host host)
        {
            if (reasoner == null || host == null) return reasoner;

            if (reasoner is INoticeObserver notice && host.Notice != null)
                notice.OnNotice = host.Notice;

            if (reasoner is INativeToolObserver native && host.NativeTool != null)
                native.OnNativeTool = host.NativeTool;

            if (reasoner is OpenCodeReasoner openCode)
            {
                if (host.ModelProgress != null) openCode.OnProgress = host.ModelProgress;
                if (host.ShouldStop != null) openCode.ShouldStop = host.ShouldStop;
            }
            else if (reasoner is OpenAiCompatibleReasoner direct && host.ModelProgress != null)
            {
                direct.OnProgress = host.ModelProgress;
            }

            return reasoner;
        }

        /// <summary>
        /// 原生工具调用的标准步骤记录（界面步骤流与 trace 同一形状）。
        /// 原生工具的入参是 JSON 原文，这里压成可读摘要；trace 需要原文时另走 trace.NativeTool。
        /// </summary>
        public static AgentStepRecord NativeStep(NativeToolCall tool) => new()
        {
            Tool = "native:" + tool.Tool,
            ArgsSummary = Truncate(tool.Input, 120),
            ArgsFull = tool.Input,
            Ok = tool.Ok,
            Summary = Truncate(string.IsNullOrEmpty(tool.Output) ? tool.Status : tool.Output, 160),
            FullResult = "native-result: " + tool.Output,
            ResultFull = tool.Output,
            DurationMs = tool.DurationMs
        };

        private static string Truncate(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";
    }
}
