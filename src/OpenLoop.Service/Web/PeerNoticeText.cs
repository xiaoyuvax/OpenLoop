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

        /// <summary>
        /// 别的会话定向发给我的私信（say 带 to）：作为协作提示推进我的输入队列。
        /// 首行是折叠头（界面按协作提示折成一行、淡黄底），正文另起一段；末尾附回复方式。
        /// </summary>
        public static string ForMessage(string from, string text)
        {
            string sender = string.IsNullOrWhiteSpace(from) ? "另一个会话" : from.Trim();
            string target = AgentIdentity.BoardTarget;
            var sb = new StringBuilder();
            sb.Append("【协作提示】").Append(sender).Append(" 私信你（定向留言，只有你与它可见）").AppendLine();
            sb.AppendLine();
            sb.AppendLine((text ?? "").Trim());
            sb.Append("（宿主注入，不是用户说的。不需要回复就不回；要协调就用 Puppet(target=\"").Append(target)
              .Append("\", action=\"say\", text=\"...\", to=\"").Append(sender)
              .Append("\") 私信回复它（带 to 才会直接投递到它的队列）。给用户的正文只写与这条私信相关的告知，不要重复总结之前的上下文或进度；没有要告知的就不要输出正文——本轮界面上按协作提示标淡黄底，用户会跳过思考区，只有正文才当正常答复读。）");
            return sb.ToString();
        }
    }
}
