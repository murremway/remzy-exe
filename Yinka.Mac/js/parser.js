// Direct Bible reference detection (port of Yinka.Core/BibleReferenceParser.cs).
// Detects spoken-style English references ("John 3:16", "Romans chapter 8 verse 28",
// "Philippians 4:6-7", etc.). Pure JS, no dependencies.

const BOOK_ROWS = [
  ["first corinthians", "1corinthians"], ["1 corinthians", "1corinthians"], ["1 cor", "1corinthians"], ["1cor", "1corinthians"],
  ["second corinthians", "2corinthians"], ["2 corinthians", "2corinthians"], ["2 cor", "2corinthians"], ["2cor", "2corinthians"],
  ["first thessalonians", "1thessalonians"], ["1 thessalonians", "1thessalonians"], ["1 thess", "1thessalonians"], ["1thess", "1thessalonians"],
  ["second thessalonians", "2thessalonians"], ["2 thessalonians", "2thessalonians"], ["2 thess", "2thessalonians"], ["2thess", "2thessalonians"],
  ["first chronicles", "1chronicles"], ["1 chronicles", "1chronicles"], ["1ch", "1chronicles"],
  ["second chronicles", "2chronicles"], ["2 chronicles", "2chronicles"], ["2ch", "2chronicles"],
  ["first samuel", "1samuel"], ["1 samuel", "1samuel"], ["1 sam", "1samuel"], ["1sam", "1samuel"],
  ["second samuel", "2samuel"], ["2 samuel", "2samuel"], ["2 sam", "2samuel"], ["2sam", "2samuel"],
  ["first kings", "1kings"], ["1 kings", "1kings"], ["1ki", "1kings"],
  ["second kings", "2kings"], ["2 kings", "2kings"], ["2ki", "2kings"],
  ["first timothy", "1timothy"], ["1 timothy", "1timothy"], ["1 tim", "1timothy"], ["1tim", "1timothy"],
  ["second timothy", "2timothy"], ["2 timothy", "2timothy"], ["2 tim", "2timothy"], ["2tim", "2timothy"],
  ["first peter", "1peter"], ["1 peter", "1peter"], ["1 pet", "1peter"], ["1pe", "1peter"],
  ["second peter", "2peter"], ["2 peter", "2peter"], ["2 pet", "2peter"], ["2pe", "2peter"],
  ["first john", "1john"], ["1 john", "1john"], ["1 jn", "1john"], ["1jn", "1john"],
  ["second john", "2john"], ["2 john", "2john"], ["2 jn", "2john"], ["2jn", "2john"],
  ["third john", "3john"], ["3 john", "3john"], ["3 jn", "3john"], ["3jn", "3john"],
  ["song of solomon", "songofsolomon"], ["song of songs", "songofsolomon"],
  ["ecclesiastes", "ecclesiastes"], ["deuteronomy", "deuteronomy"], ["philippians", "philippians"],
  ["lamentations", "lamentations"], ["leviticus", "leviticus"], ["numbers", "numbers"],
  ["revelation", "revelation"],
  ["galatians", "galatians"], ["ephesians", "ephesians"], ["colossians", "colossians"],
  ["romans", "romans"], ["genesis", "genesis"], ["exodus", "exodus"],
  ["gen", "genesis"], ["exo", "exodus"], ["lev", "leviticus"], ["num", "numbers"],
  ["deu", "deuteronomy"], ["jos", "joshua"], ["jdg", "judges"], ["neh", "nehemiah"],
  ["psa", "psalms"], ["ecc", "ecclesiastes"], ["isa", "isaiah"], ["jer", "jeremiah"],
  ["eze", "ezekiel"], ["dan", "daniel"], ["hos", "hosea"], ["oba", "obadiah"],
  ["jon", "jonah"], ["mic", "micah"], ["nah", "nahum"], ["hab", "habakkuk"],
  ["zep", "zephaniah"], ["hag", "haggai"], ["zec", "zechariah"], ["mal", "malachi"],
  ["mat", "matthew"], ["mrk", "mark"], ["luk", "luke"], ["jhn", "john"], ["act", "acts"],
  ["rom", "romans"], ["php", "philippians"], ["col", "colossians"], ["jas", "james"],
  ["tit", "titus"], ["phm", "philemon"], ["heb", "hebrews"], ["rev", "revelation"],
  ["joshua", "joshua"], ["judges", "judges"], ["ruth", "ruth"], ["ezra", "ezra"],
  ["esther", "esther"], ["nehemiah", "nehemiah"], ["job", "job"], ["psalms", "psalms"],
  ["psalm", "psalms"], ["proverbs", "proverbs"], ["isaiah", "isaiah"], ["jeremiah", "jeremiah"],
  ["ezekiel", "ezekiel"], ["daniel", "daniel"], ["hosea", "hosea"], ["joel", "joel"],
  ["amos", "amos"], ["obadiah", "obadiah"], ["jonah", "jonah"], ["micah", "micah"],
  ["nahum", "nahum"], ["habakkuk", "habakkuk"], ["zephaniah", "zephaniah"], ["haggai", "haggai"],
  ["zechariah", "zechariah"], ["malachi", "malachi"], ["matthew", "matthew"], ["mark", "mark"],
  ["luke", "luke"], ["john", "john"], ["acts", "acts"], ["james", "james"], ["jude", "jude"],
  ["hebrews", "hebrews"], ["titus", "titus"], ["philemon", "philemon"],
];

// Deduped + sorted by descending alias length so longer aliases match first.
const BOOKS_SORTED = (() => {
  const seen = new Set();
  const rows = [];
  for (const [alias, slug] of BOOK_ROWS) {
    const key = `${alias}|${slug}`;
    if (seen.has(key)) continue;
    seen.add(key);
    rows.push({ alias, slug });
  }
  rows.sort((a, b) =>
    b.alias.length - a.alias.length || a.alias.localeCompare(b.alias)
  );
  return rows;
})();

// Tail after a book name: "chapter N (verse) M", "N:M", "N;M", "N, M", "N verse M", optional "-K".
const AFTER_BOOK_RE =
  /^\s*,?\s*(?:(?:chapter\s+(\d+)\s+(?:verse\s+)?(\d+))|(?:(\d+)\s*(?:[:;]|,|\s+verse\s+)\s*(\d+)))(?:\s*[-–]\s*(\d+))?/i;

function escapeRegex(str) {
  return str.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Find every direct Bible reference inside `text`.
 * Returns: [{ bookSlug, chapter, verseStart, verseEnd, matchedText, displayReference, source:'direct' }]
 */
export function findReferences(text) {
  if (!text || !text.trim()) return [];
  const lower = text.toLowerCase();
  const found = [];

  for (const { alias, slug } of BOOKS_SORTED) {
    const pattern = new RegExp(
      `(?<!\\p{L})${escapeRegex(alias)}(?!\\p{L})`,
      "gu"
    );
    let m;
    while ((m = pattern.exec(lower)) !== null) {
      const tailStart = m.index + m[0].length;
      if (tailStart >= lower.length) continue;
      const tail = lower.slice(tailStart);
      const tailMatch = AFTER_BOOK_RE.exec(tail);
      if (!tailMatch) continue;

      const chapterStr = tailMatch[1] || tailMatch[3];
      const verseStr = tailMatch[2] || tailMatch[4];
      const chapter = parseInt(chapterStr, 10);
      const verseStart = parseInt(verseStr, 10);
      if (!Number.isFinite(chapter) || !Number.isFinite(verseStart)) continue;
      const verseEnd = tailMatch[5] ? parseInt(tailMatch[5], 10) : null;

      const endIdx = tailStart + tailMatch[0].length;
      const matchedText = text
        .substring(m.index, Math.min(endIdx, text.length))
        .trim();

      found.push({
        bookSlug: slug,
        chapter,
        verseStart,
        verseEnd,
        matchedText,
        displayReference: matchedText,
        source: "direct",
        startIndex: m.index,
        endIndex: endIdx,
      });
    }
  }

  return dedupe(found);
}

function dedupe(items) {
  const seen = new Set();
  const out = [];
  // Sort by shorter matched text first (prefer the simpler match form).
  const ordered = [...items].sort(
    (a, b) => a.matchedText.length - b.matchedText.length
  );
  for (const item of ordered) {
    const key = `${item.bookSlug}:${item.chapter}:${item.verseStart}:${item.verseEnd ?? ""}`;
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(item);
  }
  return out;
}

export const BOOK_ALIASES = BOOKS_SORTED;
