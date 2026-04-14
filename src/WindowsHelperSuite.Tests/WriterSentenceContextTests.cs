using FluentAssertions;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Tests;

public sealed class WriterSentenceContextTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("hello", "hello")]
    [InlineData("hello world", "world")]
    [InlineData("  hello world  ", "world")]
    public void LastCompletedWord_returns_last_token(string context, string expected) =>
        WriterSentenceContext.LastCompletedWord(context).Should().Be(expected);
}
