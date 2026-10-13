using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// Reads the text of an ingredient list (from the camera, or typed by the user) and sorts every piece of it into
    /// one of three piles:
    ///
    ///   ingredients   the piece is an ingredient the app KNOWS (a name or synonym from the website's list or the
    ///                 built-in list, or a close misreading of one: see IngredientKnowledge.FuzzyThreshold).
    ///                 Only these are checked against the user's profile and count in the result.
    ///   unrecognized  the piece looks like an ingredient name but matches nothing the app knows. It is NOT counted as
    ///                 an ingredient and not part of the verdict; the result says how many there were. Online, the AI
    ///                 lookup may be asked about it (IngredientResearcher).
    ///   rejected      the piece is not an ingredient at all: label wording from the stoplist (IngredientStoplist: net
    ///                 weight, "made in", batch codes, web addresses, advertising), a number or a code, a sentence, or
    ///                 a brand or product name. It is thrown away.
    ///
    /// How the text is read: only the text AFTER the word "Ingredients" (when the label has it) and BEFORE the label goes
    /// on to something else (directions, warnings, who made it); cut at commas; each piece cleaned; "Water (Aqua)" and
    /// "Aqua/Water/Eau" give one ingredient; a name that wraps over two lines is put together again; two known names
    /// whose comma the camera missed are separated again. The colourants behind "may contain" or "+/-" are read as well
    /// and remembered as "may contain, depending on the shade".
    /// </summary>
    public static class IngredientTextParser
    {
        /// <summary>What the reader made of a label.</summary>
        public class ParsedList
        {
            /// <summary>Known ingredients, by the name the app knows them under, in the order of the label.</summary>
            public List<string> ingredients = new List<string>();
            /// <summary>Pieces that look like ingredient names but match nothing the app knows.</summary>
            public List<string> unrecognized = new List<string>();
            /// <summary>Pieces that are not ingredients, each with the reason: "Net Wt 30g  (stoplist: net wt)".</summary>
            public List<string> rejected = new List<string>();
            /// <summary>The names (of either pile) that stood behind "may contain" or "+/-": they depend on the shade.</summary>
            public List<string> mayContain = new List<string>();
            /// <summary>ingredients and unrecognized together, in the order of the label.</summary>
            public List<string> inOrder = new List<string>();
            /// <summary>The label had the word "Ingredients" (or INCI, Composition ...).</summary>
            public bool anchorFound;
            public int Count => inOrder.Count;
        }

        // names the app knows as brands or products (the scanner fills this in): a brand printed on the pack is never an ingredient
        static readonly HashSet<string> notIngredients = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The scanner tells the reader which names are brands and products, so they are never taken for ingredients.</summary>
        public static void SetNotIngredients(IEnumerable<string> names)
        {
            notIngredients.Clear();
            if (names == null) return;
            foreach (string name in names) { string key = TextKey.Normalize(name); if (key.Length >= 2) notIngredients.Add(key); }
        }

        /// <summary>
        /// True when the text has the SHAPE of an ingredient list: the word "Ingredients" (also INCI, Composition), or several
        /// names in a row with commas between them. The front of a pack (brand, product name, claims) has neither.
        /// </summary>
        public static bool HasListShape(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (IngredientStoplist.ListStart.IsMatch(text)) return true;
            int commas = 0; foreach (char c in text) if (c == ',' || c == ';') commas++;
            return commas >= 4;
        }

        /// <summary>The names the reader kept (known ingredients and unrecognised names), in the order of the label. See <see cref="Read"/> for the piles.</summary>
        public static List<string> Parse(string text) => Read(text).inOrder;

        /// <summary>How many of these names are real, recognised ingredients (used to decide whether a camera read worked).</summary>
        public static int CountRecognised(List<string> names)
        {
            int n = 0;
            foreach (string name in names) if (IngredientKnowledge.IsKnown(name)) n++;
            return n;
        }

        /// <summary>Reads a label text into the three piles.</summary>
        public static ParsedList Read(string text)
        {
            var result = new ParsedList();
            if (string.IsNullOrWhiteSpace(text)) return result;
            string t = text.Replace("\r\n", "\n").Replace('\r', '\n');

            // 1. start after the word "Ingredients" when the label has it (a second header right behind it is skipped too:
            //    "INGREDIENTS / INGREDIENTES:")
            Match start = IngredientStoplist.ListStart.Match(t);
            if (start.Success)
            {
                result.anchorFound = true;
                t = t.Substring(start.Index + start.Length);
                for (int i = 0; i < 3; i++)
                {
                    Match again = IngredientStoplist.ListStart.Match(t);
                    if (!again.Success || again.Index > 3) break;
                    t = t.Substring(again.Index + again.Length);
                }
            }

            // 2. the list ends where the label goes on to something else, or where the list starts again in another language
            int end = t.Length;
            Match stop = IngredientStoplist.ListEnd.Match(t);
            if (stop.Success && stop.Index > 12) end = stop.Index;
            Match next = IngredientStoplist.ListStart.Match(t);
            if (next.Success && next.Index > 12 && next.Index < end) end = next.Index;
            t = t.Substring(0, end);

            // 3. the colourants that depend on the shade stand behind "may contain" or "+/-"
            Match may = IngredientStoplist.MayContain.Match(t);
            if (may.Success)
            {
                ReadPart(t.Substring(0, may.Index), result, false, result.anchorFound);
                ReadPart(t.Substring(may.Index + may.Length), result, true, true);
            }
            else ReadPart(t, result, false, result.anchorFound);
            return result;
        }

        // ---------------------------------------------------------------------------------------
        static void ReadPart(string part, ParsedList result, bool mayContain, bool startIsClean)
        {
            if (string.IsNullOrWhiteSpace(part)) return;
            // a name cut in two by a line break keeps its hyphen but not the break: "DIMETHI-\nCONE", "BIS-\nDIGLYCERYL"
            part = Regex.Replace(part, @"-[ \t]*\n[ \t]*", "-");

            // ONE LINE WITHOUT A COMMA is not what a pack prints: somebody typed it ("retinol and niacinamide"). The words
            // "and", "&" and "plus" stand between its names.
            if (part.IndexOf(',') < 0 && part.IndexOf(';') < 0 && part.Trim().IndexOf('\n') < 0)
                part = Regex.Replace(part, @"\s+(and|&|plus)\s+", ", ", RegexOptions.IgnoreCase);

            // a list separated by commas wraps over several lines; a list typed one name per line has no commas
            bool commas = part.IndexOf(',') >= 0 || part.IndexOf(';') >= 0;
            char[] cuts = commas ? new[] { ',', ';', '\u2022', '\u00b7', '*' } : new[] { '\n', '\u2022', '\u00b7', '*' };
            string[] pieces = part.Split(cuts, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < pieces.Length; i++)
            {
                string piece = pieces[i].Trim(' ', '\t', '\n');
                if (piece.Length == 0) continue;
                // text in front of the list (when the label had no "Ingredients" word) and behind its last name
                bool first = i == 0 && !startIsClean && commas, last = i == pieces.Length - 1 && commas;

                if (commas && piece.IndexOf('\n') >= 0)
                {
                    // one name that wraps over two lines, or two names whose comma was not read
                    string whole = Regex.Replace(piece, @"\s*\n\s*", " ");
                    if (Recognise(whole, out string known)) { Keep(result, known, true, mayContain); continue; }
                    string[] lines = piece.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var names = new List<string>();
                    foreach (string line in lines) { if (Recognise(line, out string name)) names.Add(name); else { names = null; break; } }
                    if (names != null && names.Count > 1) { foreach (string name in names) Keep(result, name, true, mayContain); continue; }
                    piece = whole;
                }
                Sort(piece, result, mayContain, first, last);
            }
        }

        /// <summary>Puts one piece of the label on its pile.</summary>
        static void Sort(string rawPiece, ParsedList result, bool mayContain, bool first, bool last)
        {
            string piece = Clean(rawPiece);
            if (piece.Length == 0) return;
            // 0. "fragrance free", "no parabens", "0% alcohol": it names an ingredient to say it is NOT in the product. It is
            //    never that ingredient, and nothing is rescued out of it either.
            if (IngredientStoplist.IsFreeOfClaim(rawPiece)) { result.rejected.Add(piece + "  (a claim that something is not in it)"); return; }
            List<string> forms = FormsOf(piece);
            string main = forms.Count > 0 ? forms[0] : piece;

            // 1. exactly a name the app knows: always an ingredient, whatever else the piece looks like
            if (ExactName(forms, out string exactName)) { Keep(result, exactName, true, mayContain); return; }

            // 2. not an ingredient: a brand or product name, label wording, or the wrong shape
            string reason = null;
            if (notIngredients.Contains(TextKey.Normalize(main)) || notIngredients.Contains(TextKey.Normalize(piece))) reason = "a brand or product name";
            else
            {
                string stop = IngredientStoplist.Hit(piece);
                if (stop != null) reason = "stoplist: " + stop;
                else if (IngredientStoplist.WrongShape(main)) reason = "not the shape of an ingredient name";
            }
            if (reason != null)
            {
                // the first and the last piece often carry other label text glued to a real ingredient: "POWDER 8.5 g Talc", "Tocopherol 12M"
                if ((first || last) && Rescue(main, first, out string rescued)) { Keep(result, rescued, true, mayContain); return; }
                result.rejected.Add(piece + "  (" + reason + ")");
                return;
            }

            // 3. two or more known names in a row, whose commas the camera missed or the user did not type:
            //    "Glycerin Niacinamide", "talc mica dimethicone fragrance". (Before step 4: taken as ONE name, such a row
            //    would pass as a member of the last name's family, "... fragrance".)
            List<string> parts = Segment(main);
            if (parts != null) { foreach (string name in parts) Keep(result, name, true, mayContain); return; }

            // 4. a close misreading of a known name, or an ingredient whose family is known
            if (CloseName(forms, out string closeName)) { Keep(result, closeName, true, mayContain); return; }

            // 5. it looks like an ingredient name, but the app does not know it
            if ((first || last) && Rescue(main, first, out string edge)) { Keep(result, edge, true, mayContain); return; }
            Keep(result, Shown(main), false, mayContain);
        }

        static void Keep(ParsedList result, string name, bool recognised, bool mayContain)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            foreach (string have in result.inOrder) if (string.Equals(have, name, StringComparison.OrdinalIgnoreCase)) return;   // "Aqua" and "Water" are one ingredient
            result.inOrder.Add(name);
            (recognised ? result.ingredients : result.unrecognized).Add(name);
            if (mayContain) result.mayContain.Add(name);
        }

        /// <summary>True when the text is (exactly or closely) an ingredient the app knows, or one of a known family. `name` = the name to show.</summary>
        static bool Recognise(string rawPiece, out string name)
        {
            name = null;
            string piece = Clean(rawPiece);
            if (piece.Length < 3 || IngredientStoplist.IsFreeOfClaim(rawPiece)) return false;
            List<string> forms = FormsOf(piece);
            if (ExactName(forms, out name)) return true;
            if (IngredientStoplist.Hit(piece) != null || IngredientStoplist.WrongShape(forms.Count > 0 ? forms[0] : piece)) return false;
            return CloseName(forms, out name);
        }

        /// <summary>
        /// The ingredient that ONE entry of a list names, however it is written: "Water", "Water (Aqua)", "Aqua/Water/Eau",
        /// "Iron Oxides (CI 77491)". An exact name wins over a close spelling or a family. Not found: Found is false.
        /// (The analysis and the product matcher use this for the lists stored with the inventory's products.)
        /// </summary>
        public static IngredientKnowledge.Match MatchEntry(string entry)
        {
            string piece = Clean(entry);
            if (piece.Length < 3) return new IngredientKnowledge.Match();
            List<string> forms = FormsOf(piece);
            foreach (string form in forms)
            {
                if (form.Length < 3) continue;
                IngredientKnowledge.Match match = IngredientKnowledge.Find(form);
                if (match.kind == IngredientKnowledge.MatchKind.Exact) return match;
            }
            foreach (string form in forms)
            {
                if (form.Length < 3) continue;
                IngredientKnowledge.Match match = IngredientKnowledge.Find(form);
                if (match.Found) return match;
            }
            return new IngredientKnowledge.Match();
        }

        /// <summary>True when one of the forms is, letter for letter, a name or synonym the app knows.</summary>
        static bool ExactName(List<string> forms, out string name)
        {
            name = null;
            foreach (string form in forms)
            {
                if (form.Length < 3) continue;
                IngredientKnowledge.Match match = IngredientKnowledge.Find(form);
                if (match.kind == IngredientKnowledge.MatchKind.Exact) { name = match.data.name; return true; }
            }
            return false;
        }

        /// <summary>True when one of the forms is a close misreading of a known name, or belongs to a known family.</summary>
        static bool CloseName(List<string> forms, out string name)
        {
            name = null;
            foreach (string form in forms)
            {
                if (form.Length < 3) continue;
                IngredientKnowledge.Match match = IngredientKnowledge.Find(form);
                if (!match.Found) continue;
                name = match.kind == IngredientKnowledge.MatchKind.Family ? Shown(form) : match.data.name;
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------------------------------
        //  One piece of label text
        // ---------------------------------------------------------------------------------------
        static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string c = s.Replace('\n', ' ').Replace('\\', '/');                             // "Water\Aqua\Eau" is "Water/Aqua/Eau"
            c = Regex.Replace(c, @"\d+([.,]\d+)?\s*%", " ");                                // percentages
            c = Regex.Replace(c, @"[\[\(]\s*nano\s*[\]\)]", " ", RegexOptions.IgnoreCase);    // "Titanium Dioxide [nano]"
            c = Regex.Replace(c, @"[^\p{L}\p{N}\s\-/'.,()+&\[\]]", " ");                     // stray symbols from the camera, footnote marks
            c = Regex.Replace(c, @"\s+", " ").Trim().Trim('.', '-', '/', '\'', ':', '&', '+').Trim();
            c = Regex.Replace(c, @"^(and|et|y)\s+", "", RegexOptions.IgnoreCase);             // "... and Tocopherol"
            return c.Length < 2 ? "" : c;
        }

        /// <summary>
        /// The ways one piece can name its ingredient, most likely first: without its brackets ("Water" of "Water (Aqua)"),
        /// as it stands, what is inside the brackets, each side of a slash ("Aqua/Water/Eau"), and without a leading
        /// "Organic" or "Natural".
        /// </summary>
        static List<string> FormsOf(string piece)
        {
            var forms = new List<string>();
            void Add(string form)
            {
                form = Regex.Replace(form ?? "", @"\s+", " ").Trim().Trim('.', '-', '/', '\'', ':', '(', ')', '[', ']').Trim();
                if (form.Length >= 2 && !forms.Contains(form)) forms.Add(form);
            }

            string outside = Regex.Replace(piece, @"[\(\[][^\)\]]*[\)\]]?", " ");
            Add(outside);
            Add(piece);
            foreach (Match inside in Regex.Matches(piece, @"[\(\[]([^\)\]]*)[\)\]]?")) Add(inside.Groups[1].Value);
            foreach (string source in new[] { outside, piece })
                if (source.IndexOf('/') >= 0)
                    foreach (string side in source.Split('/')) if (side.Trim().Length >= 3) Add(side);
            // "Water (Aqua/Eau)": the names inside the brackets, one by one
            foreach (Match inside in Regex.Matches(piece, @"[\(\[]([^\)\]]*)[\)\]]?"))
                if (inside.Groups[1].Value.IndexOf('/') >= 0)
                    foreach (string side in inside.Groups[1].Value.Split('/')) if (side.Trim().Length >= 3) Add(side);
            string plain = Regex.Replace(outside.Trim(), @"^(certified organic|organic|natural|pure|bio)\s+", "", RegexOptions.IgnoreCase);
            if (plain != outside.Trim()) Add(plain);
            return forms;
        }

        /// <summary>The name as it is shown: kept as written when the label used capitals and small letters, otherwise with a capital for each word.</summary>
        static string Shown(string name)
        {
            name = (name ?? "").Trim();
            Match colour = Regex.Match(name, @"^C\.?I\.?\s*(\d{5})$", RegexOptions.IgnoreCase);
            if (colour.Success) return "CI " + colour.Groups[1].Value;
            bool upper = false, lower = false;
            foreach (char c in name) { if (char.IsUpper(c)) upper = true; else if (char.IsLower(c)) lower = true; }
            if (upper && lower) return name;
            string titled = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
            // short forms that are written in capitals: "PEG-40", "BHT", "Disodium EDTA"
            return Regex.Replace(titled, @"\b(Peg|Ppg|Pvp|Vp|Va|Bht|Bha|Edta|Mea|Tea|Dea|Mipa|Pca|Ci|Hdi|Dmdm)\b", m => m.Value.ToUpperInvariant());
        }

        /// <summary>
        /// Cuts a piece into known names when EVERY word of it belongs to one ("Glycerin Niacinamide Dimethicone": the
        /// camera missed two commas). Null when that does not work out: then the piece is one name.
        /// </summary>
        static List<string> Segment(string piece)
        {
            // words that only join two names are left out ("Glycerin and Niacinamide")
            var kept = new List<string>();
            foreach (string word in piece.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (!(word == "&" || word == "+" || string.Equals(word, "and", StringComparison.OrdinalIgnoreCase) || string.Equals(word, "plus", StringComparison.OrdinalIgnoreCase)))
                    kept.Add(word);
            string[] words = kept.ToArray();
            if (words.Length < 2 || words.Length > 12) return null;
            var names = new List<string>();
            int at = 0;
            while (at < words.Length)
            {
                string found = null; int taken = 0;
                for (int count = Math.Min(6, words.Length - at); count >= 1; count--)
                {
                    string candidate = string.Join(" ", words, at, count);
                    if (candidate.Length < 4) continue;                                    // too short to be sure ("mi", "aha")
                    IngredientKnowledge.Match match = IngredientKnowledge.Find(candidate);
                    if (match.kind != IngredientKnowledge.MatchKind.Exact) continue;
                    found = match.data.name; taken = count; break;
                }
                if (found == null) return null;
                names.Add(found); at += taken;
            }
            return names.Count >= 2 ? names : null;
        }

        /// <summary>
        /// The first piece of a list often has other label text in front of the ingredient, the last piece behind it.
        /// When the END of the first piece (or the START of the last) is exactly a known name, that name is the ingredient.
        /// </summary>
        static bool Rescue(string piece, bool takeEnd, out string name)
        {
            name = null;
            string[] words = piece.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) return false;
            for (int count = Math.Min(6, words.Length - 1); count >= 1; count--)
            {
                string candidate = takeEnd ? string.Join(" ", words, words.Length - count, count) : string.Join(" ", words, 0, count);
                if (candidate.Length < 4) continue;
                IngredientKnowledge.Match match = IngredientKnowledge.Find(candidate);
                if (match.kind == IngredientKnowledge.MatchKind.Exact) { name = match.data.name; return true; }
            }
            return false;
        }
    }
}
