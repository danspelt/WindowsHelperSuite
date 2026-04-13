using System.Text.RegularExpressions;

namespace StillSpace.Counseling;

public enum CrisisLevel
{
    None,
    Elevated
}

public static class CrisisDetector
{
    private static readonly Regex[] Patterns =
    {
        new(@"\b(kill myself|end it all|suicid|want to die|better off dead)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(can't go on|cannot go on|no point (in )?living)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(hurt someone|harm (them|him|her)|going to kill)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    };

    public const string ResourcesText =
        "If you are in immediate danger, please contact local emergency services. In the U.S., you can call or text 988 (Suicide & Crisis Lifeline). If you can, reach someone you trust who can be with you in person.";

    public static CrisisLevel Detect(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return CrisisLevel.None;
        return Patterns.Any(re => re.IsMatch(t)) ? CrisisLevel.Elevated : CrisisLevel.None;
    }
}
