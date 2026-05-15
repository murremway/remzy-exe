export const HYMNS = [
  {
    id: "amazing-grace",
    title: "Amazing Grace",
    author: "John Newton",
    year: "1779",
    license: "Public domain",
    stanzas: [
      "Amazing grace! how sweet the sound,\nThat saved a wretch like me!\nI once was lost, but now am found,\nWas blind, but now I see.",
      "'Twas grace that taught my heart to fear,\nAnd grace my fears relieved;\nHow precious did that grace appear\nThe hour I first believed!",
    ],
  },
  {
    id: "holy-holy-holy",
    title: "Holy, Holy, Holy",
    author: "Reginald Heber",
    year: "1826",
    license: "Public domain",
    stanzas: [
      "Holy, holy, holy! Lord God Almighty!\nEarly in the morning our song shall rise to thee;\nHoly, holy, holy! merciful and mighty!\nGod in three persons, blessed Trinity!",
      "Holy, holy, holy! all the saints adore thee,\nCasting down their golden crowns around the glassy sea;\nCherubim and seraphim falling down before thee,\nWhich wert, and art, and evermore shalt be.",
    ],
  },
  {
    id: "blessed-assurance",
    title: "Blessed Assurance",
    author: "Fanny J. Crosby",
    year: "1873",
    license: "Public domain",
    stanzas: [
      "Blessed assurance, Jesus is mine!\nO what a foretaste of glory divine!\nHeir of salvation, purchase of God,\nBorn of his Spirit, washed in his blood.",
      "This is my story, this is my song,\nPraising my Savior all the day long;\nThis is my story, this is my song,\nPraising my Savior all the day long.",
    ],
  },
  {
    id: "it-is-well",
    title: "It Is Well with My Soul",
    author: "Horatio G. Spafford",
    year: "1873",
    license: "Public domain",
    stanzas: [
      "When peace, like a river, attendeth my way,\nWhen sorrows like sea billows roll;\nWhatever my lot, thou hast taught me to say,\nIt is well, it is well with my soul.",
      "It is well with my soul,\nIt is well, it is well with my soul.",
    ],
  },
  {
    id: "rock-of-ages",
    title: "Rock of Ages",
    author: "Augustus M. Toplady",
    year: "1776",
    license: "Public domain",
    stanzas: [
      "Rock of Ages, cleft for me,\nLet me hide myself in thee;\nLet the water and the blood,\nFrom thy wounded side which flowed,\nBe of sin the double cure;\nSave from wrath and make me pure.",
      "Not the labors of my hands\nCan fulfill thy law's demands;\nCould my zeal no respite know,\nCould my tears forever flow,\nAll for sin could not atone;\nThou must save, and thou alone.",
    ],
  },
  {
    id: "come-thou-fount",
    title: "Come Thou Fount of Every Blessing",
    author: "Robert Robinson",
    year: "1758",
    license: "Public domain",
    stanzas: [
      "Come, thou fount of every blessing,\nTune my heart to sing thy grace;\nStreams of mercy, never ceasing,\nCall for songs of loudest praise.",
      "Here I raise mine Ebenezer;\nHither by thy help I'm come;\nAnd I hope, by thy good pleasure,\nSafely to arrive at home.",
    ],
  },
];

export function searchHymns(query) {
  const q = (query ?? "").trim().toLowerCase();
  if (!q) return HYMNS;
  return HYMNS.filter((h) =>
    [h.title, h.author, h.stanzas.join(" ")].join(" ").toLowerCase().includes(q)
  );
}
