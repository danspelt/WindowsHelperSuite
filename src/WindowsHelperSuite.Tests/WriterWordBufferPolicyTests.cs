using FluentAssertions;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Tests;

public sealed class WriterWordBufferPolicyTests
{
    [Theory]
    [InlineData('a', true)]
    [InlineData('Z', true)]
    [InlineData('5', true)]
    [InlineData('\'', true)]
    [InlineData('-', true)]
    [InlineData(' ', false)]
    [InlineData('.', false)]
    [InlineData('_', false)]
    [InlineData('@', false)]
    public void IsWordExtendingCharacter_matches_input_buffer_rules(char ch, bool expected) =>
        WriterWordBufferPolicy.IsWordExtendingCharacter(ch).Should().Be(expected);

    [Fact]
    public void NormalizeWord_strips_non_word_chars_and_lowercases() =>
        WriterWordBufferPolicy.NormalizeWord("  Hel-lo's!  ").Should().Be("hel-lo's");

    [Fact]
    public void NormalizeWord_empty_for_whitespace_only() =>
        WriterWordBufferPolicy.NormalizeWord("   ").Should().BeEmpty();

    [Fact]
    public void NormalizeWord_unicode_letters_preserved() =>
        WriterWordBufferPolicy.NormalizeWord("Café").Should().Be("café");
}
