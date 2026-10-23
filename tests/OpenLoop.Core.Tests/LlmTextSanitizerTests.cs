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

    [Fact]
    public void DetectsSafetyOnlyPayload()
    {
        Assert.True(LlmTextSanitizer.IsSafetyOnly("User Safety: safe\nResponse Safety: unsafe"));
        Assert.True(LlmTextSanitizer.IsSafetyOnly("  response safety: safe  "));
        Assert.False(LlmTextSanitizer.IsSafetyOnly("正文\nUser Safety: safe"));
        Assert.False(LlmTextSanitizer.IsSafetyOnly(""));
    }
}
