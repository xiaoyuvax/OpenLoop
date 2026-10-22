using System.Text.Json;

namespace OpenLoop.Core.Store
{
    /// <summary>账本里的一件特征。不可变：改内容必须新增 id。</summary>
    public sealed class FeatureRecord
    {
        public string Id { get; init; }
        public string Kind { get; init; }
        public string Problem { get; init; }
        public JsonElement Payload { get; init; }
        public string Source { get; init; } = "user";
        public string CreatedAt { get; init; }
    }

    /// <summary>激活记录里对特征的引用：只引用 id 与阈值，不复制内容。</summary>
    public sealed class FeatureRef
    {
        public string Id { get; init; }
        public Dictionary<string, JsonElement> Params { get; init; }
    }

    /// <summary>一个版本启用了哪些特征、阈值是多少。文件一次写定，永不覆盖。</summary>
    public sealed class ActivationRecord
    {
        public int Version { get; init; }
        public string At { get; init; }
        public string By { get; init; }
        public string Note { get; init; }
        public List<FeatureRef> Features { get; init; } = [];
    }

    /// <summary>每次变更的判定。必须带对照版本、成本与证据指针。</summary>
    public sealed class VerdictRecord
    {
        public string Id { get; init; }
        public string Decision { get; init; }
        public int BaselineVersion { get; init; }
        public int ResultVersion { get; init; }
        public string Judge { get; init; }
        public string Note { get; init; }
        public decimal? Cost { get; init; }
        public List<string> Evidence { get; init; } = [];
        public string At { get; init; }
    }

    /// <summary>一次运行的证据（O1/O2/O7）。</summary>
    public sealed class RunRecord
    {
        public string Id { get; init; }
        public int ActivationVersion { get; init; }
        public string Task { get; init; }
        public string Outcome { get; init; }
        public int Steps { get; init; }
        public decimal? Cost { get; init; }
        public string TraceDir { get; init; }
        public string At { get; init; }
    }
}
