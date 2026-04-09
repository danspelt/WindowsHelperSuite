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

    [Theory]
    [InlineData('a', true)]
    [InlineData('Z', true)]
    [InlineData('é', true)]
    [InlineData('0', false)]
    [InlineData('9', false)]
    [InlineData('\'', false)]
    [InlineData('-', false)]
    [InlineData(' ', false)]
    [InlineData('.', false)]
    [InlineData('@', false)]
    [InlineData('©', false)]
    public void CanStartWriterSessionFromKeystroke_letters_only(char ch, bool expected) =>
        WriterWordBufferPolicy.CanStartWriterSessionFromKeystroke(ch).Should().Be(expected);

    [Fact]
    public void TryGetDeletePreviousWordStart_hello_world_te_prefix()
    {
        const string before = "hello world   ";
        WriterWordBufferPolicy.TryGetDeletePreviousWordStart(before, out var start).Should().BeTrue();
        start.Should().Be(6);
        (before.Length - start).Should().Be(8); // "world" + three spaces before partial
    }

    [Fact]
    public void TryGetDeletePreviousWordStart_single_word_prefix()
    {
        WriterWordBufferPolicy.TryGetDeletePreviousWordStart("hello ", out var start).Should().BeTrue();
        start.Should().Be(0);
    }

    [Fact]
    public void TryGetDeletePreviousWordStart_empty_or_whitespace_false()
    {
        WriterWordBufferPolicy.TryGetDeletePreviousWordStart("", out _).Should().BeFalse();
        WriterWordBufferPolicy.TryGetDeletePreviousWordStart("   ", out _).Should().BeFalse();
    }

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
