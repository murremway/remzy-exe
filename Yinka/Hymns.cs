// Yinka — public-domain hymn library bundled with the Windows app.
// These classic texts are bundled offline; the same hymns ship in the Mac
// build (Yinka.Mac/js/hymns.js). All hymns here are in the public domain.

namespace Yinka;

public static class Hymns
{
    public static IReadOnlyList<Hymn> All { get; } = new[]
    {
        new Hymn(
            Id: "amazing-grace",
            Title: "Amazing Grace",
            Author: "John Newton",
            Year: "1779",
            License: "Public domain",
            Stanzas: new[]
            {
                "Amazing grace! how sweet the sound,\nThat saved a wretch like me!\nI once was lost, but now am found,\nWas blind, but now I see.",
                "'Twas grace that taught my heart to fear,\nAnd grace my fears relieved;\nHow precious did that grace appear\nThe hour I first believed!",
                "Through many dangers, toils and snares,\nI have already come;\n'Tis grace hath brought me safe thus far,\nAnd grace will lead me home.",
                "The Lord has promised good to me,\nHis word my hope secures;\nHe will my shield and portion be,\nAs long as life endures.",
                "When we've been there ten thousand years,\nBright shining as the sun,\nWe've no less days to sing God's praise\nThan when we'd first begun.",
            }),
        new Hymn(
            Id: "holy-holy-holy",
            Title: "Holy, Holy, Holy",
            Author: "Reginald Heber",
            Year: "1826",
            License: "Public domain",
            Stanzas: new[]
            {
                "Holy, holy, holy! Lord God Almighty!\nEarly in the morning our song shall rise to thee;\nHoly, holy, holy! merciful and mighty!\nGod in three persons, blessed Trinity!",
                "Holy, holy, holy! all the saints adore thee,\nCasting down their golden crowns around the glassy sea;\nCherubim and seraphim falling down before thee,\nWhich wert, and art, and evermore shalt be.",
                "Holy, holy, holy! though the darkness hide thee,\nThough the eye of sinful man thy glory may not see;\nOnly thou art holy; there is none beside thee,\nPerfect in power, in love, and purity.",
                "Holy, holy, holy! Lord God Almighty!\nAll thy works shall praise thy name, in earth, and sky, and sea;\nHoly, holy, holy! merciful and mighty!\nGod in three persons, blessed Trinity!",
            }),
        new Hymn(
            Id: "blessed-assurance",
            Title: "Blessed Assurance",
            Author: "Fanny J. Crosby",
            Year: "1873",
            License: "Public domain",
            Stanzas: new[]
            {
                "Blessed assurance, Jesus is mine!\nO what a foretaste of glory divine!\nHeir of salvation, purchase of God,\nBorn of his Spirit, washed in his blood.",
                "Refrain:\nThis is my story, this is my song,\nPraising my Savior all the day long;\nThis is my story, this is my song,\nPraising my Savior all the day long.",
                "Perfect submission, perfect delight,\nVisions of rapture now burst on my sight;\nAngels descending bring from above\nEchoes of mercy, whispers of love.",
                "Perfect submission, all is at rest,\nI in my Savior am happy and blest,\nWatching and waiting, looking above,\nFilled with his goodness, lost in his love.",
            }),
        new Hymn(
            Id: "it-is-well",
            Title: "It Is Well with My Soul",
            Author: "Horatio G. Spafford",
            Year: "1873",
            License: "Public domain",
            Stanzas: new[]
            {
                "When peace, like a river, attendeth my way,\nWhen sorrows like sea billows roll;\nWhatever my lot, thou hast taught me to say,\nIt is well, it is well with my soul.",
                "Refrain:\nIt is well with my soul,\nIt is well, it is well with my soul.",
                "Though Satan should buffet, though trials should come,\nLet this blest assurance control,\nThat Christ has regarded my helpless estate,\nAnd hath shed his own blood for my soul.",
                "My sin—oh, the bliss of this glorious thought—\nMy sin, not in part but the whole,\nIs nailed to the cross, and I bear it no more;\nPraise the Lord, praise the Lord, O my soul!",
                "And, Lord, haste the day when my faith shall be sight,\nThe clouds be rolled back as a scroll;\nThe trump shall resound, and the Lord shall descend,\nEven so, it is well with my soul.",
            }),
        new Hymn(
            Id: "rock-of-ages",
            Title: "Rock of Ages",
            Author: "Augustus M. Toplady",
            Year: "1776",
            License: "Public domain",
            Stanzas: new[]
            {
                "Rock of Ages, cleft for me,\nLet me hide myself in thee;\nLet the water and the blood,\nFrom thy wounded side which flowed,\nBe of sin the double cure;\nSave from wrath and make me pure.",
                "Not the labors of my hands\nCan fulfill thy law's demands;\nCould my zeal no respite know,\nCould my tears forever flow,\nAll for sin could not atone;\nThou must save, and thou alone.",
                "Nothing in my hand I bring,\nSimply to thy cross I cling;\nNaked, come to thee for dress;\nHelpless, look to thee for grace;\nFoul, I to the fountain fly;\nWash me, Savior, or I die.",
                "While I draw this fleeting breath,\nWhen mine eyes shall close in death,\nWhen I soar to worlds unknown,\nSee thee on thy judgment throne,\nRock of Ages, cleft for me,\nLet me hide myself in thee.",
            }),
        new Hymn(
            Id: "come-thou-fount",
            Title: "Come Thou Fount of Every Blessing",
            Author: "Robert Robinson",
            Year: "1758",
            License: "Public domain",
            Stanzas: new[]
            {
                "Come, thou fount of every blessing,\nTune my heart to sing thy grace;\nStreams of mercy, never ceasing,\nCall for songs of loudest praise.\nTeach me some melodious sonnet,\nSung by flaming tongues above;\nPraise the mount! I'm fixed upon it,\nMount of thy redeeming love.",
                "Here I raise mine Ebenezer;\nHither by thy help I'm come;\nAnd I hope, by thy good pleasure,\nSafely to arrive at home.\nJesus sought me when a stranger,\nWandering from the fold of God;\nHe, to rescue me from danger,\nInterposed his precious blood.",
                "O to grace how great a debtor\nDaily I'm constrained to be!\nLet thy goodness, like a fetter,\nBind my wandering heart to thee.\nProne to wander, Lord, I feel it,\nProne to leave the God I love;\nHere's my heart, O take and seal it,\nSeal it for thy courts above.",
            }),
        new Hymn(
            Id: "great-is-thy-faithfulness-public-domain-source",
            Title: "Great Is Thy Faithfulness (1923)",
            Author: "Thomas O. Chisholm",
            Year: "1923",
            License: "Public domain in the United States (pre-1929)",
            Stanzas: new[]
            {
                "Great is thy faithfulness, O God my Father,\nThere is no shadow of turning with thee;\nThou changest not, thy compassions, they fail not\nAs thou hast been thou forever wilt be.",
                "Refrain:\nGreat is thy faithfulness! Great is thy faithfulness!\nMorning by morning new mercies I see;\nAll I have needed thy hand hath provided—\nGreat is thy faithfulness, Lord, unto me!",
                "Summer and winter, and springtime and harvest,\nSun, moon and stars in their courses above,\nJoin with all nature in manifold witness\nTo thy great faithfulness, mercy and love.",
                "Pardon for sin and a peace that endureth,\nThine own dear presence to cheer and to guide;\nStrength for today and bright hope for tomorrow,\nBlessings all mine, with ten thousand beside!",
            }),
        new Hymn(
            Id: "all-hail-the-power",
            Title: "All Hail the Power of Jesus' Name",
            Author: "Edward Perronet",
            Year: "1779",
            License: "Public domain",
            Stanzas: new[]
            {
                "All hail the power of Jesus' name!\nLet angels prostrate fall;\nBring forth the royal diadem,\nAnd crown him Lord of all!\nBring forth the royal diadem,\nAnd crown him Lord of all!",
                "Ye chosen seed of Israel's race,\nYe ransomed from the fall,\nHail him who saves you by his grace,\nAnd crown him Lord of all!\nHail him who saves you by his grace,\nAnd crown him Lord of all!",
                "Let every kindred, every tribe,\nOn this terrestrial ball,\nTo him all majesty ascribe,\nAnd crown him Lord of all!\nTo him all majesty ascribe,\nAnd crown him Lord of all!",
            }),
        new Hymn(
            Id: "jesus-loves-me",
            Title: "Jesus Loves Me",
            Author: "Anna B. Warner",
            Year: "1860",
            License: "Public domain",
            Stanzas: new[]
            {
                "Jesus loves me—this I know,\nFor the Bible tells me so;\nLittle ones to him belong,\nThey are weak, but he is strong.",
                "Refrain:\nYes, Jesus loves me!\nYes, Jesus loves me!\nYes, Jesus loves me!\nThe Bible tells me so.",
                "Jesus loves me—he who died\nHeaven's gate to open wide;\nHe will wash away my sin,\nLet his little child come in.",
            }),
        new Hymn(
            Id: "what-a-friend",
            Title: "What a Friend We Have in Jesus",
            Author: "Joseph M. Scriven",
            Year: "1855",
            License: "Public domain",
            Stanzas: new[]
            {
                "What a friend we have in Jesus,\nAll our sins and griefs to bear!\nWhat a privilege to carry\nEverything to God in prayer!\nO what peace we often forfeit,\nO what needless pain we bear,\nAll because we do not carry\nEverything to God in prayer.",
                "Have we trials and temptations?\nIs there trouble anywhere?\nWe should never be discouraged—\nTake it to the Lord in prayer.\nCan we find a friend so faithful,\nWho will all our sorrows share?\nJesus knows our every weakness;\nTake it to the Lord in prayer.",
                "Are we weak and heavy laden,\nCumbered with a load of care?\nPrecious Savior, still our refuge—\nTake it to the Lord in prayer.\nDo thy friends despise, forsake thee?\nTake it to the Lord in prayer!\nIn his arms he'll take and shield thee,\nThou wilt find a solace there.",
            }),
        new Hymn(
            Id: "doxology",
            Title: "Doxology (Praise God from Whom All Blessings Flow)",
            Author: "Thomas Ken",
            Year: "1674",
            License: "Public domain",
            Stanzas: new[]
            {
                "Praise God, from whom all blessings flow;\nPraise him, all creatures here below;\nPraise him above, ye heavenly host;\nPraise Father, Son, and Holy Ghost.\nAmen.",
            }),
    };

    public static IReadOnlyList<Hymn> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return All;

        var q = query.Trim();
        return All
            .Where(h =>
                h.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || h.Author.Contains(q, StringComparison.OrdinalIgnoreCase)
                || h.Stanzas.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Build a Live/Preview payload for one stanza or a stanza range (1-indexed).</summary>
    public static VersePayload BuildPayload(Hymn hymn, int stanzaStart, int stanzaEnd)
    {
        var lo = Math.Max(1, Math.Min(stanzaStart, stanzaEnd));
        var hi = Math.Min(hymn.Stanzas.Count, Math.Max(stanzaStart, stanzaEnd));
        var slice = hymn.Stanzas
            .Skip(lo - 1)
            .Take(hi - lo + 1)
            .ToList();
        var reference = lo == hi
            ? $"{hymn.Title} · Stanza {lo}"
            : $"{hymn.Title} · Stanzas {lo}-{hi}";
        return new VersePayload(
            Reference: reference,
            Text: string.Join("\n\n", slice),
            TranslationId: "hymn",
            TranslationName: $"{hymn.Author} · {hymn.Year} · {hymn.License}");
    }

    /// <summary>Build a Live/Preview payload covering the entire hymn.</summary>
    public static VersePayload BuildWholeHymnPayload(Hymn hymn) =>
        BuildPayload(hymn, 1, hymn.Stanzas.Count);
}

public sealed record Hymn(
    string Id,
    string Title,
    string Author,
    string Year,
    string License,
    IReadOnlyList<string> Stanzas)
{
    public override string ToString() => Title;
}
