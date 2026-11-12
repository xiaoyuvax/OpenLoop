using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Trace;
using Xunit;

namespace OpenLoop.Core.Tests
{
    /// <summary>trace 里要能看出「实际发给模型的是什么」，而不只是对话两端。</summary>
    public class TraceWriterTests
    {
        [Fact]
        public void Native_tool_calls_are_recorded_in_steps()
        {
            string root = Path.Combine(Path.GetTempPath(), "openloop-trace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using var trace = TraceWriter.Start(root, "task", "hi", "m");
                trace.NativeTool(new NativeToolCall("shell", "{\"command\":\"dir\"}", false, "denied", "error"));
                trace.Finish("stopped", "");

                string[] lines = File.ReadAllLines(Path.Combine(trace.DirectoryPath, "steps.jsonl"));
                Assert.Single(lines);
                Assert.Contains("\"tool\":\"native:shell\"", lines[0]);
                Assert.Contains("dir", lines[0]);
                Assert.Contains("\"ok\":false", lines[0]);
                Assert.Contains("\"status\":\"error\"", lines[0]);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void Records_what_was_sent_to_the_model()
        {
            string root = Path.Combine(Path.GetTempPath(), "openloop-trace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using var trace = TraceWriter.Start(root, "task", "hi", "m");
                trace.Message("user", "hi");
                trace.Request([new LlmMessage("system", "sys"), new LlmMessage("user", "hi")], 123);
                trace.Message("assistant", "ok");
                trace.Finish("ok", "ok");

                string[] lines = File.ReadAllLines(Path.Combine(trace.DirectoryPath, "requests.jsonl"));
                Assert.Single(lines);
                Assert.Contains("\"tokens\":123", lines[0]);
                Assert.Contains("\"sys\"", lines[0]);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
