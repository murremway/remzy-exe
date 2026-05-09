// Offline KJV store (port of Yinka.Core/KjvBibleStore.cs).
// Loads ../Data/en_kjv.json (thiagobodruk/bible shape: [{abbrev, chapters:[[verse,...]]}]).

const SLUG_TO_ABBREV = {
  genesis: "gn", exodus: "ex", leviticus: "lv", numbers: "nm", deuteronomy: "dt",
  joshua: "js", judges: "jud", ruth: "rt",
  "1samuel": "1sm", "2samuel": "2sm", "1kings": "1kgs", "2kings": "2kgs",
  "1chronicles": "1ch", "2chronicles": "2ch", ezra: "ezr", nehemiah: "ne", esther: "et",
  job: "job", psalms: "ps", proverbs: "prv", ecclesiastes: "ec", songofsolomon: "so",
  isaiah: "is", jeremiah: "jr", lamentations: "lm", ezekiel: "ez", daniel: "dn",
  hosea: "ho", joel: "jl", amos: "am", obadiah: "ob", jonah: "jn",
  micah: "mi", nahum: "na", habakkuk: "hk", zephaniah: "zp", haggai: "hg",
  zechariah: "zc", malachi: "ml",
  matthew: "mt", mark: "mk", luke: "lk", john: "jo", acts: "act",
  romans: "rm", "1corinthians": "1co", "2corinthians": "2co", galatians: "gl", ephesians: "eph",
  philippians: "ph", colossians: "cl", "1thessalonians": "1ts", "2thessalonians": "2ts",
  "1timothy": "1tm", "2timothy": "2tm", titus: "tt", philemon: "phm", hebrews: "hb",
  james: "jm", "1peter": "1pe", "2peter": "2pe", "1john": "1jo", "2john": "2jo", "3john": "3jo",
  jude: "jd", revelation: "re",
};

const ABBREV_TO_TITLE = {
  gn: "Genesis", ex: "Exodus", lv: "Leviticus", nm: "Numbers", dt: "Deuteronomy",
  js: "Joshua", jud: "Judges", rt: "Ruth",
  "1sm": "1 Samuel", "2sm": "2 Samuel", "1kgs": "1 Kings", "2kgs": "2 Kings",
  "1ch": "1 Chronicles", "2ch": "2 Chronicles", ezr: "Ezra", ne: "Nehemiah", et: "Esther",
  job: "Job", ps: "Psalms", prv: "Proverbs", ec: "Ecclesiastes", so: "Song of Solomon",
  is: "Isaiah", jr: "Jeremiah", lm: "Lamentations", ez: "Ezekiel", dn: "Daniel",
  ho: "Hosea", jl: "Joel", am: "Amos", ob: "Obadiah", jn: "Jonah",
  mi: "Micah", na: "Nahum", hk: "Habakkuk", zp: "Zephaniah", hg: "Haggai",
  zc: "Zechariah", ml: "Malachi",
  mt: "Matthew", mk: "Mark", lk: "Luke", jo: "John", act: "Acts",
  rm: "Romans", "1co": "1 Corinthians", "2co": "2 Corinthians", gl: "Galatians", eph: "Ephesians",
  ph: "Philippians", cl: "Colossians", "1ts": "1 Thessalonians", "2ts": "2 Thessalonians",
  "1tm": "1 Timothy", "2tm": "2 Timothy", tt: "Titus", phm: "Philemon", hb: "Hebrews",
  jm: "James", "1pe": "1 Peter", "2pe": "2 Peter", "1jo": "1 John", "2jo": "2 John", "3jo": "3 John",
  jd: "Jude", re: "Revelation",
};

const ABBREV_ORDER = [
  "gn","ex","lv","nm","dt","js","jud","rt","1sm","2sm","1kgs","2kgs","1ch","2ch","ezr","ne","et",
  "job","ps","prv","ec","so","is","jr","lm","ez","dn","ho","jl","am","ob","jn","mi","na","hk","zp","hg","zc","ml",
  "mt","mk","lk","jo","act","rm","1co","2co","gl","eph","ph","cl","1ts","2ts","1tm","2tm","tt","phm","hb","jm","1pe","2pe","1jo","2jo","3jo","jd","re",
];

const BRACE_NOTES = /\{[^}]*\}/g;

class KjvStore {
  constructor() {
    this.loaded = false;
    this.error = null;
    this.byAbbrev = new Map();
  }

  /**
   * Load the KJV JSON from one of several candidate locations. The .app
   * bundle ships it at `Data/en_kjv.json` (alongside index.html); the dev
   * launcher serves the repo root and exposes it at `../Data/en_kjv.json`.
   * A single explicit URL can also be passed.
   */
  async load(jsonUrl) {
    this.loaded = false;
    this.error = null;
    const candidates = jsonUrl
      ? [jsonUrl]
      : ["Data/en_kjv.json", "../Data/en_kjv.json"];
    let lastError = null;
    for (const url of candidates) {
      try {
        const res = await fetch(url);
        if (!res.ok) {
          lastError = `Failed to fetch KJV from ${url} (${res.status})`;
          continue;
        }
        const data = await res.json();
        if (!Array.isArray(data) || data.length === 0) {
          lastError = `KJV JSON at ${url} was empty`;
          continue;
        }
        for (const book of data) {
          if (!book || !book.abbrev || !Array.isArray(book.chapters)) continue;
          this.byAbbrev.set(book.abbrev.trim(), book.chapters);
        }
        this.loaded = true;
        return;
      } catch (err) {
        lastError = `Could not load KJV from ${url}: ${err && err.message ? err.message : err}`;
      }
    }
    this.error = lastError ?? "KJV JSON not found";
  }

  /**
   * Return a verse payload for a parsed reference {bookSlug, chapter, verseStart, verseEnd?}
   * or null if the passage cannot be resolved.
   */
  getPassage(ref) {
    if (!this.loaded || !ref) return null;
    const abbrev = SLUG_TO_ABBREV[ref.bookSlug];
    if (!abbrev) return null;
    const chapters = this.byAbbrev.get(abbrev);
    if (!chapters) return null;

    const chIndex = ref.chapter - 1;
    if (chIndex < 0 || chIndex >= chapters.length) return null;
    const chapterVerses = chapters[chIndex];

    const end = ref.verseEnd ?? ref.verseStart;
    const start = Math.min(ref.verseStart, end);
    const stop = Math.max(ref.verseStart, end);

    const pieces = [];
    for (let v = start; v <= stop; v++) {
      const idx = v - 1;
      if (idx < 0 || idx >= chapterVerses.length) continue;
      const raw = chapterVerses[idx] ?? "";
      pieces.push(cleanVerse(raw));
    }
    if (pieces.length === 0) return null;

    const bookName = ABBREV_TO_TITLE[abbrev] ?? abbrev.toUpperCase();
    const reference =
      stop === start
        ? `${bookName} ${ref.chapter}:${start}`
        : `${bookName} ${ref.chapter}:${start}-${stop}`;

    return {
      reference,
      bookName,
      bookSlug: ref.bookSlug,
      chapter: ref.chapter,
      verseStart: start,
      verseEnd: stop,
      text: pieces.join(" "),
      translationId: "kjv",
      translationName: "King James Version",
    };
  }

  /** All canonical book metadata (in canonical order) for the Book mode picker. */
  listBooks() {
    return ABBREV_ORDER.filter((a) => this.byAbbrev.has(a)).map((abbrev) => ({
      abbrev,
      name: ABBREV_TO_TITLE[abbrev] ?? abbrev,
      slug: Object.keys(SLUG_TO_ABBREV).find(
        (k) => SLUG_TO_ABBREV[k] === abbrev
      ),
      chapterCount: this.byAbbrev.get(abbrev).length,
    }));
  }

  chapterVerses(slugOrAbbrev, chapter) {
    const abbrev = SLUG_TO_ABBREV[slugOrAbbrev] ?? slugOrAbbrev;
    const chapters = this.byAbbrev.get(abbrev);
    if (!chapters) return [];
    const idx = chapter - 1;
    if (idx < 0 || idx >= chapters.length) return [];
    return chapters[idx].map((raw) => cleanVerse(raw));
  }

  /** Iterate every verse in canonical order. Used by context (keyword) search. */
  *iterAllVerses() {
    for (const abbrev of ABBREV_ORDER) {
      const chapters = this.byAbbrev.get(abbrev);
      if (!chapters) continue;
      const bookName = ABBREV_TO_TITLE[abbrev];
      const slug = Object.keys(SLUG_TO_ABBREV).find(
        (k) => SLUG_TO_ABBREV[k] === abbrev
      );
      for (let c = 0; c < chapters.length; c++) {
        const verses = chapters[c];
        for (let v = 0; v < verses.length; v++) {
          yield {
            bookName,
            bookSlug: slug,
            chapter: c + 1,
            verse: v + 1,
            text: cleanVerse(verses[v] ?? ""),
          };
        }
      }
    }
  }
}

function cleanVerse(s) {
  let out = (s ?? "").replace(BRACE_NOTES, " ");
  while (out.includes("  ")) out = out.replace("  ", " ");
  return out.trim();
}

export const kjvStore = new KjvStore();
export { ABBREV_TO_TITLE, SLUG_TO_ABBREV };
