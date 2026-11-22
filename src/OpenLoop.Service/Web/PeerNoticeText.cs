using System.Text;
using OpenLoop.Core.Agent;

namespace OpenLoop.Service.Web
{
    /// <summary>SessionBoard 的协作提示文本（单独放这里避免内嵌引号被编辑脚本转义弄坏）。</summary>
    public static class PeerNoticeText
    {
        /// <summary>同工作区出现新会话时塞进输入队列的一条提示：与用户输入同一条队列，用户可以撤回。</summary>
        public static string For(IReadOnlyList<string> peers)
        {
            string list = string.Join("；", peers ?? Array.Empty<string>());
            string target = AgentIdentity.BoardTarget;
            return "【协作提示】同工作区新会话已启动：" + list
                + "。技能 peer-board 已可用：Puppet(target=\"" + target + "\", action=\"book\") 看它的工具步骤，"
                + "Puppet(target=\"" + target + "\", action=\"say\", text=\"...\", to=\"<对方 id>\") 定向留言；改动同一文件前先问一句。";
        }
    }
}
