using FluentAssertions;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Tests;

public sealed class WriterTitleContextHintsTests
{
    [Theory]
    [InlineData(null, 1.0)]
    [InlineData("", 1.0)]
    [InlineData("Inbox - me@example.com - Outlook", 1.06)]
    [InlineData("Mail - John Doe - Outlook", 1.06)]
    [InlineData("Inbox (12) - Gmail", 1.05)]
    [InlineData("Random document", 1.0)]
    [InlineData("Inbox - user@company.org", 1.04)]
    public void PhraseBoostFromWindowTitle_cases(string? title, double expected) =>
        WriterTitleContextHints.PhraseBoostFromWindowTitle(title).Should().BeApproximately(expected, 0.001);

    [Fact]
    public void Outlook_beats_generic_inbox_without_at()
    {
        WriterTitleContextHints.PhraseBoostFromWindowTitle("Outlook Web").Should().BeGreaterThan(1.0);
        WriterTitleContextHints.PhraseBoostFromWindowTitle("Project inbox board").Should().Be(1.0);
    }
}
