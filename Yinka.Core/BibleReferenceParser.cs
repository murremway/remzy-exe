using System.Globalization;
using System.Text.RegularExpressions;

namespace Yinka;

/// <summary>Detects spoken-style English Bible references (explicit references only).</summary>
public static class BibleReferenceParser
{
    private static readonly Regex AfterBookRegex = new(
        @"^\s*,?\s*(?:(?:chapter\s+(\d+)\s+(?:verse\s+)?(\d+))|(?:(\d+)\s*(?:[:;]|,|\s+verse\s+)\s*(\d+)))(?:\s*[-–]\s*(\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private static readonly (string Alias, string Slug)[] BooksSortedByLength;

    static BibleReferenceParser()
    {
        var set = new HashSet<(string, string)>();
        foreach (var row in BookRows)
            set.Add((row.Alias.ToLowerInvariant(), row.Slug));

        BooksSortedByLength = set
            .OrderByDescending(x => x.Item1.Length)
            .ThenBy(x => x.Item1)
            .ToArray();
    }

    public static IReadOnlyList<ParsedReference> FindReferences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<ParsedReference>();

        var lower = text.ToLowerInvariant();
        var results = new List<ParsedReference>();

        foreach (var (alias, slug) in BooksSortedByLength)
        {
            var pattern = $@"(?<!\p{{L}}){Regex.Escape(alias)}(?!\p{{L}})";
            foreach (Match m in Regex.Matches(lower, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500)))
            {
                var tailStart = m.Index + m.Length;
                if (tailStart >= lower.Length)
                    continue;

                var tail = lower[tailStart..];
                var tailMatch = AfterBookRegex.Match(tail);
                if (!tailMatch.Success)
                    continue;

                var chapterStr = tailMatch.Groups[1].Success ? tailMatch.Groups[1].Value : tailMatch.Groups[3].Value;
                var verseStr = tailMatch.Groups[2].Success ? tailMatch.Groups[2].Value : tailMatch.Groups[4].Value;
                if (!int.TryParse(chapterStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chapter))
                    continue;
                if (!int.TryParse(verseStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var verseStart))
                    continue;

                int? verseEnd = null;
                if (tailMatch.Groups[5].Success &&
                    int.TryParse(tailMatch.Groups[5].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ve))
                    verseEnd = ve;

                var endIdx = tailStart + tailMatch.Length;
                var matchedOriginal = text.Substring(m.Index, Math.Min(endIdx, text.Length) - m.Index).Trim();
                results.Add(new ParsedReference(slug, chapter, verseStart, verseEnd, matchedOriginal));
            }
        }

        return Deduplicate(results);
    }

    private static IReadOnlyList<ParsedReference> Deduplicate(List<ParsedReference> items)
    {
        var seen = new HashSet<string>();
        var list = new List<ParsedReference>();
        foreach (var p in items.OrderBy(x => x.MatchedText.Length))
        {
            var key = $"{p.BookSlug}:{p.Chapter}:{p.VerseStart}:{p.VerseEnd}";
            if (seen.Add(key))
                list.Add(p);
        }
        return list;
    }

    private static readonly (string Alias, string Slug)[] BookRows =
    [
        ("first corinthians", "1corinthians"), ("1 corinthians", "1corinthians"), ("1 cor", "1corinthians"), ("1cor", "1corinthians"),
        ("second corinthians", "2corinthians"), ("2 corinthians", "2corinthians"), ("2 cor", "2corinthians"), ("2cor", "2corinthians"),
        ("first thessalonians", "1thessalonians"), ("1 thessalonians", "1thessalonians"), ("1 thess", "1thessalonians"), ("1thess", "1thessalonians"),
        ("second thessalonians", "2thessalonians"), ("2 thessalonians", "2thessalonians"), ("2 thess", "2thessalonians"), ("2thess", "2thessalonians"),
        ("first chronicles", "1chronicles"), ("1 chronicles", "1chronicles"), ("1ch", "1chronicles"),
        ("second chronicles", "2chronicles"), ("2 chronicles", "2chronicles"), ("2ch", "2chronicles"),
        ("first samuel", "1samuel"), ("1 samuel", "1samuel"), ("1 sam", "1samuel"), ("1sam", "1samuel"),
        ("second samuel", "2samuel"), ("2 samuel", "2samuel"), ("2 sam", "2samuel"), ("2sam", "2samuel"),
        ("first kings", "1kings"), ("1 kings", "1kings"), ("1ki", "1kings"),
        ("second kings", "2kings"), ("2 kings", "2kings"), ("2ki", "2kings"),
        ("first timothy", "1timothy"), ("1 timothy", "1timothy"), ("1 tim", "1timothy"), ("1tim", "1timothy"),
        ("second timothy", "2timothy"), ("2 timothy", "2timothy"), ("2 tim", "2timothy"), ("2tim", "2timothy"),
        ("first peter", "1peter"), ("1 peter", "1peter"), ("1 pet", "1peter"), ("1pe", "1peter"),
        ("second peter", "2peter"), ("2 peter", "2peter"), ("2 pet", "2peter"), ("2pe", "2peter"),
        ("first john", "1john"), ("1 john", "1john"), ("1 jn", "1john"), ("1jn", "1john"),
        ("second john", "2john"), ("2 john", "2john"), ("2 jn", "2john"), ("2jn", "2john"),
        ("third john", "3john"), ("3 john", "3john"), ("3 jn", "3john"), ("3jn", "3john"),
        ("song of solomon", "songofsolomon"), ("song of songs", "songofsolomon"),
        ("ecclesiastes", "ecclesiastes"), ("deuteronomy", "deuteronomy"), ("philippians", "philippians"),
        ("lamentations", "lamentations"), ("leviticus", "leviticus"), ("numbers", "numbers"),
        ("revelation", "revelation"),
        ("galatians", "galatians"), ("ephesians", "ephesians"), ("colossians", "colossians"),
        ("romans", "romans"), ("genesis", "genesis"), ("exodus", "exodus"),
        ("gen", "genesis"), ("exo", "exodus"), ("lev", "leviticus"), ("num", "numbers"),
        ("deu", "deuteronomy"), ("jos", "joshua"), ("jdg", "judges"), ("neh", "nehemiah"),
        ("psa", "psalms"), ("ecc", "ecclesiastes"), ("isa", "isaiah"), ("jer", "jeremiah"),
        ("eze", "ezekiel"), ("dan", "daniel"), ("hos", "hosea"), ("oba", "obadiah"),
        ("jon", "jonah"), ("mic", "micah"), ("nah", "nahum"), ("hab", "habakkuk"),
        ("zep", "zephaniah"), ("hag", "haggai"), ("zec", "zechariah"), ("mal", "malachi"),
        ("mat", "matthew"), ("mrk", "mark"), ("luk", "luke"), ("jhn", "john"), ("act", "acts"),
        ("rom", "romans"), ("php", "philippians"), ("col", "colossians"), ("jas", "james"),
        ("tit", "titus"), ("phm", "philemon"), ("heb", "hebrews"), ("rev", "revelation"),
        ("joshua", "joshua"), ("judges", "judges"), ("ruth", "ruth"), ("ezra", "ezra"),
        ("esther", "esther"), ("nehemiah", "nehemiah"), ("job", "job"), ("psalms", "psalms"),
        ("psalm", "psalms"), ("proverbs", "proverbs"), ("isaiah", "isaiah"), ("jeremiah", "jeremiah"),
        ("ezekiel", "ezekiel"), ("daniel", "daniel"), ("hosea", "hosea"), ("joel", "joel"),
        ("amos", "amos"), ("obadiah", "obadiah"), ("jonah", "jonah"), ("micah", "micah"),
        ("nahum", "nahum"), ("habakkuk", "habakkuk"), ("zephaniah", "zephaniah"), ("haggai", "haggai"),
        ("zechariah", "zechariah"), ("malachi", "malachi"), ("matthew", "matthew"), ("mark", "mark"),
        ("luke", "luke"), ("john", "john"), ("acts", "acts"), ("james", "james"), ("jude", "jude"),
        ("hebrews", "hebrews"), ("titus", "titus"), ("philemon", "philemon"),
    ];
}

public sealed record ParsedReference(string BookSlug, int Chapter, int VerseStart, int? VerseEnd, string MatchedText)
{
    public string DisplayReference => MatchedText;
}
