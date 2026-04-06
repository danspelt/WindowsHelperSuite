using System.Reflection;

namespace WindowsHelperSuite.Prediction;

/// <summary>
/// Comprehensive English dictionary loaded from embedded resource files.
/// Words and phrases are organized by frequency tiers defined in the resource files.
/// Format: lines starting with # set the current tier frequency (e.g. #100),
/// all subsequent non-empty lines are entries at that frequency.
/// </summary>
public static class EnglishDictionary
{
    // ── Lazy-loaded tiers from embedded resources ─────────────────────────
    private static readonly Lazy<(string[] Words, int BaseFrequency)[]> _wordTiers =
        new(() => LoadTiers("WindowsHelperSuite.Prediction.Resources.english-words.txt"));

    private static readonly Lazy<(string[] Phrases, int BaseFrequency)[]> _phraseTiers =
        new(() => LoadTiers("WindowsHelperSuite.Prediction.Resources.texting-phrases.txt"));

    public static (string[] Words, int BaseFrequency)[] WordTiers => _wordTiers.Value;
    public static (string[] Phrases, int BaseFrequency)[] PhraseTiers => _phraseTiers.Value;

    private static (string[] Items, int BaseFrequency)[] LoadTiers(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return [];

        using var reader = new StreamReader(stream);
        var tiers = new List<(string[], int)>();
        var currentItems = new List<string>();
        var currentFreq = 5; // default

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            if (trimmed.StartsWith('#'))
            {
                // Flush previous tier
                if (currentItems.Count > 0)
                {
                    tiers.Add((currentItems.ToArray(), currentFreq));
                    currentItems = new List<string>();
                }
                if (int.TryParse(trimmed.AsSpan(1), out var freq))
                    currentFreq = freq;
                continue;
            }

            currentItems.Add(trimmed);
        }

        // Flush last tier
        if (currentItems.Count > 0)
            tiers.Add((currentItems.ToArray(), currentFreq));

        return tiers.ToArray();
    }

    // ── Sentence starters (suggested when context is empty) ────────────
    public static readonly string[] SentenceStarters =
    [
        "i", "the", "we", "it", "he", "she", "they", "you", "this", "that",
        "there", "what", "how", "when", "where", "why", "who", "can", "do",
        "is", "are", "was", "will", "would", "could", "should", "have", "let",
        "my", "our", "please", "thank", "yes", "no", "but", "so", "if",
        "just", "also", "well",
    ];

    // ── Bigram map: previous word → likely next words ────────────────────
    // Used for context-aware next-word prediction
    public static readonly Dictionary<string, string[]> NextWordMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["i"] = ["am", "have", "want", "need", "think", "know", "can", "will", "was", "do", "would", "like", "feel", "hope", "believe", "love", "just", "don't", "really", "also"],
        ["you"] = ["are", "have", "can", "will", "want", "need", "know", "should", "would", "do", "like", "think", "don't", "could", "were", "just", "really", "might"],
        ["he"] = ["is", "was", "has", "will", "can", "would", "could", "should", "said", "did", "does", "had", "went", "got", "likes", "wants", "needs", "thinks"],
        ["she"] = ["is", "was", "has", "will", "can", "would", "could", "should", "said", "did", "does", "had", "went", "got", "likes", "wants", "needs", "thinks"],
        ["it"] = ["is", "was", "will", "would", "can", "could", "has", "does", "looks", "seems", "feels", "sounds", "takes", "makes", "works", "depends"],
        ["we"] = ["are", "have", "can", "will", "need", "should", "would", "could", "want", "were", "do", "just", "also", "don't", "all", "might"],
        ["they"] = ["are", "have", "can", "will", "were", "would", "could", "should", "do", "want", "need", "said", "don't", "just", "also", "all"],
        ["the"] = ["best", "first", "last", "most", "next", "other", "same", "way", "world", "time", "new", "old", "only", "right", "good", "whole", "problem", "thing"],
        ["a"] = ["lot", "little", "few", "bit", "new", "good", "great", "big", "long", "small", "very", "really", "couple", "single", "whole", "certain"],
        ["is"] = ["a", "the", "not", "it", "that", "this", "very", "really", "there", "just", "also", "going", "what", "how", "why", "so", "still", "always"],
        ["are"] = ["you", "we", "they", "not", "the", "there", "going", "some", "all", "very", "so", "still", "just", "also", "really", "here"],
        ["was"] = ["a", "the", "not", "it", "that", "very", "really", "just", "so", "going", "there", "about", "also", "still", "already", "always"],
        ["have"] = ["a", "to", "been", "not", "the", "you", "any", "some", "no", "it", "never", "ever", "already", "just", "also", "always"],
        ["do"] = ["you", "not", "it", "that", "this", "the", "we", "they", "something", "anything", "everything", "well", "better"],
        ["will"] = ["be", "have", "not", "do", "get", "make", "take", "come", "go", "see", "need", "try", "let", "help", "also", "never", "always"],
        ["can"] = ["you", "i", "we", "they", "be", "do", "have", "get", "make", "see", "help", "tell", "also", "not", "just", "still"],
        ["would"] = ["be", "have", "like", "you", "not", "love", "want", "need", "rather", "say", "think", "prefer", "also", "never", "just"],
        ["should"] = ["be", "have", "i", "we", "you", "not", "do", "try", "get", "make", "take", "also", "probably", "never", "just"],
        ["could"] = ["be", "have", "you", "not", "do", "get", "make", "see", "tell", "help", "also", "just", "never", "still"],
        ["don't"] = ["know", "think", "want", "have", "need", "like", "worry", "care", "mind", "understand", "believe", "forget", "remember"],
        ["didn't"] = ["know", "think", "want", "have", "need", "like", "get", "see", "say", "do", "mean", "expect", "realize"],
        ["what"] = ["is", "are", "do", "does", "did", "was", "were", "would", "about", "if", "happened", "kind", "time", "else"],
        ["how"] = ["are", "is", "do", "does", "did", "was", "much", "many", "long", "about", "come", "old", "far", "often", "would"],
        ["when"] = ["i", "you", "we", "they", "it", "the", "did", "do", "will", "is", "was", "are", "can", "should"],
        ["where"] = ["is", "are", "do", "does", "did", "was", "were", "can", "will", "would", "the", "i", "you", "we"],
        ["why"] = ["is", "are", "do", "does", "did", "was", "were", "would", "don't", "not", "can't", "should", "the"],
        ["who"] = ["is", "are", "was", "were", "can", "will", "would", "does", "did", "has", "the"],
        ["this"] = ["is", "was", "will", "would", "can", "could", "has", "does", "looks", "means", "one", "way", "time", "morning"],
        ["that"] = ["is", "was", "would", "will", "can", "could", "has", "the", "you", "i", "we", "it", "sounds", "looks", "makes"],
        ["there"] = ["is", "are", "was", "were", "will", "would", "should", "can", "might", "has"],
        ["not"] = ["a", "the", "be", "have", "do", "only", "sure", "just", "going", "really", "very", "too", "even", "yet", "enough"],
        ["to"] = ["be", "do", "have", "get", "make", "go", "see", "take", "come", "know", "say", "tell", "give", "find", "help", "work", "try", "the", "a", "my", "your"],
        ["in"] = ["the", "a", "my", "your", "this", "that", "order", "fact", "case", "front", "general", "terms"],
        ["for"] = ["the", "a", "you", "me", "us", "them", "it", "this", "that", "example", "now", "sure"],
        ["with"] = ["the", "a", "my", "your", "you", "me", "us", "them", "it", "this", "that"],
        ["on"] = ["the", "a", "my", "your", "this", "that", "it", "top", "time"],
        ["at"] = ["the", "a", "least", "all", "home", "work", "first", "last", "night", "this", "that"],
        ["but"] = ["i", "it", "the", "that", "this", "we", "you", "he", "she", "they", "not", "also", "still", "anyway"],
        ["so"] = ["i", "it", "the", "that", "we", "you", "much", "many", "far", "what", "how", "long", "good"],
        ["if"] = ["you", "i", "we", "they", "it", "the", "that", "there", "not", "so"],
        ["just"] = ["a", "the", "want", "need", "like", "got", "had", "said", "let", "wanted", "because", "one", "about", "in", "to"],
        ["very"] = ["good", "much", "well", "nice", "happy", "important", "interesting", "different", "long", "hard", "easy", "sorry"],
        ["really"] = ["good", "like", "want", "need", "think", "hope", "appreciate", "sorry", "nice", "great", "important", "well"],
        ["going"] = ["to", "on", "well", "home", "back", "out", "through", "forward", "anywhere"],
        ["want"] = ["to", "you", "me", "it", "the", "a", "that", "this", "something", "anything"],
        ["need"] = ["to", "a", "the", "you", "me", "it", "some", "more", "your", "help"],
        ["like"] = ["to", "a", "the", "it", "that", "this", "you", "me", "what"],
        ["think"] = ["that", "about", "it", "so", "of", "the", "we", "you", "i"],
        ["know"] = ["that", "what", "how", "about", "if", "the", "it", "you", "i", "where", "when", "why"],
        ["get"] = ["a", "the", "it", "to", "out", "up", "back", "in", "some", "more", "your"],
        ["make"] = ["a", "the", "it", "sure", "sense", "up", "me", "you"],
        ["go"] = ["to", "ahead", "back", "out", "home", "on", "for", "with", "there"],
        ["come"] = ["to", "back", "in", "on", "out", "up", "from", "here", "with", "home"],
        ["see"] = ["you", "if", "what", "the", "it", "how", "that", "this"],
        ["take"] = ["a", "the", "it", "care", "off", "out", "up", "my", "your", "some", "time"],
        ["give"] = ["me", "you", "it", "the", "a", "up", "back"],
        ["tell"] = ["me", "you", "the", "them", "him", "her", "us", "about"],
        ["let"] = ["me", "us", "the", "it", "them", "him", "her"],
        ["thank"] = ["you", "god"],
        ["good"] = ["morning", "afternoon", "evening", "night", "luck", "job", "idea", "point", "news", "time", "day", "one"],
        ["look"] = ["at", "for", "like", "forward", "into", "up", "out", "good", "great"],
        ["feel"] = ["like", "free", "good", "bad", "better", "the", "that", "so"],
        ["try"] = ["to", "it", "the", "a", "again", "not", "and", "out", "harder"],
        ["work"] = ["on", "out", "with", "for", "in", "hard", "well", "together"],
        ["put"] = ["it", "the", "a", "in", "on", "up", "down", "out", "together", "away"],
        ["keep"] = ["it", "the", "up", "in", "going", "your", "me", "an"],
        ["find"] = ["a", "the", "it", "out", "something", "your", "my", "what"],
        ["help"] = ["me", "you", "us", "them", "with", "the", "it"],
        ["start"] = ["a", "the", "with", "to", "over", "from", "by"],
        ["run"] = ["the", "a", "it", "out", "into", "away", "for"],
        ["write"] = ["a", "the", "it", "to", "about", "down", "up"],
        ["read"] = ["the", "a", "it", "about", "this", "that", "more"],
        ["send"] = ["me", "you", "it", "the", "a", "an", "them"],
        ["call"] = ["me", "you", "it", "the", "a", "them", "back"],
        ["ask"] = ["me", "you", "for", "about", "the", "a", "if"],
        ["turn"] = ["on", "off", "the", "it", "around", "out", "up", "down"],
        ["move"] = ["to", "the", "it", "on", "out", "forward", "back"],
        ["play"] = ["the", "a", "it", "with", "music", "games"],
        ["pay"] = ["for", "the", "it", "me", "attention", "off"],
        ["open"] = ["the", "a", "it", "up", "your", "my"],
        ["close"] = ["the", "it", "to", "your", "enough"],
        ["leave"] = ["the", "it", "me", "a", "now", "here"],
        ["bring"] = ["me", "the", "it", "a", "back", "your"],
        ["buy"] = ["a", "the", "it", "some", "me", "one"],
        ["wait"] = ["for", "a", "until", "here", "please"],
        ["stop"] = ["it", "the", "that", "doing", "being"],
        ["watch"] = ["the", "it", "out", "this", "a"],
        ["sit"] = ["down", "here", "there", "in", "on"],
        ["stand"] = ["up", "by", "out", "for", "back"],
        ["talk"] = ["to", "about", "with", "the"],
        ["walk"] = ["to", "the", "away", "in", "home"],
        ["pick"] = ["up", "the", "a", "it", "out", "one"],
        ["use"] = ["the", "a", "it", "this", "that", "your", "my"],
        ["show"] = ["me", "you", "the", "it", "them", "up"],
        ["hear"] = ["you", "me", "the", "it", "about", "from", "that"],
        ["been"] = ["a", "to", "in", "the", "there", "here", "doing", "working", "going"],
        ["had"] = ["a", "to", "the", "been", "no", "it", "some", "enough", "never"],
        ["has"] = ["been", "a", "the", "to", "not", "it", "no", "never", "always"],
        ["did"] = ["you", "not", "it", "the", "he", "she", "they", "we", "that"],
        ["does"] = ["not", "it", "the", "he", "she", "that", "this"],
        ["am"] = ["i", "a", "the", "not", "going", "so", "very", "here", "sorry"],
        ["were"] = ["you", "we", "they", "not", "the", "going", "there", "able"],
        ["won't"] = ["be", "have", "do", "let", "get", "make", "take", "work", "happen"],
        ["can't"] = ["be", "do", "have", "get", "make", "see", "find", "believe", "wait", "stop"],
        ["isn't"] = ["it", "that", "the", "a", "there", "he", "she", "going", "working"],
        ["aren't"] = ["you", "we", "they", "the", "there", "going"],
        ["wasn't"] = ["a", "the", "it", "that", "there", "able", "sure", "going"],
        ["wouldn't"] = ["be", "have", "do", "it", "that", "want", "let", "mind"],
        ["couldn't"] = ["be", "have", "do", "get", "find", "see", "believe", "help"],
        ["shouldn't"] = ["be", "have", "do", "we", "you", "that"],
        ["my"] = ["name", "first", "last", "own", "best", "new", "old", "friend", "phone", "email", "work", "life", "family", "house"],
        ["your"] = ["name", "first", "last", "own", "best", "new", "old", "friend", "phone", "email", "work", "life", "family"],
        ["his"] = ["name", "first", "last", "own", "best", "new", "old", "friend", "phone", "work", "life", "family"],
        ["her"] = ["name", "first", "last", "own", "best", "new", "old", "friend", "phone", "work", "life", "family"],
        ["its"] = ["own", "best", "first", "last", "way", "name"],
        ["our"] = ["own", "best", "first", "last", "new", "old", "team", "work", "family"],
        ["their"] = ["own", "best", "first", "last", "new", "old", "work", "family", "way"],
        ["some"] = ["of", "people", "things", "time", "way", "kind", "more", "new"],
        ["any"] = ["of", "time", "more", "other", "way", "kind", "questions"],
        ["all"] = ["of", "the", "right", "day", "time", "good", "about", "over"],
        ["more"] = ["than", "about", "time", "information", "or", "like", "of", "and"],
        ["other"] = ["than", "people", "things", "way", "side", "hand"],
        ["new"] = ["to", "and", "one", "year", "way", "york"],
        ["about"] = ["the", "it", "that", "this", "a", "what", "how", "my", "your", "to"],
        ["after"] = ["the", "a", "that", "all", "this"],
        ["before"] = ["the", "i", "you", "we", "it", "that"],
        ["now"] = ["i", "the", "that", "it", "we", "you", "and", "is"],
        ["then"] = ["i", "the", "it", "we", "you", "he", "she", "they"],
        ["here"] = ["is", "are", "in", "to", "for", "we", "you"],
        ["still"] = ["have", "a", "the", "not", "in", "be", "here"],
        ["also"] = ["a", "the", "have", "be", "like", "need", "want"],
        ["back"] = ["to", "in", "and", "up", "on", "from", "home"],
        ["only"] = ["a", "the", "one", "if", "to", "in", "way"],
        ["even"] = ["if", "though", "more", "the", "a", "when"],
    };
}