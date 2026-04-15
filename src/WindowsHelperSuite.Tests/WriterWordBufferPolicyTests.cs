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

    [Fact]
    public void SplitLastWhitespaceToken_single_token()
    {
        WriterWordBufferPolicy.SplitLastWhitespaceToken("match", out var prefix, out var last);
        prefix.Should().BeEmpty();
        last.Should().Be("match");
    }

    [Fact]
    public void SplitLastWhitespaceToken_two_tokens()
    {
        WriterWordBufferPolicy.SplitLastWhitespaceToken("hello match", out var prefix, out var last);
        prefix.Should().Be("hello");
        last.Should().Be("match");
    }

    [Fact]
    public void TryResolveCompletedWordForCommit_fragment_after_mid_word_repair()
    {
        WriterWordBufferPolicy.TryResolveCompletedWordForCommit("match", "h", "bogus", out var word, out var before);
        word.Should().Be("match");
        before.Should().BeEmpty();
    }

    [Fact]
    public void TryResolveCompletedWordForCommit_normal_word_equals_sentence_token()
    {
        WriterWordBufferPolicy.TryResolveCompletedWordForCommit("hello world", "world", "x", out var word, out var before);
        word.Should().Be("world");
        before.Should().Be("hello");
    }

    /// <summary>
    /// After fixing matc→h, backspace must trim both buffers so retyping h does not produce matchh on the next commit.
    /// </summary>
    [Fact]
    public void IsSentenceSuffixAlignedWithCurrentWord_allows_dual_backspace_for_suffix_after_mid_word_repair()
    {
        WriterWordBufferPolicy.IsSentenceSuffixAlignedWithCurrentWord("match", "h").Should().BeTrue();
        // If sentence was accidentally matchh, one dual backspace restores match + empty word
        WriterWordBufferPolicy.IsSentenceSuffixAlignedWithCurrentWord("matchh", "h").Should().BeTrue();
    }

    [Theory]
    [InlineData("match", "match", true)]
    [InlineData("hello match", "match", true)]
    [InlineData("match", "h", true)]
    [InlineData("matchh", "h", true)]
    [InlineData("hello match", "h", true)]
    [InlineData("a.", "a", false)]
    public void IsSentenceSuffixAlignedWithCurrentWord_cases(string sentence, string word, bool aligned) =>
        WriterWordBufferPolicy.IsSentenceSuffixAlignedWithCurrentWord(sentence, word).Should().Be(aligned);
}
