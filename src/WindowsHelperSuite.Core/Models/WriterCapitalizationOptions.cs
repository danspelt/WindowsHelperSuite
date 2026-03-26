using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Core.Models;

/// <summary>
/// Runtime options for sentence capitalization (from <see cref="WriterSettings"/>).
/// </summary>
public readonly record struct WriterCapitalizationOptions(
    bool Enabled,
    bool CapitalizeSingleLetterI)
{
    public static WriterCapitalizationOptions From(WriterSettings w) =>
        new(w.AutoCapitalizeSentences, w.CapitalizeSingleLetterI);
}
