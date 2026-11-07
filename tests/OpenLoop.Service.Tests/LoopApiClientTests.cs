using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenLoop.Service.Web;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 同步包装（<c>GetActivation</c> / <c>SetSkill</c>）会被 UI 线程直接调用。UI 线程用
    /// <c>GetResult()</c> 堵住消息循环、续体又靠同一个循环调度，就是死锁（2026-11-04 实测卡死）。
    /// 这里用一个「只收不跑」的 SynchronizationContext 复现那个环境。
    /// </summary>
    public class LoopApiClientTests
    {
        [Fact]
        public void Sync_helper_does_not_deadlock_on_a_blocked_synchronization_context()
        {
            int port = FreePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            try
            {
                _ = Task.Run(async () =>
                {
                    var context = await listener.GetContextAsync();
                    byte[] body = Encoding.UTF8.GetBytes("""{"skills":[]}""");
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(body);
                    context.Response.Close();
                });

                using var client = new LoopApiClient($"127.0.0.1:{port}", "test-key");
                Exception failure = null;
                var worker = new Thread(() =>
                {
                    SynchronizationContext.SetSynchronizationContext(new BlockedContext());
                    try
                    {
                        client.GetActivation("C:/work");
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                }) { IsBackground = true };

                worker.Start();
                bool finished = worker.Join(TimeSpan.FromSeconds(10));

                Assert.True(finished, "同步调用在阻塞的 SynchronizationContext 上死锁了");
                Assert.Null(failure);
            }
            finally
            {
                listener.Stop();
            }
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        /// <summary>模拟被 <c>GetResult()</c> 堵死的 WinForms 消息循环：续体排进来但永远不执行。</summary>
        private sealed class BlockedContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state)
            {
            }
        }
    }
}
