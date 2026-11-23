using OpenLoop.Core.Agent;
using OpenLoop.Service.OpenUI;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// 留言板私信（say 带 to，或在正文里 @ 一个会话）的主动投递：把这条私信以「协作提示」的正文
    /// 推进目标会话的输入队列，目标不必自己去读留言板（广播要对方来读，错过就看不到了）。
    ///
    /// 只投同工作区的会话：发言方的工作区取自 <see cref="AgentIdentity.Workspace"/>，
    /// 目标必须在留言板上、且 Purpose 的工作区后缀一致；<c>to</c> 也可以只写 id 前缀（唯一命中就投）。
    /// 按 id 前缀分流：A- 是 A 里的 Worker 会话，I- 是壳里的 Loop 会话；
    /// 本进程能直投就走 <see cref="Local"/>（不经端口），否则走 HTTP：
    /// A 的 <c>POST /api/sessions/{id}/notice</c>、壳的 <c>MainForm.DeliverNotice</c>。
    /// </summary>
    public static class PeerDelivery
    {
        /// <summary>
        /// 本进程的直接投递入口：会话 id（去掉 A-/I- 前缀）、已拼好的提示正文、来源标签（mention / puppet …）。
        /// 返回 false = 本进程没有这个会话，调用方退回 HTTP。
        /// </summary>
        public static Func<string, string, string, bool> Local { get; set; }

        /// <summary>
        /// 把 from 发给 to 的私信投到对方的队列；返回 null 表示已送达，
        /// 否则是给发言方看的一句说明（对方不在/不同工作区/没接上，私信只留在板上）。
        /// </summary>
        public static async Task<string> DeliverAsync(string from, string to, string text, CancellationToken ct = default)
        {
            string target = (to ?? "").Trim().TrimStart('@');
            if (string.IsNullOrWhiteSpace(target)) return null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!string.IsNullOrWhiteSpace(from)
                && string.Equals(StripRole(target), StripRole(from), StringComparison.OrdinalIgnoreCase))
                return "（私信目标是本会话自己，未投递）";

            // @ 的是同工作区的会话：不在本工作区的（或只写了个没命中的前缀）不投，留在板上
            var (resolved, refusal) = Resolve(target);
            if (refusal != null) return refusal;
            target = resolved;

            string notice = PeerNoticeText.ForMessage(from, text);
            const string Tag = "mention";   // 界面折叠头据此标「会话私信」
            bool worker = target.StartsWith("A-", StringComparison.OrdinalIgnoreCase);
            string id = worker ? target[2..]
                : target.StartsWith("I-", StringComparison.OrdinalIgnoreCase) ? target[2..]
                : target;
            string self = (PuppetBridge.SelfRole ?? "").Trim().ToLowerInvariant();

            // 本进程就有这个会话（A 里的 Worker / 壳里的 Loop）：直投，省掉一次往返
            if (Local != null && ((worker && self == "a") || (!worker && self == "shell")))
            {
                try
                {
                    if (Local(id, notice, Tag)) return null;
                }
                catch
                {
                    // 直投失败退回 HTTP：会话表在动、视图已销毁都可能
                }
            }

            try
            {
                if (worker)
                {
                    using var client = new WorkerApiClient();
                    var snapshot = await client.NoticeAsync(id, notice, Tag, ct).ConfigureAwait(false);
                    return snapshot != null ? null : "（对方会话已不在 A 里，私信只留在留言板上，等它自己读）";
                }

                // Loop 会话在壳里：壳没开就投不到，只留留言板
                await ShellPuppet.InvokeAsync("MainForm", "DeliverNotice", [target, notice], ct).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                return $"（对方当前没法接收（{ex.Message}），私信只留在留言板上，等它自己读）";
            }
        }

        /// <summary>
        /// 把 to 解析成同工作区的会话 id：全 id 命中直接用；否则按前缀唯一命中，把完整 id 带回来；
        /// 第二个返回值非空 = 不投递的原因（给发言方看）。
        /// 拿不到工作区（会话没登记工作区）或读不到留言板时不做限制，照原样投。
        /// </summary>
        internal static (string Id, string Refusal) Resolve(string to)
        {
            string target = (to ?? "").Trim().TrimStart('@');
            if (target.Length == 0) return (target, null);

            string workspace = AgentIdentity.Workspace;
            if (string.IsNullOrWhiteSpace(workspace)) return (target, null);

            var peers = SameWorkspacePeers(workspace, AgentIdentity.Id,
                string.Equals(PuppetBridge.SelfRole, "shell", StringComparison.OrdinalIgnoreCase));
            if (peers.Count == 0) return (target, null);   // 留言板读不到：不拿它当拒绝理由

            var exact = peers.FirstOrDefault(p => Matches(p.Id, target));
            if (exact != null) return (exact.Id, null);

            var prefixed = peers.Where(p => (p.Id ?? "").StartsWith(target, StringComparison.OrdinalIgnoreCase)).ToList();
            if (prefixed.Count == 1) return (prefixed[0].Id, null);
            if (prefixed.Count > 1)
                return (target, $"（to=\"{to}\" 命中了多个同工作区会话：{string.Join("、", prefixed.Select(p => p.Id))}；请写全 id）");

            return (target, $"（{to} 不是本工作区的会话：定向留言只投同工作区，这条私信只留在留言板上，它不会主动收到）");
        }

        /// <summary>
        /// 正文里的 @：找出第一个命中同工作区会话的 @token（@A-xxxx / @I-xxxx / @前缀）。
        /// 没有就用 <c>to</c>；两者都没有返回 null（= 广播，要对方自己来读）。
        /// </summary>
        internal static string MentionOf(string text, string fallback = null)
        {
            string explicitTo = (fallback ?? "").Trim().TrimStart('@');
            if (explicitTo.Length > 0) return explicitTo;
            if (string.IsNullOrWhiteSpace(text)) return null;

            string workspace = AgentIdentity.Workspace;
            if (string.IsNullOrWhiteSpace(workspace)) return null;
            var peers = SameWorkspacePeers(workspace, AgentIdentity.Id,
                string.Equals(PuppetBridge.SelfRole, "shell", StringComparison.OrdinalIgnoreCase));
            if (peers.Count == 0) return null;

            foreach (string token in Tokens(text))
            {
                string id = token.TrimStart('@');
                if (id.Length <= 2 || !(id.StartsWith("A-", StringComparison.OrdinalIgnoreCase)
                        || id.StartsWith("I-", StringComparison.OrdinalIgnoreCase))) continue;
                var hit = peers.FirstOrDefault(p => Matches(p.Id, id));
                if (hit != null) return hit.Id;   // 补成完整 id（@ 只写了前缀也认）
            }
            return null;
        }

        /// <summary>正文里以 @ 开头的词：到空白或标点为止（中文标点也算分隔）。</summary>
        private static IEnumerable<string> Tokens(string text)
        {
            foreach (string raw in text.Split(' ', '\t', '\r', '\n'))
            {
                string word = raw.Trim();
                if (word.Length > 0 && word[0] == '@') yield return word.TrimEnd('，', '。', '、', '；', '：', '！', '？', ',', '.', ';', ':', '!', '?', '）', ')', '」', '"', '\'');
            }
        }

        private static bool Matches(string peerId, string wanted)
            => string.Equals(peerId, wanted, StringComparison.OrdinalIgnoreCase)
               || (peerId ?? "").StartsWith(wanted, StringComparison.OrdinalIgnoreCase);

        private static List<PeerInfo> SameWorkspacePeers(string workspace, string selfId, bool remote)
        {
            try
            {
                return SessionBoard.Peers(workspace, selfId, remote).ToList();
            }
            catch
            {
                return [];
            }
        }

        private static string StripRole(string boardId)
        {
            string text = (boardId ?? "").Trim();
            return text.Length > 2 && text[1] == '-' ? text[2..] : text;
        }
    }
}
