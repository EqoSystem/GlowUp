using System;
using System.Collections.Generic;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// What GlowUp is for, in one place: MAKE-UP ONLY.
    ///
    ///   Make-up    - foundation, concealer, powder, blush, bronzer, highlighter, contour, eyeshadow, eyeliner, mascara,
    ///                brow products, lipstick, lip gloss, lip tint, lip liner, primer, setting spray and the like.
    ///                Checked against the user's profile, kept in the inventory, recommended, offered for AR Try-On.
    ///   Not make-up - skin care (moisturizer, serum, cleanser, toner, sunscreen, mask, make-up remover ...) and
    ///                hair, body, nail and fragrance products. Never added, shown or recommended. A scan of one is
    ///                stopped with "GlowUp only checks make-up products".
    ///   Unclear    - the app cannot tell (no name and no kind), or the kind is one the project has not decided on yet
    ///                (see <see cref="Undecided"/>). Such a product can still be checked for the user who holds it, but it
    ///                is not added to the inventory, not shown and not recommended until an admin has looked at it.
    ///
    /// The app never suggests a cure or a treatment. When the user has a skin condition it warns and explains how the
    /// product may affect it, and every result ends with the advice to see a dermatologist.
    ///
    /// The admin website uses the same rule (productScope in _Shared/glowupRules.js) and both are checked against the
    /// same list of examples (Assets/_Project/Editor/Tests/MakeupScopeCases.txt). Change the word lists in both places.
    /// </summary>
    public static class ProductScope
    {
        public enum Kind { Makeup, SkinCare, OutOfScope, Unclear }

        /// <summary>What the rule decided, and why.</summary>
        public struct Verdict
        {
            public Kind kind;
            /// <summary>For a product that is not make-up: "skin care", "hair", "body care", "nail", "fragrance" or "make-up tool".</summary>
            public string family;
            /// <summary>The word that decided it ("" when nothing was recognised).</summary>
            public string word;
            public bool IsMakeup => kind == Kind.Makeup;
            /// <summary>True when the product is clearly something else than make-up (an unclear product is not).</summary>
            public bool IsNotMakeup => kind == Kind.SkinCare || kind == Kind.OutOfScope;
        }

        /// <summary>The kinds of make-up the app knows by name. The inventory, the type list on the scan screen and the AI all use these words.</summary>
        public static readonly string[] MakeupTypes =
        {
            "Lipstick", "Lip Gloss", "Lip Tint", "Lip Liner", "Foundation", "Concealer", "Primer", "Powder", "Blush", "Bronzer",
            "Highlighter", "Contour", "Eyeshadow", "Eyeliner", "Mascara", "Brow", "Setting Spray"
        };

        /// <summary>
        /// Finds a supported make-up type when its label appears in OCR text; returns empty when it is unclear. Capitals never matter
        /// ("Foundation", "FOUNDATION", "foundation"); see TypeFromMisread for what a camera does to the word on a real pack.
        /// </summary>
        public static string TypeFromLabel(string text)
        {
            string key = " " + TextKey.Normalize(text) + " ";
            foreach (string type in MakeupTypes)
            {
                string normalized = TextKey.Normalize(type);
                if (normalized.Length > 0 && key.Contains(" " + normalized + " ")) return type;
            }
            if (key.Contains(" lip stain ")) return "Lip Tint";
            if (key.Contains(" liquid lipstick ")) return "Lipstick";
            if (key.Contains(" cheek tint ") || key.Contains(" cheek stain ")) return "Blush";
            if (key.Contains(" loose powder ") || key.Contains(" pressed powder ") || key.Contains(" face powder ")) return "Powder";
            return TypeFromMisread(key);
        }

        // the last rows of TypeWords ("gloss", "tint", "liner") are too common on a label to name a kind by themselves
        const int LooseTypeWords = 3;
        // shorter words ("brow", "kohl") are only taken as written: one misread letter makes them another word
        const int ShortestMisreadWord = 5;
        const int ShortestWordWithLostLetter = 8;

        /// <summary>
        /// The kind of make-up when the word is on the label but not as the app spells it, in this order: another spelling or a plural
        /// ("EYE SHADOW", "BLUSHER", "FOUNDATIONS"); letters spaced apart for style ("F O U N D A T I O N"); a word cut in two
        /// ("FOUN DATION", or at the end of a line); letters a camera confuses ("F0UNDATION", "FOUNDATlON", TextKey.Squash); one letter
        /// lost from a long word ("FOUNDATON"). A wrong letter is NOT forgiven, so "CONCEALED" and "BROWN" stay what they are.
        /// key = " " + TextKey.Normalize(text) + " ".
        /// </summary>
        static string TypeFromMisread(string key)
        {
            string joined = JoinLoneLetters(key);
            int rows = TypeWords.Length - LooseTypeWords;
            for (int row = 0; row < rows; row++)
                for (int i = 1; i < TypeWords[row].Length; i++)
                    if (Holds(joined, TypeWords[row][i])) return TypeWords[row][0];

            string[] read = joined.Trim().Split(' ');
            var squashed = new HashSet<string>();
            for (int i = 0; i < read.Length; i++)
            {
                squashed.Add(TextKey.Squash(read[i]));
                if (i + 1 < read.Length && read[i].Length > 1 && read[i + 1].Length > 1) squashed.Add(TextKey.Squash(read[i] + read[i + 1]));
            }
            for (int row = 0; row < rows; row++)
                for (int i = 1; i < TypeWords[row].Length; i++)
                {
                    string word = TextKey.Squash(TypeWords[row][i]);
                    if (word.Length >= ShortestMisreadWord && (squashed.Contains(word) || squashed.Contains(word + "s"))) return TypeWords[row][0];
                }
            for (int row = 0; row < rows; row++)
                for (int i = 1; i < TypeWords[row].Length; i++)
                {
                    string word = TextKey.Squash(TypeWords[row][i]);
                    if (word.Length < ShortestWordWithLostLetter) continue;
                    foreach (string seen in squashed) if (LostOneLetter(seen, word)) return TypeWords[row][0];
                }
            return "";
        }

        /// <summary>" f o u n d a t i o n " gives " foundation ": three or more single letters in a row are one word spaced apart.</summary>
        static string JoinLoneLetters(string key)
        {
            string[] words = key.Trim().Split(' ');
            var kept = new System.Text.StringBuilder(" ");
            for (int i = 0; i < words.Length;)
            {
                int end = i;
                while (end < words.Length && words[end].Length == 1) end++;
                if (end - i >= 3) { for (int j = i; j < end; j++) kept.Append(words[j]); kept.Append(' '); i = end; }
                else { kept.Append(words[i]).Append(' '); i++; }
            }
            return kept.ToString();
        }

        /// <summary>True when "seen" is "word" with exactly one letter missing.</summary>
        static bool LostOneLetter(string seen, string word)
        {
            if (seen.Length != word.Length - 1) return false;
            int at = 0;
            while (at < seen.Length && seen[at] == word[at]) at++;
            return string.CompareOrdinal(seen, at, word, at + 1, seen.Length - at) == 0;
        }

        // ---------------------------------------------------------------------------------------
        //  The word lists. Each word is looked for as a whole word (a plural with -s or -es counts too).
        // ---------------------------------------------------------------------------------------
        /// <summary>Words that always mean make-up.</summary>
        static readonly string[] Strong =
        {
            "lipstick", "lip stick", "lip gloss", "lipgloss", "lip tint", "lip stain", "lip liner", "lipliner", "lip lacquer", "lip cream",
            "lip colour", "lip color", "lip crayon", "lip pencil", "lip mousse", "lip matte", "liquid lip", "lip paint", "lip glaze", "lip shine",
            "eyeshadow", "eye shadow", "eyeliner", "eye liner", "mascara", "blush", "blusher", "concealer", "foundation",
            "eyebrow", "brow pencil", "brow gel", "brow pomade", "brow powder", "brow mascara", "brow pen", "brow liner", "brow tint", "brow wax",
            "brow definer", "brow soap", "soap brow", "kajal", "kohl", "face powder", "pressed powder", "loose powder", "compact powder",
            "setting powder", "finishing powder", "powder foundation", "cushion", "contour", "highlighter", "bronzer", "makeup", "make up",
            "cheek tint", "cheek stain", "cheek color", "cheek colour", "setting spray", "fixing spray", "setting mist", "fixing mist",
            // Decided on 12 October 2026 by how they are sold and regulated: BB and CC creams, tinted moisturizers and skin tints are
            // face make-up (shops list them under Makeup > Face; the ASEAN list of cosmetics has "tinted bases")
            "bb cream", "cc cream", "tinted moisturizer", "tinted moisturiser", "skin tint"
        };
        /// <summary>Words that mean make-up unless another word says otherwise ("baby powder", "hair gloss", "primer serum").</summary>
        static readonly string[] Generic = { "powder", "primer", "gloss", "tint", "liner", "brow", "palette", "glitter", "shimmer", "corrector", "illuminator", "rouge", "stain" };

        static readonly string[] HairWords = { "shampoo", "conditioner", "hair", "scalp", "haircare" };
        static readonly string[] NailWords = { "nail", "cuticle", "manicure" };
        static readonly string[] ScentWords = { "perfume", "cologne", "eau de", "parfum", "body mist", "fragrance mist" };
        static readonly string[] BodyWords =
        {
            "body", "bodylotion", "bodywash", "hand", "foot", "feet", "deodorant", "antiperspirant", "soap", "shower", "bath", "shaving", "shave",
            "toothpaste", "mouthwash", "oral", "intimate", "feminine wash", "baby", "tanning", "massage", "sanitizer", "sanitiser"
        };
        static readonly string[] CareWords =
        {
            "moisturizer", "moisturiser", "serum", "cleanser", "toner", "sunscreen", "sunblock", "sun screen", "sun block", "mask", "essence",
            "ampoule", "lotion", "face wash", "facial wash", "wash", "scrub", "exfoliant", "exfoliator", "peel", "emulsion", "acne", "pimple",
            "treatment", "skincare", "skin care", "eye cream", "night cream", "day cream", "sleeping pack", "spot corrector"
        };
        /// <summary>Skin-care words that only count when the name holds no make-up word at all ("cream blush" is make-up).</summary>
        static readonly string[] WeakCareWords = { "cream", "gel", "balm", "mist", "spf", "water", "butter", "oil", "uv" };

        /// <summary>Checked first: these are never make-up, whatever else the name says.</summary>
        static readonly string[] NeverMakeup =
        {
            "makeup remover", "make up remover", "remover", "cleansing", "micellar", "lip scrub", "lip mask", "lip treatment", "lip care",
            "lip serum", "lip conditioner", "brow serum", "lash serum", "eyelash serum", "lash conditioner", "brow conditioner",
            // decided on 12 October 2026: lip balms and lip oils are lip CARE, and a tinted sunscreen is a sunscreen (sun care)
            "lip balm", "lip oil", "tinted sunscreen", "tinted sunblock", "tinted sun screen", "tinted sun block"
        };
        static readonly string[] NeverMakeupOther = { "hair mascara", "nail polish", "nail lacquer", "makeup brush", "make up brush", "brush cleaner", "makeup sponge", "make up sponge" };

        /// <summary>
        /// Kinds that are care and colour at once, which no source places clearly. They count as unclear: kept for an
        /// admin to look at, not shown, not recommended and not added by the phone. They are looked for BEFORE the
        /// "never make-up" words (a tinted lip balm is not simply a lip balm). To decide one, move its words to
        /// <see cref="Strong"/> (it is make-up) or to <see cref="NeverMakeup"/> (it is not), here and on the website.
        /// (BB and CC cream, tinted moisturizer, skin tint, lip balm, lip oil and tinted sunscreen were decided on
        /// 12 October 2026: see those two lists.)
        /// </summary>
        public static readonly string[] Undecided = { "tinted lip balm", "tinted lip oil", "lip balm tint", "lip oil tint" };

        /// <summary>
        /// Product words that are unmistakable on the front of a pack. Used when all the app has is the text the camera
        /// read (no AI, no inventory match): only these stop a scan, and only when no make-up word stands on the pack.
        /// </summary>
        static readonly string[] LabelNotMakeup =
        {
            "shampoo", "hair conditioner", "body wash", "bodywash", "body lotion", "hand cream", "hand lotion", "deodorant", "antiperspirant",
            "eau de parfum", "eau de toilette", "cologne", "nail polish", "nail lacquer", "toothpaste", "mouthwash",
            "facial wash", "face wash", "facial cleanser", "facial foam", "micellar water", "makeup remover", "make up remover", "feminine wash",
            "baby oil", "baby powder", "body scrub", "bath soap", "hand soap", "hand sanitizer"
        };

        /// <summary>What the AI answers for a product that is not make-up (see <see cref="AiTypeChoices"/>), and the old catch-all kind "Skincare".</summary>
        static string DeclaredFamily(string typeKey)
        {
            switch (typeKey)
            {
                case "skin care": case "skincare": case "face care": return "skin care";
                case "hair": case "hair care": case "haircare": return "hair";
                case "body": case "body care": case "bodycare": return "body care";
                case "nail": case "nails": case "nail care": return "nail";
                case "fragrance": case "perfume": return "fragrance";
                default: return null;
            }
        }

        // ---------------------------------------------------------------------------------------
        //  The rule
        // ---------------------------------------------------------------------------------------
        /// <summary>Is this product make-up? `cosmeticType` is the kind someone gave it (the AI, the inventory, the user); either may be empty.</summary>
        public static Verdict Judge(string cosmeticType, string productName)
        {
            string typeKey = TextKey.Normalize(cosmeticType);
            if (typeKey == "unknown" || typeKey == "other" || typeKey == "cosmetic" || typeKey == "cosmetics") { cosmeticType = ""; typeKey = ""; }   // "I cannot tell" is not a kind
            string name = Plain(productName), text = Plain((cosmeticType ?? "") + " " + (productName ?? ""));
            if (text.Trim().Length == 0) return Result(Kind.Unclear, "", "");

            // the kind was given as "not make-up" outright: only a make-up word in the name can still make it unclear
            string declared = DeclaredFamily(typeKey);
            if (declared != null)
            {
                if (Has(name, Strong) != null || Has(name, Undecided) != null) return Result(Kind.Unclear, "", typeKey);
                return Result(declared == "skin care" ? Kind.SkinCare : Kind.OutOfScope, declared, typeKey);
            }

            string word = Has(text, Undecided);
            if (word != null) return Result(Kind.Unclear, "", word);
            word = Has(text, NeverMakeup);
            if (word != null) return Result(Kind.SkinCare, "skin care", word);
            word = Has(text, NeverMakeupOther);
            if (word != null) return Result(Kind.OutOfScope, word.StartsWith("hair") ? "hair" : word.StartsWith("nail") ? "nail" : "make-up tool", word);

            string strong = Has(text, Strong), generic = Has(text, Generic);
            bool browOrLash = Has(text, new[] { "brow", "eyebrow", "lash", "eyelash" }) != null;
            string hair = Has(text, HairWords);
            if (hair == "hair" && browOrLash) hair = null;                 // "hair-like strokes" on a brow pen is not a hair product
            string nail = Has(text, NailWords), scent = Has(text, ScentWords), body = Has(text, BodyWords), care = Has(text, CareWords);
            string other = hair ?? nail ?? scent ?? body;
            string family = hair != null ? "hair" : nail != null ? "nail" : scent != null ? "fragrance" : body != null ? "body care" : "";

            bool nameSaysMakeup = Has(name, Strong) != null || Has(name, Generic) != null;
            bool nameSaysOther = Has(name, HairWords) != null || Has(name, NailWords) != null || Has(name, ScentWords) != null
                              || Has(name, BodyWords) != null || Has(name, CareWords) != null;

            if (strong != null)
            {
                if (hair != null || nail != null) return Result(Kind.Unclear, "", hair ?? nail);
                // the make-up word is only in the kind, while the name speaks of something else ("Blush" + "Body Lotion")
                if (!nameSaysMakeup && nameSaysOther) return Result(Kind.Unclear, "", other ?? care);
                return Result(Kind.Makeup, "", strong);
            }
            if (generic != null)
            {
                if (other != null) return Has(name, Generic) != null ? Result(Kind.OutOfScope, family, other) : Result(Kind.Unclear, "", other);
                if (care != null) return Result(Kind.Unclear, "", care);
                return Result(Kind.Makeup, "", generic);
            }
            if (other != null) return Result(Kind.OutOfScope, family, other);
            if (care != null) return Result(Kind.SkinCare, "skin care", care);
            word = Has(text, WeakCareWords);
            if (word != null) return Result(Kind.SkinCare, "skin care", word);
            return Result(Kind.Unclear, "", "");
        }

        /// <summary>
        /// The same question for the raw text the camera read on the FRONT of a pack. Careful on purpose: it only says
        /// "not make-up" for unmistakable product words, and never when a make-up word is on the pack as well.
        /// </summary>
        public static Verdict JudgeLabel(string frontText)
        {
            string text = Plain(frontText);
            if (text.Trim().Length == 0) return Result(Kind.Unclear, "", "");
            string remover = Has(text, new[] { "makeup remover", "make up remover" });
            if (remover != null) return Result(Kind.SkinCare, "skin care", remover);
            string makeup = Has(text, Strong);
            if (makeup != null) return Result(Kind.Makeup, "", makeup);
            string word = Has(text, LabelNotMakeup);
            if (word == null || Has(text, Undecided) != null) return Result(Kind.Unclear, "", "");
            Verdict whole = Judge("", word);
            return whole.IsNotMakeup ? whole : Result(Kind.SkinCare, "skin care", word);
        }

        public static Kind Classify(string cosmeticType, string productName) => Judge(cosmeticType, productName).kind;
        public static Kind Classify(ScanRecord record) => record == null ? Kind.Unclear : Classify(record.cosmeticType, record.productName);
        public static bool IsMakeup(string cosmeticType, string productName) => Judge(cosmeticType, productName).IsMakeup;

        /// <summary>AR Try-On is only for make-up.</summary>
        public static bool CanTryOn(ScanRecord record) => record != null && Classify(record) == Kind.Makeup;

        // "Lip Cream" is sold as a lipstick, a "Cushion" is a foundation ... : the app's own word for a kind of make-up
        static readonly string[][] TypeWords =
        {
            new[] { "Lip Gloss", "lip gloss", "lipgloss", "lip glaze", "lip shine" },
            new[] { "Lip Tint", "lip tint", "lip stain" },
            new[] { "Lip Liner", "lip liner", "lipliner", "lip pencil" },
            new[] { "Lipstick", "lipstick", "lip stick", "lip cream", "lip lacquer", "liquid lip", "lip colour", "lip color", "lip crayon", "lip mousse", "lip matte", "lip paint" },
            new[] { "Setting Spray", "setting spray", "fixing spray", "setting mist", "fixing mist" },
            new[] { "Brow", "eyebrow", "brow" },
            new[] { "Foundation", "powder foundation", "foundation", "cushion", "bb cream", "cc cream", "tinted moisturizer", "tinted moisturiser", "skin tint" },
            new[] { "Concealer", "concealer", "corrector" },
            new[] { "Primer", "primer" },
            new[] { "Eyeshadow", "eyeshadow", "eye shadow" },
            new[] { "Eyeliner", "eyeliner", "eye liner", "kajal", "kohl" },
            new[] { "Mascara", "mascara" },
            new[] { "Blush", "blush", "blusher", "cheek tint", "cheek stain", "cheek color", "cheek colour" },
            new[] { "Bronzer", "bronzer" },
            new[] { "Contour", "contour" },
            new[] { "Highlighter", "highlighter", "illuminator" },
            new[] { "Powder", "powder" },
            new[] { "Lip Gloss", "gloss" },
            new[] { "Lip Tint", "tint" },
            new[] { "Eyeliner", "liner" }
        };

        /// <summary>
        /// The app's own word for this kind of make-up ("Liquid Lipstick" and "Lip Cream" give "Lipstick"), or "" when the
        /// product is not clearly make-up or its kind cannot be told. Only a product with such a word is added to the inventory.
        /// </summary>
        public static string MakeupType(string cosmeticType, string productName)
        {
            if (!Judge(cosmeticType, productName).IsMakeup) return "";
            string type = Plain(cosmeticType), name = Plain(productName);
            foreach (string known in MakeupTypes) if (type.Trim() == known.ToLowerInvariant()) return known;
            foreach (string text in new[] { type, name })
                foreach (string[] words in TypeWords)
                    for (int i = 1; i < words.Length; i++)
                        if (Holds(text, words[i])) return words[0];
            return "";
        }

        // ---------------------------------------------------------------------------------------
        //  What the user is told
        // ---------------------------------------------------------------------------------------
        public const string DermatologistLine = "A guide for buying decisions. Not medical advice.";
        /// <summary>The treatment phrase used before 27 Nov 2026; still found in scans and scenes saved before then.</summary>
        public const string OldDermatologistLine = "Consult a dermatologist for the best treatment.";
        /// <summary>Old wording replaced by the current one (cautions only, no treatment advice).</summary>
        public static string CurrentWording(string text) => string.IsNullOrEmpty(text) ? text ?? ""
            : text.Replace(OldDermatologistLine, DermatologistLine).Replace("May aggravate ", "Caution for ").Replace("often used for", "listed for");
        /// <summary>Said when a scan is stopped because the product is not make-up. {0} = "a hair product", "a skin care product" ...</summary>
        public static string NotMakeupMessage = "GlowUp only checks make-up products. This looks like {0}. Scan again, or type the name if we got it wrong.";
        public static string NotMakeupMessagePlain = "GlowUp only checks make-up products. Scan again, or type the name if we got it wrong.";
        public static string ConditionNotice = "You listed {0}. GlowUp does not suggest a cure or a treatment. It only shows cautions.";
        /// <summary>Kept for scans saved before the app became make-up only.</summary>
        public static string OutOfScopeNotice = "GlowUp only checks make-up products. This looks like a {0} product, so the result only lists ingredient warnings and AR Try-On is not available for it.";
        public static string SkinCareNotice = "This is a skin care product. GlowUp only checks make-up products: the result only lists ingredient warnings and AR Try-On is not available for it.";

        /// <summary>The sentence shown when a scan is stopped ("GlowUp only checks make-up products. This looks like a hair product ...").</summary>
        public static string StopMessage(Verdict verdict)
        {
            if (string.IsNullOrEmpty(verdict.family)) return NotMakeupMessagePlain;
            return string.Format(NotMakeupMessage, "a " + verdict.family + (verdict.family == "make-up tool" ? "" : " product"));
        }

        /// <summary>The notice for this product and this user ("" when there is nothing to say).</summary>
        public static string NoticeFor(ScanRecord record, UserProfileData profile)
        {
            var parts = new List<string>();
            Verdict verdict = record == null ? Result(Kind.Unclear, "", "") : Judge(record.cosmeticType, record.productName);
            if (verdict.kind == Kind.OutOfScope) parts.Add(string.Format(OutOfScopeNotice, verdict.family == "body care" ? "body" : verdict.family));
            else if (verdict.kind == Kind.SkinCare) parts.Add(SkinCareNotice);

            List<string> conditions = SkinConditionsOf(profile);
            if (conditions.Count > 0) parts.Add(string.Format(ConditionNotice, string.Join(", ", conditions)));
            return string.Join("\n\n", parts);
        }

        /// <summary>The skin conditions saved in a profile (an answer such as "None" is not one).</summary>
        public static List<string> SkinConditionsOf(UserProfileData profile)
        {
            var conditions = new List<string>();
            if (profile == null || profile.skinConditions == null) return conditions;
            foreach (string c in profile.skinConditions)
            {
                string key = TextKey.Normalize(c);
                if (key.Length == 0 || key == "none" || key == "no" || key == "n a" || key == "na" || key == "nothing" || key == "no skin condition") continue;
                conditions.Add(c.Trim());
            }
            return conditions;
        }

        /// <summary>True when the user saved a skin condition (acne, eczema, psoriasis, rosacea, dermatitis, melasma or one they typed in).</summary>
        public static bool HasSkinCondition(UserProfileData profile) => SkinConditionsOf(profile).Count > 0;

        /// <summary>
        /// Writes the scope into a finished scan: its kind, and the notice at the top of "Why this result?". Returns the
        /// notice ("" when there is none). The advice to see a dermatologist is the last card of the result screen
        /// (ScanResultFlowController): it is not repeated at the end of this text.
        /// </summary>
        public static string Apply(ScanRecord record, UserProfileData profile)
        {
            if (record == null) return "";
            // a face powder is tried on as white: the colour of its box (or of the powder in the pan) is not how it looks on skin
            if (ShadeAdvisor.IsFacePowder(record.cosmeticType)) record.shadeHex = ShadeAdvisor.PowderShade;
            Kind kind = Classify(record);
            record.scope = kind == Kind.Makeup ? "makeup" : kind == Kind.SkinCare ? "skincare" : kind == Kind.OutOfScope ? "out" : "unclear";
            string notice = NoticeFor(record, profile);
            record.scopeNote = notice;

            string explanation = (record.explanation ?? "").Trim();
            if (notice.Length > 0 && !explanation.Contains(notice)) explanation = "<b>Please note</b>\n" + notice + (explanation.Length > 0 ? "\n\n" + explanation : "");
            record.explanation = explanation;
            return notice;
        }

        /// <summary>The rules every question to the AI carries. The user's profile is never part of a question.</summary>
        public const string AiRules =
            "RULES YOU MUST FOLLOW: This app is about make-up only. " +
            "Never suggest a cure, a treatment, a medicine or a dose for any skin condition, and never diagnose. " +
            "If the product is not make-up (skin care, hair, body, nail or fragrance), say in one sentence that the app only checks make-up and stop. " +
            "Do not end with advice of your own: the app adds the line about seeing a dermatologist itself.";

        /// <summary>The kinds the AI may answer with when it looks at a product: the make-up kinds, and what to say for anything else.</summary>
        public static string AiTypeChoices =>
            "one of these kinds of make-up: " + string.Join(", ", MakeupTypes) +
            "; or, when the product is NOT make-up, exactly one of: Skin Care, Hair, Body, Nail, Fragrance; or Unknown when you cannot tell";

        // ---------------------------------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------------------------------
        static Verdict Result(Kind kind, string family, string word) => new Verdict { kind = kind, family = family ?? "", word = word ?? "" };

        /// <summary>Lower case with a space at both ends, punctuation as spaces, and "... free" claims taken out ("fragrance free" says nothing about the kind).</summary>
        static string Plain(string text)
        {
            string key = TextKey.Normalize(text);
            if (key.Length == 0) return " ";
            string[] words = key.Split(' ');
            var kept = new System.Text.StringBuilder(" ");
            for (int i = 0; i < words.Length; i++)
            {
                if (i + 1 < words.Length && words[i + 1] == "free" && IsLetters(words[i])) { i++; continue; }
                kept.Append(words[i]).Append(' ');
            }
            return kept.ToString();
        }

        static bool IsLetters(string word)
        {
            foreach (char c in word) if (c < 'a' || c > 'z') return false;
            return word.Length > 0;
        }

        /// <summary>The first word of the list that the text holds, or null.</summary>
        static string Has(string text, string[] words)
        {
            foreach (string word in words) if (Holds(text, word)) return word;
            return null;
        }

        /// <summary>True when the text holds the word as a whole word (also with -s or -es at its end).</summary>
        static bool Holds(string text, string word)
        {
            return text.IndexOf(" " + word + " ", StringComparison.Ordinal) >= 0
                || text.IndexOf(" " + word + "s ", StringComparison.Ordinal) >= 0
                || text.IndexOf(" " + word + "es ", StringComparison.Ordinal) >= 0;
        }
    }
}
