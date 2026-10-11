using System;
using System.Collections.Generic;
using System.Text;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>What the text reader found in ONE camera picture.</summary>
    public class FrameReading
    {
        /// <summary>One line of text, how sure the reader is of it (0 to 1), and where it is in the picture (0 to 1, from the top-left corner).</summary>
        public class Line
        {
            public string text = "";
            public float confidence = 1f;
            public float left, top, right, bottom;
        }

        public List<Line> lines = new List<Line>();
        /// <summary>The digits under a barcode in the picture, or "".</summary>
        public string barcode = "";

        /// <summary>The lines the reader is at least this sure of, one per line of text.</summary>
        public string Text(float minimumConfidence = 0f)
        {
            var sb = new StringBuilder();
            foreach (Line line in lines)
                if (line != null && line.confidence >= minimumConfidence && !string.IsNullOrWhiteSpace(line.text)) sb.Append(line.text.Trim()).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>
    /// MULTI-FRAME TEXT MERGING of hold-to-scan. While the button is held the same pack is read many times, each time
    /// a little differently: a word hidden by glare in one picture is clear in the next, and a long ingredient list
    /// passes the camera piece by piece as the product is turned. This class puts the readings together:
    ///
    ///  - every line of text is kept once, in its best reading (the one the text reader was most sure of);
    ///  - the ingredient names are kept in the order of the label. A name COUNTS when the app knows it (also when it
    ///    was read with a small mistake), or when it was read in at least two pictures. A name read once that the app
    ///    does not know is treated as a misreading until it is seen again;
    ///  - a list that came in pieces is put together where the pieces overlap.
    ///
    /// Pure logic: no camera, no Unity scene.
    /// </summary>
    public class ScanTextMerger
    {
        /// <summary>One distinct line of text.</summary>
        public class Line
        {
            public string text = "", key = "";
            public float confidence;
            public int seen;
        }

        /// <summary>One name of the ingredient list.</summary>
        public class Token
        {
            /// <summary>The name to show (for a known ingredient: the name the app knows it under).</summary>
            public string name = "";
            public string key = "";
            /// <summary>The app knows this ingredient.</summary>
            public bool known;
            /// <summary>It stood behind "may contain" or "+/-".</summary>
            public bool mayContain;
            /// <summary>In how many pictures it was read.</summary>
            public int seen;
            /// <summary>True when it is part of the list: known, or read at least twice.</summary>
            public bool Counts => known || seen >= SeenToCount;
        }

        /// <summary>An unknown name must be read in this many pictures before it counts.</summary>
        public static int SeenToCount = 2;
        /// <summary>The list counts as read to its end after this many pictures of it in a row brought nothing new.</summary>
        public static int QuietFramesForDone = 2;
        /// <summary>How alike two readings of a name the app does not know must be to count as the same name (1 = letter for letter).</summary>
        public static float SameReadingThreshold = 0.75f;

        readonly List<Line> lines = new List<Line>();
        readonly Dictionary<string, Line> lineByKey = new Dictionary<string, Line>(StringComparer.Ordinal);
        readonly List<Token> tokens = new List<Token>();
        int quietFrames;

        public IReadOnlyList<Line> Lines => lines;
        public IReadOnlyList<Token> Tokens => tokens;
        /// <summary>A picture showed the word "Ingredients" (the list's start was seen).</summary>
        public bool AnchorSeen { get; private set; }
        /// <summary>How many pictures of the ingredient list were put together.</summary>
        public int IngredientFrames { get; private set; }

        public void Clear()
        {
            lines.Clear(); lineByKey.Clear(); tokens.Clear();
            quietFrames = 0; AnchorSeen = false; IngredientFrames = 0;
        }

        // ---------------------------------------------------------------------------------------
        //  Lines
        // ---------------------------------------------------------------------------------------
        /// <summary>Adds the lines of one picture. Returns how many of them were not known before.</summary>
        public int AddLines(IEnumerable<FrameReading.Line> read, float minimumConfidence = 0f)
        {
            int fresh = 0;
            if (read == null) return 0;
            foreach (FrameReading.Line one in read)
            {
                if (one == null || one.confidence < minimumConfidence) continue;
                string key = TextKey.Squash(TextKey.Normalize(one.text));
                if (key.Length < 2) continue;
                if (!lineByKey.TryGetValue(key, out Line have)) have = Alike(key);
                if (have == null)
                {
                    have = new Line { text = one.text.Trim(), key = key, confidence = one.confidence, seen = 1 };
                    lines.Add(have); lineByKey[key] = have;
                    fresh++;
                    continue;
                }
                have.seen++;
                if (one.confidence > have.confidence) { have.text = one.text.Trim(); have.confidence = one.confidence; }     // the better reading wins
                lineByKey[key] = have;
            }
            return fresh;
        }

        /// <summary>How many of these lines are known already (the same, or the same with a small reading mistake).</summary>
        public int KnownAmong(IEnumerable<FrameReading.Line> read)
        {
            int known = 0;
            if (read == null) return 0;
            foreach (FrameReading.Line one in read)
            {
                if (one == null) continue;
                string key = TextKey.Squash(TextKey.Normalize(one.text));
                if (key.Length >= 2 && (lineByKey.ContainsKey(key) || Alike(key) != null)) known++;
            }
            return known;
        }

        // a line that was read before with one or two letters different (only tried for lines of some length)
        Line Alike(string key)
        {
            if (key.Length < 8) return null;
            int allowed = Math.Max(1, key.Length / 10);
            foreach (Line have in lines)
            {
                if (Math.Abs(have.key.Length - key.Length) > allowed) continue;
                if (Within(have.key, key, allowed)) return have;
            }
            return null;
        }

        /// <summary>True when at most `allowed` letters must be changed, added or taken away to turn one text into the other.</summary>
        static bool Within(string a, string b, int allowed)
        {
            if (a == b) return true;
            if (Math.Abs(a.Length - b.Length) > allowed) return false;
            var previous = new int[b.Length + 1]; var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) previous[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i; int best = current[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                    if (current[j] < best) best = current[j];
                }
                if (best > allowed) return false;
                int[] swap = previous; previous = current; current = swap;
            }
            return previous[b.Length] <= allowed;
        }

        /// <summary>Every line that was read, best reading of each, in the order they were first seen.</summary>
        public string AllText(float minimumConfidence = 0f)
        {
            var sb = new StringBuilder();
            foreach (Line line in lines) if (line.confidence >= minimumConfidence) sb.Append(line.text).Append('\n');
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------------
        //  The ingredient list
        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// Adds what the ingredient reader made of one picture of the list. Returns how many names started to count
        /// because of this picture (0 = it brought nothing new).
        /// </summary>
        public int AddIngredients(IngredientTextParser.ParsedList parsed)
        {
            if (parsed == null || parsed.inOrder.Count == 0) return 0;
            IngredientFrames++;
            if (parsed.anchorFound) AnchorSeen = true;
            var knownNames = new HashSet<string>(parsed.ingredients, StringComparer.OrdinalIgnoreCase);
            var maybe = new HashSet<string>(parsed.mayContain, StringComparer.OrdinalIgnoreCase);

            // which of the picture's names are in the list already?
            var incoming = new List<Token>();
            foreach (string name in parsed.inOrder)
                incoming.Add(new Token { name = name, key = TextKey.Normalize(name), known = knownNames.Contains(name), mayContain = maybe.Contains(name), seen = 1 });
            var at = new int[incoming.Count];                 // the place of each incoming name in the list, or -1
            int countedBefore = CountedCount();
            int searchFrom = 0;
            for (int i = 0; i < incoming.Count; i++)
            {
                at[i] = Find(incoming[i], searchFrom);
                if (at[i] < 0) at[i] = Find(incoming[i], 0);
                if (at[i] >= 0) searchFrom = at[i] + 1;
            }

            // names that are there already: seen once more (and a known reading replaces an unknown one)
            var counted = new HashSet<int>();                 // one picture counts once for a name, however often the name is in it
            for (int i = 0; i < incoming.Count; i++)
            {
                if (at[i] < 0) continue;
                Token have = tokens[at[i]];
                if (counted.Add(at[i])) have.seen++;
                if (incoming[i].known && !have.known) { have.known = true; have.name = incoming[i].name; have.key = incoming[i].key; }
                if (incoming[i].mayContain) have.mayContain = true;
            }

            // new names go in next to their neighbours: after the nearest name before them that is in the list already,
            // otherwise before the nearest one after them, otherwise at the end (or at the start when this picture
            // showed the word "Ingredients")
            int inserted = 0;
            for (int i = 0; i < incoming.Count; i++)
            {
                if (at[i] >= 0) continue;
                int place = -1;
                for (int before = i - 1; before >= 0 && place < 0; before--) if (at[before] >= 0) place = at[before] + 1;
                if (place < 0)
                    for (int after = i + 1; after < incoming.Count && place < 0; after++) if (at[after] >= 0) place = at[after];
                if (place < 0) place = parsed.anchorFound && inserted == 0 && tokens.Count > 0 && !AnchorWasFirst ? 0 : tokens.Count;
                place = Math.Max(0, Math.Min(tokens.Count, place));
                tokens.Insert(place, incoming[i]);
                for (int k = 0; k < at.Length; k++) if (at[k] >= place) at[k]++;      // what stood behind it moved one place on
                at[i] = place;
                inserted++;
            }
            if (parsed.anchorFound) AnchorWasFirst = true;

            int started = CountedCount() - countedBefore;
            quietFrames = started == 0 && inserted == 0 ? quietFrames + 1 : 0;
            return started;
        }
        bool AnchorWasFirst;

        int Find(Token wanted, int from)
        {
            for (int i = from; i < tokens.Count; i++) if (Same(tokens[i], wanted)) return i;
            return -1;
        }

        /// <summary>The same name: letter for letter, or read with a small mistake (the same safeguards as for known ingredients).</summary>
        static bool Same(Token a, Token b)
        {
            if (a.key == b.key) return true;
            if (a.known && b.known) return false;                       // two different known ingredients
            if (TextKey.Squash(a.key) == TextKey.Squash(b.key)) return true;
            // Two readings of the same place on the same label may differ a little more than a reading and a known name
            // ("Hectorite" read as "Hectonte" is two letters off). The safeguards stay: the same number of words, the same
            // first letters, the same digits, three letters at most.
            return IngredientKnowledge.EditsBetween(a.key, b.key, Math.Min(IngredientKnowledge.FuzzyThreshold, SameReadingThreshold)) > 0;
        }

        int CountedCount() { int n = 0; foreach (Token token in tokens) if (token.Counts) n++; return n; }

        /// <summary>The names that count, in the order of the label (the colourants behind "may contain" come last).</summary>
        public List<string> CountedNames(bool withMayContain = true)
        {
            var names = new List<string>();
            foreach (Token token in tokens) if (token.Counts && !token.mayContain) names.Add(token.name);
            if (withMayContain) foreach (Token token in tokens) if (token.Counts && token.mayContain) names.Add(token.name);
            return names;
        }

        /// <summary>How many names count, and how many of them the app knows.</summary>
        public int Counted => CountedCount();
        public int KnownCount { get { int n = 0; foreach (Token token in tokens) if (token.known) n++; return n; } }
        /// <summary>Names read once that the app does not know: they wait for a second reading.</summary>
        public int Waiting { get { int n = 0; foreach (Token token in tokens) if (!token.Counts) n++; return n; } }

        /// <summary>
        /// How much of the ingredient list has been read, 0 to 1. It grows with every name that counts and reaches 1 when
        /// enough names count and the last pictures of the list brought nothing new (the list was read to its end).
        /// </summary>
        public float IngredientProgress(int minimumNames = 2)
        {
            int counted = CountedCount();
            if (counted == 0) return 0f;
            if (counted >= Math.Max(1, minimumNames) && quietFrames >= QuietFramesForDone) return 1f;
            // not at the end yet: the names that wait and "a few more to come" keep it below full
            return Math.Min(0.9f, counted / (float)(counted + Waiting + 3));
        }

        /// <summary>
        /// The list as one text for the analysis: "Ingredients: A, B, C" and, when there are any, " May contain: X, Y".
        /// "" when no name counts yet.
        /// </summary>
        public string IngredientText()
        {
            var main = new List<string>(); var maybe = new List<string>();
            foreach (Token token in tokens)
            {
                if (!token.Counts) continue;
                (token.mayContain ? maybe : main).Add(token.name);
            }
            if (main.Count + maybe.Count == 0) return "";
            string text = "Ingredients: " + string.Join(", ", main);
            if (maybe.Count > 0) text += " May contain: " + string.Join(", ", maybe);
            return text;
        }
    }
}
