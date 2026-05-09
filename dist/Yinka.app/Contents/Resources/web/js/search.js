// Scripture Search: Book mode + Context (keyword) mode.
// Book mode parses queries like "John", "John 3", "Ps 23:1", "Romans 8:28-30".
// Context mode does keyword scoring across the whole KJV (offline,
// no embeddings — Pewbeam-style "semantic" requires server-side AI).

import { findReferences } from "./parser.js";
import { kjvStore, SLUG_TO_ABBREV, ABBREV_TO_TITLE } from "./store.js";

const STOP_WORDS = new Set([
  "the","and","of","to","a","in","that","for","is","on","it","with","as","at","by","an","be",
  "this","which","or","from","not","but","are","was","were","have","has","had","he","she","they",
  "his","her","their","its","you","your","i","me","my","we","our","us","do","does","did","so",
  "if","then","than","what","when","where","who","whom","whose","why","how","into","unto","upon",
  "shall","will","would","should","could","may","might","can","yea","verily","ye","thee","thou",
  "thy","thine","saith","said","say","one","also","there","here","let","make","made","every","all",
]);

/** Suggest book names matching a prefix (case-insensitive). */
export function suggestBooks(prefix, limit = 8) {
  const p = prefix.trim().toLowerCase();
  if (!p) return [];
  const books = kjvStore.listBooks();
  const starts = books.filter((b) => b.name.toLowerCase().startsWith(p));
  if (starts.length >= limit) return starts.slice(0, limit);
  const contains = books.filter(
    (b) => !b.name.toLowerCase().startsWith(p) && b.name.toLowerCase().includes(p)
  );
  return [...starts, ...contains].slice(0, limit);
}

/**
 * Resolve a Book-mode query to a verse payload, or to a chapter listing
 * (when the user has typed a book + chapter but no verse yet).
 *
 * Returns one of:
 *   { kind:'verse', payload }
 *   { kind:'chapter', bookName, bookSlug, chapter, verses:[{verse,text}], all }
 *   { kind:'book', bookName, bookSlug, chapterCount }
 *   { kind:'suggestions', books }
 *   { kind:'empty' }
 */
export function resolveBookQuery(query) {
  const q = (query ?? "").trim();
  if (!q) return { kind: "empty" };

  const refs = findReferences(q);
  if (refs.length > 0) {
    const payload = kjvStore.getPassage(refs[0]);
    if (payload) return { kind: "verse", payload };
  }

  // Try "book chapter" (no verse) → chapter listing.
  const bookChapter = /^([1-3]?\s?[a-z][a-z\s.]+?)\s+(\d+)\s*$/i.exec(q);
  if (bookChapter) {
    const bookText = bookChapter[1].trim().replace(/\./g, "");
    const chapter = parseInt(bookChapter[2], 10);
    const slug = resolveBookSlug(bookText);
    if (slug) {
      const verses = kjvStore.chapterVerses(slug, chapter);
      if (verses.length > 0) {
        return {
          kind: "chapter",
          bookName: ABBREV_TO_TITLE[SLUG_TO_ABBREV[slug]],
          bookSlug: slug,
          chapter,
          verses: verses.map((text, i) => ({ verse: i + 1, text })),
          all: verses,
        };
      }
    }
  }

  // Just a book name?
  const slug = resolveBookSlug(q);
  if (slug) {
    const abbrev = SLUG_TO_ABBREV[slug];
    return {
      kind: "book",
      bookName: ABBREV_TO_TITLE[abbrev],
      bookSlug: slug,
      chapterCount: kjvStore.byAbbrev.get(abbrev)?.length ?? 0,
    };
  }

  const sug = suggestBooks(q);
  return { kind: "suggestions", books: sug };
}

/** Lowercased input → canonical book slug, using parser aliases or stored book list. */
export function resolveBookSlug(text) {
  const lower = text.toLowerCase().trim().replace(/\s+/g, " ");
  if (!lower) return null;
  for (const slug of Object.keys(SLUG_TO_ABBREV)) {
    if (slug === lower) return slug;
  }
  // Try by display name match.
  const books = kjvStore.listBooks();
  const exact = books.find((b) => b.name.toLowerCase() === lower);
  if (exact) return exact.slug;
  const starts = books.find((b) => b.name.toLowerCase().startsWith(lower));
  if (starts) return starts.slug;
  // Last resort: parse a fragment with the reference parser by appending "1:1".
  const refs = findReferences(`${lower} 1:1`);
  return refs[0]?.bookSlug ?? null;
}

/** Tokenise a query into useful keywords (lowercase, stopwords removed, deduped). */
function tokenize(query) {
  const raw = query
    .toLowerCase()
    .replace(/[^a-z0-9'\s]/g, " ")
    .split(/\s+/)
    .filter(Boolean);
  const seen = new Set();
  const out = [];
  for (const t of raw) {
    if (t.length < 2) continue;
    if (STOP_WORDS.has(t)) continue;
    if (seen.has(t)) continue;
    seen.add(t);
    out.push(t);
  }
  return out;
}

/**
 * Context-mode keyword search across the whole KJV.
 * Score = (matched tokens / total tokens) * 100, plus small bonuses for
 * exact-phrase containment and adjacency. Offline, no embeddings.
 *
 * Returns: [{ payload, score, matchedTokens, totalTokens }]
 */
export function contextSearch(query, limit = 12) {
  const tokens = tokenize(query);
  if (tokens.length === 0) return [];
  const phrase = query.toLowerCase().replace(/\s+/g, " ").trim();

  const results = [];
  for (const v of kjvStore.iterAllVerses()) {
    const lower = v.text.toLowerCase();
    let matched = 0;
    for (const t of tokens) {
      if (lower.includes(t)) matched++;
    }
    if (matched === 0) continue;

    let score = (matched / tokens.length) * 70;
    if (matched === tokens.length) score += 15;
    if (phrase.length > 4 && lower.includes(phrase)) score += 15;

    results.push({
      bookName: v.bookName,
      bookSlug: v.bookSlug,
      chapter: v.chapter,
      verseStart: v.verse,
      verseEnd: v.verse,
      reference: `${v.bookName} ${v.chapter}:${v.verse}`,
      text: v.text,
      translationId: "kjv",
      translationName: "King James Version",
      _matched: matched,
      _total: tokens.length,
      _score: Math.min(99, Math.round(score)),
    });
  }

  results.sort((a, b) => b._score - a._score);
  return results.slice(0, limit).map((r) => ({
    payload: {
      reference: r.reference,
      bookName: r.bookName,
      bookSlug: r.bookSlug,
      chapter: r.chapter,
      verseStart: r.verseStart,
      verseEnd: r.verseEnd,
      text: r.text,
      translationId: r.translationId,
      translationName: r.translationName,
    },
    score: r._score,
    matchedTokens: r._matched,
    totalTokens: r._total,
  }));
}
