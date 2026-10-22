using OpenLoop.Core.Messages;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class LlmTextSanitizerTests
{
    [Fact]
    public void StripsProviderSafetyLines()
    {
        string raw = "你好。\nUser Safety: safe\nResponse Safety: safe";
        Assert.Equal("你好。", LlmTextSanitizer.StripSafetyLines(raw));
    }

    [Fact]
    public void KeepsNormalTextAndSafetyMentionsInSentences()
    {
        const string text = "The safety check passed.\n模型的安全性由供应商负责。";
        Assert.Equal(text, LlmTextSanitizer.StripSafetyLines(text));
    }
}
