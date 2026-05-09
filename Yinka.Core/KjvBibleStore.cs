using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Yinka;

/// <summary>Loads bundled KJV (thiagobodruk/bible <c>en_kjv.json</c>) for fully offline verse lookup.</summary>
public sealed class KjvBibleStore
{
    private static readonly Regex BraceNotes = new(@"\{[^}]*\}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private readonly Dictionary<string, List<List<string>>> _booksByAbbrev = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _slugToAbbrev;

    public KjvBibleStore()
    {
        _slugToAbbrev = BuildSlugToAbbrevMap();
    }

    public bool IsLoaded { get; private set; }

    public string? LoadError { get; private set; }

    public void LoadFromFile(string jsonPath)
    {
        LoadError = null;
        IsLoaded = false;
        _booksByAbbrev.Clear();

        if (!File.Exists(jsonPath))
        {
            LoadError = $"Missing KJV data file: {jsonPath}";
            return;
        }

        try
        {
            using var reader = new StreamReader(jsonPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false), detectEncodingFromByteOrderMarks: true);
            var json = reader.ReadToEnd();
            var books = JsonSerializer.Deserialize<List<BookJson>>(json, JsonOptions);
            if (books is null || books.Count == 0)
            {
                LoadError = "KJV JSON was empty.";
                return;
            }

            foreach (var b in books)
            {
                if (string.IsNullOrWhiteSpace(b.Abbrev) || b.Chapters is null)
                    continue;
                _booksByAbbrev[b.Abbrev.Trim()] = b.Chapters;
            }

            IsLoaded = true;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
    }

    public VersePayload? GetPassage(ParsedReference reference)
    {
        if (!IsLoaded)
            return null;

        if (!_slugToAbbrev.TryGetValue(reference.BookSlug, out var abbrev))
            return null;
        if (!_booksByAbbrev.TryGetValue(abbrev, out var chapters))
            return null;

        var chIndex = reference.Chapter - 1;
        if (chIndex < 0 || chIndex >= chapters.Count)
            return null;

        var chapterVerses = chapters[chIndex];
        var end = reference.VerseEnd ?? reference.VerseStart;
        var start = Math.Min(reference.VerseStart, end);
        var stop = Math.Max(reference.VerseStart, end);

        var pieces = new List<string>();
        for (var v = start; v <= stop; v++)
        {
            var vi = v - 1;
            if (vi < 0 || vi >= chapterVerses.Count)
                continue;
            var raw = chapterVerses[vi] ?? "";
            pieces.Add(CleanVerseText(raw));
        }

        if (pieces.Count == 0)
            return null;

        var bookName = TitleFromAbbrev(abbrev);
        var refLabel = stop == start
            ? $"{bookName} {reference.Chapter}:{start}"
            : $"{bookName} {reference.Chapter}:{start}-{stop}";

        return new VersePayload(refLabel, string.Join(" ", pieces), "kjv", "King James Version");
    }

    private static string CleanVerseText(string s)
    {
        s = BraceNotes.Replace(s, " ");
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        return s.Trim();
    }

    private static string TitleFromAbbrev(string abbrev) =>
        AbbrevTitles.TryGetValue(abbrev, out var t) ? t : abbrev.ToUpperInvariant();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class BookJson
    {
        [JsonPropertyName("abbrev")]
        public string? Abbrev { get; set; }

        [JsonPropertyName("chapters")]
        public List<List<string>>? Chapters { get; set; }
    }

    private static Dictionary<string, string> BuildSlugToAbbrevMap() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["genesis"] = "gn", ["exodus"] = "ex", ["leviticus"] = "lv", ["numbers"] = "nm", ["deuteronomy"] = "dt",
            ["joshua"] = "js", ["judges"] = "jud", ["ruth"] = "rt",
            ["1samuel"] = "1sm", ["2samuel"] = "2sm", ["1kings"] = "1kgs", ["2kings"] = "2kgs",
            ["1chronicles"] = "1ch", ["2chronicles"] = "2ch", ["ezra"] = "ezr", ["nehemiah"] = "ne", ["esther"] = "et",
            ["job"] = "job", ["psalms"] = "ps", ["proverbs"] = "prv", ["ecclesiastes"] = "ec", ["songofsolomon"] = "so",
            ["isaiah"] = "is", ["jeremiah"] = "jr", ["lamentations"] = "lm", ["ezekiel"] = "ez", ["daniel"] = "dn",
            ["hosea"] = "ho", ["joel"] = "jl", ["amos"] = "am", ["obadiah"] = "ob", ["jonah"] = "jn",
            ["micah"] = "mi", ["nahum"] = "na", ["habakkuk"] = "hk", ["zephaniah"] = "zp", ["haggai"] = "hg",
            ["zechariah"] = "zc", ["malachi"] = "ml",
            ["matthew"] = "mt", ["mark"] = "mk", ["luke"] = "lk", ["john"] = "jo", ["acts"] = "act",
            ["romans"] = "rm", ["1corinthians"] = "1co", ["2corinthians"] = "2co", ["galatians"] = "gl", ["ephesians"] = "eph",
            ["philippians"] = "ph", ["colossians"] = "cl", ["1thessalonians"] = "1ts", ["2thessalonians"] = "2ts",
            ["1timothy"] = "1tm", ["2timothy"] = "2tm", ["titus"] = "tt", ["philemon"] = "phm", ["hebrews"] = "hb",
            ["james"] = "jm", ["1peter"] = "1pe", ["2peter"] = "2pe", ["1john"] = "1jo", ["2john"] = "2jo", ["3john"] = "3jo",
            ["jude"] = "jd", ["revelation"] = "re",
        };

    private static readonly Dictionary<string, string> AbbrevTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gn"] = "Genesis", ["ex"] = "Exodus", ["lv"] = "Leviticus", ["nm"] = "Numbers", ["dt"] = "Deuteronomy",
        ["js"] = "Joshua", ["jud"] = "Judges", ["rt"] = "Ruth",
        ["1sm"] = "1 Samuel", ["2sm"] = "2 Samuel", ["1kgs"] = "1 Kings", ["2kgs"] = "2 Kings",
        ["1ch"] = "1 Chronicles", ["2ch"] = "2 Chronicles", ["ezr"] = "Ezra", ["ne"] = "Nehemiah", ["et"] = "Esther",
        ["job"] = "Job", ["ps"] = "Psalms", ["prv"] = "Proverbs", ["ec"] = "Ecclesiastes", ["so"] = "Song of Solomon",
        ["is"] = "Isaiah", ["jr"] = "Jeremiah", ["lm"] = "Lamentations", ["ez"] = "Ezekiel", ["dn"] = "Daniel",
        ["ho"] = "Hosea", ["jl"] = "Joel", ["am"] = "Amos", ["ob"] = "Obadiah", ["jn"] = "Jonah",
        ["mi"] = "Micah", ["na"] = "Nahum", ["hk"] = "Habakkuk", ["zp"] = "Zephaniah", ["hg"] = "Haggai",
        ["zc"] = "Zechariah", ["ml"] = "Malachi",
        ["mt"] = "Matthew", ["mk"] = "Mark", ["lk"] = "Luke", ["jo"] = "John", ["act"] = "Acts",
        ["rm"] = "Romans", ["1co"] = "1 Corinthians", ["2co"] = "2 Corinthians", ["gl"] = "Galatians", ["eph"] = "Ephesians",
        ["ph"] = "Philippians", ["cl"] = "Colossians", ["1ts"] = "1 Thessalonians", ["2ts"] = "2 Thessalonians",
        ["1tm"] = "1 Timothy", ["2tm"] = "2 Timothy", ["tt"] = "Titus", ["phm"] = "Philemon", ["hb"] = "Hebrews",
        ["jm"] = "James", ["1pe"] = "1 Peter", ["2pe"] = "2 Peter", ["1jo"] = "1 John", ["2jo"] = "2 John", ["3jo"] = "3 John",
        ["jd"] = "Jude", ["re"] = "Revelation",
    };
}

public sealed record VersePayload(string Reference, string Text, string TranslationId, string TranslationName);
