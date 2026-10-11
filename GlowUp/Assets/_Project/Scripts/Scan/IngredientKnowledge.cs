using System;
using System.Collections.Generic;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// What the app knows about common cosmetic ingredients WITHOUT the internet: a short description,
    /// what products use it, and who should take care with it. This is general cosmetic-safety guidance,
    /// not medical advice.
    ///
    /// The admin website always wins: when the 'ingredients' collection has an entry for the same name,
    /// its name / description / fields are used and only the gaps are filled from here.
    /// To add or correct an entry, add a line in <see cref="Build"/> (or, better, add it on the website).
    /// </summary>
    public static class IngredientKnowledge
    {
        static List<IngredientData> all;
        static Dictionary<string, IngredientData> byKey;

        // ---------------------------------------------------------------------------------------
        //  Lookup
        // ---------------------------------------------------------------------------------------
        /// <summary>Best information available for a scanned ingredient name, or null when nothing is known.</summary>
        public static IngredientData Resolve(string scannedName)
        {
            if (string.IsNullOrWhiteSpace(scannedName)) return null;
            Ensure();
            string key = Key(scannedName);

            IngredientData builtIn = FindBuiltIn(key);
            IngredientData fromWebsite = IngredientRepository.Find(scannedName);
            if (fromWebsite == null && builtIn != null)
                fromWebsite = IngredientRepository.Find(builtIn.name) ?? IngredientRepository.Find(builtIn.techName);

            // An entry the AI lookup added that no admin has reviewed yet never overrides what the app already knows.
            if (fromWebsite != null && fromWebsite.IsUnreviewedAi())
                return builtIn ?? Family(key, scannedName) ?? Clone(fromWebsite);
            if (fromWebsite != null) return Merge(fromWebsite, builtIn);
            return builtIn ?? Family(key, scannedName);
        }

        /// <summary>True when the name is a real ingredient we recognise (used to judge whether a camera read worked).</summary>
        public static bool IsKnown(string scannedName) => Resolve(scannedName) != null;

        static IngredientData FindBuiltIn(string key)
        {
            if (byKey.TryGetValue(key, out var exact)) return exact;

            // the camera often misreads a letter or two: accept a very close spelling of a long name
            if (key.Length >= 7)
            {
                IngredientData best = null; int bestDistance = int.MaxValue;
                int allowed = key.Length >= 14 ? 2 : 1;
                foreach (var kv in byKey)
                {
                    if (Math.Abs(kv.Key.Length - key.Length) > allowed) continue;
                    int d = Distance(key, kv.Key, allowed);
                    if (d < bestDistance) { bestDistance = d; best = kv.Value; }
                }
                if (best != null && bestDistance <= allowed) return best;
            }
            return null;
        }

        /// <summary>Ingredient families that can be recognised from part of the name.</summary>
        static IngredientData Family(string key, string shownName)
        {
            IngredientData Like(string builtInName, string description = null)
            {
                var source = byKey.TryGetValue(Key(builtInName), out var s) ? s : null;
                if (source == null) return null;
                var copy = Clone(source);
                copy.name = shownName; copy.techName = "";
                if (description != null) copy.description = description;
                return copy;
            }

            if (key.Contains("paraben")) return Like("Methylparaben", "A paraben preservative that stops bacteria and mould growing in the product.");
            if (key.Contains("parfum") || key.Contains("fragrance") || key.Contains("perfume")) return Like("Fragrance");
            if (key.Contains("sulfate") && (key.Contains("lauryl") || key.Contains("laureth") || key.Contains("coco")))
                return Like("Sodium Laureth Sulfate", "A sulfate cleansing agent that makes the product foam.");
            if (key.Contains("alcohol denat") || key == "alcohol" || key == "ethanol") return Like("Alcohol Denat.");
            if (key.StartsWith("ci ") && key.Length <= 9)
                return new IngredientData { name = shownName, description = "A colourant, listed by its Colour Index number.", commonUses = "Makeup and tinted products", riskLevel = "safe" };
            if (key.EndsWith(" extract"))
                return new IngredientData { name = shownName, description = "A plant extract. Plant extracts are usually well tolerated, but individual sensitivities vary.", commonUses = "Skincare and makeup", riskLevel = "safe" };
            return null;
        }

        static IngredientData Merge(IngredientData site, IngredientData builtIn)
        {
            var m = Clone(site);
            if (builtIn == null) { if (string.IsNullOrEmpty(m.riskLevel)) m.riskLevel = "safe"; return m; }
            if (string.IsNullOrEmpty(m.name)) m.name = builtIn.name;
            if (string.IsNullOrEmpty(m.techName)) m.techName = builtIn.techName;
            if (string.IsNullOrEmpty(m.description)) m.description = builtIn.description;
            if (string.IsNullOrEmpty(m.riskLevel)) m.riskLevel = builtIn.riskLevel;
            if (string.IsNullOrEmpty(m.cautionNote)) m.cautionNote = builtIn.cautionNote;
            if (string.IsNullOrEmpty(m.commonUses)) m.commonUses = builtIn.commonUses;
            if (m.minAge == 0) m.minAge = builtIn.minAge;
            m.keyActive |= builtIn.keyActive; m.umbrella |= builtIn.umbrella;
            if (m.allergenTags.Count == 0) m.allergenTags = new List<string>(builtIn.allergenTags);
            if (m.avoidForSkinTypes.Count == 0) m.avoidForSkinTypes = new List<string>(builtIn.avoidForSkinTypes);
            if (m.avoidForConditions.Count == 0) m.avoidForConditions = new List<string>(builtIn.avoidForConditions);
            if (m.goodForSkinTypes.Count == 0) m.goodForSkinTypes = new List<string>(builtIn.goodForSkinTypes);
            if (m.goodForConditions.Count == 0) m.goodForConditions = new List<string>(builtIn.goodForConditions);
            return m;
        }

        static IngredientData Clone(IngredientData s) => new IngredientData
        {
            id = s.id, name = s.name, techName = s.techName, description = s.description, riskLevel = s.riskLevel,
            cautionNote = s.cautionNote, commonUses = s.commonUses, minAge = s.minAge, keyActive = s.keyActive, umbrella = s.umbrella,
            aliases = new List<string>(s.aliases ?? new List<string>()),
            allergenTags = new List<string>(s.allergenTags ?? new List<string>()),
            avoidForSkinTypes = new List<string>(s.avoidForSkinTypes ?? new List<string>()),
            avoidForConditions = new List<string>(s.avoidForConditions ?? new List<string>()),
            goodForSkinTypes = new List<string>(s.goodForSkinTypes ?? new List<string>()),
            goodForConditions = new List<string>(s.goodForConditions ?? new List<string>()),
            source = s.source, reviewed = s.reviewed, fdaUnii = s.fdaUnii, sources = new List<string>(s.sources ?? new List<string>())
        };

        public static string Key(string s)
        {
            var sb = new System.Text.StringBuilder();
            bool space = false;
            foreach (char raw in (s ?? "").Trim().ToLowerInvariant())
            {
                char c = raw;
                if (char.IsLetterOrDigit(c)) { if (space && sb.Length > 0) sb.Append(' '); sb.Append(c); space = false; }
                else space = true;
            }
            return sb.ToString();
        }

        /// <summary>Edit distance, giving up early once it exceeds `max`.</summary>
        static int Distance(string a, string b, int max)
        {
            int n = a.Length, m = b.Length;
            var prev = new int[m + 1]; var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i; int rowMin = cur[0];
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                    if (cur[j] < rowMin) rowMin = cur[j];
                }
                if (rowMin > max) return max + 1;
                var t = prev; prev = cur; cur = t;
            }
            return prev[m];
        }

        // ---------------------------------------------------------------------------------------
        //  The knowledge base
        // ---------------------------------------------------------------------------------------
        static void Ensure()
        {
            if (all != null) return;
            all = new List<IngredientData>(); byKey = new Dictionary<string, IngredientData>();
            Build();
            foreach (var d in all)
            {
                Index(d.name, d); Index(d.techName, d);
                foreach (var a in d.aliases) Index(a, d);
            }
        }
        static void Index(string key, IngredientData d) { string k = Key(key); if (k.Length > 0 && !byKey.ContainsKey(k)) byKey[k] = d; }

        static List<string> Split(string s)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(s)) foreach (var p in s.Split('|')) if (p.Trim().Length > 0) list.Add(p.Trim());
            return list;
        }

        /// <summary>
        /// One entry. risk: safe | caution | harmful.   Lists are separated with | .
        /// avoidTypes / goodTypes: Oily, Normal, Dry, Mix.
        /// avoidCond / goodCond: acne, eczema, psoriasis, rosacea, seborrheic, melasma, contact.
        /// tags (allergen families): fragrance, paraben, sulfate, formaldehyde, lanolin, essentialoil, nut.
        /// </summary>
        static void A(string name, string tech, string description, string uses, string risk = "safe", string note = "",
            string aliases = "", string tags = "", string avoidTypes = "", string avoidCond = "",
            string goodTypes = "", string goodCond = "", int minAge = 0, bool key = false, bool umbrella = false)
        {
            all.Add(new IngredientData
            {
                name = name, techName = tech, description = description, commonUses = uses, riskLevel = risk, cautionNote = note,
                aliases = Split(aliases), allergenTags = Split(tags), avoidForSkinTypes = Split(avoidTypes), avoidForConditions = Split(avoidCond),
                goodForSkinTypes = Split(goodTypes), goodForConditions = Split(goodCond), minAge = minAge, keyActive = key, umbrella = umbrella
            });
        }

        static void Build()
        {
            // ---- base, humectants, soothers ----
            A("Water", "Aqua", "The base of most products; it carries the other ingredients.", "Almost every lotion, serum and cleanser", aliases: "aqua|eau|purified water|deionized water");
            A("Glycerin", "Glycerin", "A humectant that draws water into the skin and keeps it there.", "Moisturizers, cleansers, serums, lip products", aliases: "glycerol|glycerine", goodTypes: "Dry|Normal|Mix", key: true);
            A("Hyaluronic Acid", "Sodium Hyaluronate", "Holds many times its weight in water, so it plumps and hydrates.", "Serums, moisturizers, lip products", aliases: "sodium hyaluronate|hyaluronate|hydrolyzed hyaluronic acid", goodTypes: "Dry|Normal|Mix|Oily", key: true);
            A("Butylene Glycol", "Butylene Glycol", "A light humectant and solvent that helps other ingredients spread.", "Serums, toners, makeup");
            A("Propylene Glycol", "Propylene Glycol", "A humectant and solvent.", "Creams, serums, deodorants", note: "can sting or irritate eczema-prone and very sensitive skin", avoidCond: "eczema|contact");
            A("Propanediol", "Propanediol", "A plant-derived humectant and solvent.", "Serums, toners, cleansers");
            A("Panthenol", "Panthenol", "Pro-vitamin B5; it soothes and helps the skin hold moisture.", "Moisturizers, serums, hair care", aliases: "provitamin b5|d-panthenol|dexpanthenol", goodCond: "eczema", key: true);
            A("Aloe Vera", "Aloe Barbadensis Leaf Juice", "A soothing, lightly hydrating plant juice.", "Gels, moisturizers, after-sun care", aliases: "aloe barbadensis leaf juice|aloe barbadensis|aloe barbadensis leaf extract|aloe", key: true);
            A("Allantoin", "Allantoin", "A gentle soothing and skin-conditioning ingredient.", "Moisturizers, after-shave, sensitive-skin products");
            A("Urea", "Urea", "A humectant that also softens rough, flaky skin.", "Body lotions, foot creams, dry-skin treatments", note: "can sting on broken skin", goodTypes: "Dry", goodCond: "psoriasis|eczema", key: true);
            A("Betaine", "Betaine", "A mild humectant that helps the skin stay hydrated.", "Cleansers, moisturizers");
            A("Sorbitol", "Sorbitol", "A humectant and texture improver.", "Creams, cleansers, toothpaste");

            // ---- actives ----
            A("Niacinamide", "Niacinamide", "Vitamin B3; it helps with oil control, uneven tone and the skin barrier.", "Serums, moisturizers, toners", aliases: "nicotinamide|vitamin b3", goodTypes: "Oily|Mix|Normal", goodCond: "acne|melasma|rosacea", key: true);
            A("Salicylic Acid", "Salicylic Acid", "A BHA exfoliant that works inside the pore to unclog it.", "Acne treatments, cleansers, toners", note: "can dry out or irritate the skin", aliases: "bha|beta hydroxy acid", avoidTypes: "Dry", avoidCond: "eczema|rosacea", goodTypes: "Oily|Mix", goodCond: "acne|seborrheic", minAge: 12, key: true);
            A("Glycolic Acid", "Glycolic Acid", "An AHA exfoliant that loosens dead surface cells.", "Peels, toners, night creams", note: "can sting and makes the skin more sensitive to the sun", aliases: "aha|alpha hydroxy acid", avoidCond: "eczema|rosacea|contact", goodCond: "melasma|acne", minAge: 12, key: true);
            A("Lactic Acid", "Lactic Acid", "A gentler AHA exfoliant that also hydrates.", "Toners, serums, body lotions", note: "can sting on sensitive skin", avoidCond: "eczema|rosacea", goodCond: "melasma", key: true);
            A("Mandelic Acid", "Mandelic Acid", "A mild AHA exfoliant with a larger molecule, so it acts slowly.", "Serums, peels", goodCond: "acne|melasma", key: true);
            A("Retinol", "Retinol", "A vitamin A derivative used for texture, breakouts and fine lines.", "Night creams, serums", note: "can cause dryness, peeling and sun sensitivity", aliases: "retinyl palmitate|retinal|retinaldehyde|vitamin a|retinyl acetate", avoidCond: "eczema|rosacea|contact", goodCond: "acne|melasma", minAge: 18, key: true);
            A("Benzoyl Peroxide", "Benzoyl Peroxide", "An acne treatment that reduces acne-causing bacteria.", "Acne creams, washes", note: "is drying and can irritate", avoidTypes: "Dry", avoidCond: "eczema|rosacea", goodCond: "acne", minAge: 12, key: true);
            A("Vitamin C", "Ascorbic Acid", "An antioxidant that brightens and helps protect against daily damage.", "Serums, moisturizers", note: "can sting sensitive skin at high strength", aliases: "ascorbic acid|l-ascorbic acid|sodium ascorbyl phosphate|ascorbyl glucoside|magnesium ascorbyl phosphate|ascorbyl palmitate|ethyl ascorbic acid", goodCond: "melasma", key: true);
            A("Vitamin E", "Tocopherol", "An antioxidant that also conditions the skin.", "Moisturizers, oils, lip products", aliases: "tocopherol|tocopheryl acetate");
            A("Ceramides", "Ceramide NP", "Lipids that are a natural part of the skin barrier.", "Moisturizers, barrier creams", aliases: "ceramide np|ceramide ap|ceramide eop|ceramide|ceramide ng|ceramide ns", goodTypes: "Dry|Normal", goodCond: "eczema|psoriasis", key: true);
            A("Squalane", "Squalane", "A light, non-greasy emollient similar to the skin's own oils.", "Oils, moisturizers, serums", goodTypes: "Dry|Normal|Mix|Oily", key: true);
            A("Centella Asiatica", "Centella Asiatica Extract", "Also called cica; a calming plant extract.", "Soothing creams, serums", aliases: "centella asiatica extract|cica|madecassoside|asiaticoside|centella asiatica leaf extract", goodCond: "rosacea|acne|eczema", key: true);
            A("Azelaic Acid", "Azelaic Acid", "Helps with breakouts, redness and dark marks.", "Treatment creams, serums", goodCond: "acne|rosacea|melasma", key: true);
            A("Kojic Acid", "Kojic Acid", "A brightening ingredient used on dark spots.", "Brightening serums, soaps", note: "can irritate and is a known sensitiser for some people", avoidCond: "eczema|contact", goodCond: "melasma", key: true);
            A("Arbutin", "Alpha-Arbutin", "A brightening ingredient used on dark spots.", "Brightening serums and creams", aliases: "alpha-arbutin|alpha arbutin", goodCond: "melasma", key: true);
            A("Tranexamic Acid", "Tranexamic Acid", "Used to fade stubborn dark patches.", "Brightening serums", goodCond: "melasma", key: true);
            A("Hydroquinone", "Hydroquinone", "A strong pigment lightener.", "Dark-spot treatments", risk: "harmful", note: "restricted or prescription-only in many countries and should only be used under medical supervision", key: true);
            A("Zinc Oxide", "Zinc Oxide", "A mineral UV filter that is also soothing.", "Sunscreens, diaper creams, mineral makeup", aliases: "ci 77947", goodCond: "rosacea|eczema", key: true);
            A("Titanium Dioxide", "Titanium Dioxide", "A mineral UV filter and white pigment.", "Sunscreens, foundations, powders", aliases: "ci 77891");
            A("Tea Tree Oil", "Melaleuca Alternifolia Leaf Oil", "An essential oil with antibacterial properties.", "Spot treatments, cleansers", note: "is an essential oil and can cause contact allergy", aliases: "melaleuca alternifolia leaf oil|melaleuca alternifolia", tags: "essentialoil", avoidCond: "eczema|contact|rosacea", goodCond: "acne", key: true);
            A("Witch Hazel", "Hamamelis Virginiana Water", "An astringent plant water that tightens and removes oil.", "Toners, aftershaves", note: "is astringent, often carries alcohol and can be drying", aliases: "hamamelis virginiana water|hamamelis virginiana|hamamelis virginiana leaf extract", avoidTypes: "Dry", avoidCond: "eczema|rosacea");
            A("Caffeine", "Caffeine", "Used to reduce the look of puffiness.", "Eye creams, serums");
            A("Peptides", "Palmitoyl Tripeptide-1", "Short chains of amino acids used in firming products.", "Serums, eye creams", aliases: "palmitoyl tripeptide-1|palmitoyl tetrapeptide-7|acetyl hexapeptide-8|copper tripeptide-1|palmitoyl pentapeptide-4", key: true);
            A("Colloidal Oatmeal", "Avena Sativa Kernel Flour", "Finely milled oats that soothe itchy, dry skin.", "Eczema creams, bath products", aliases: "avena sativa kernel flour|avena sativa kernel extract|oat kernel extract", goodTypes: "Dry", goodCond: "eczema", key: true);

            // ---- emollients, oils, waxes ----
            A("Dimethicone", "Dimethicone", "A silicone that smooths the skin and forms a breathable protective layer.", "Primers, moisturizers, foundations");
            A("Cyclopentasiloxane", "Cyclopentasiloxane", "A light silicone that gives a silky feel and then evaporates.", "Primers, foundations, hair serums", aliases: "cyclomethicone|cyclohexasiloxane");
            A("Petrolatum", "Petrolatum", "A very effective occlusive that seals moisture in.", "Ointments, lip balms, barrier creams", aliases: "petroleum jelly|vaseline|white petrolatum", goodTypes: "Dry", goodCond: "eczema|psoriasis");
            A("Mineral Oil", "Paraffinum Liquidum", "An occlusive oil that locks in moisture.", "Cleansing oils, baby oil, creams", note: "is heavy and can feel greasy", aliases: "paraffinum liquidum|liquid paraffin", avoidTypes: "Oily");
            A("Shea Butter", "Butyrospermum Parkii Butter", "A rich plant butter that softens and protects.", "Body butters, lip balms, creams", note: "comes from the shea nut (reactions are rare)", aliases: "butyrospermum parkii butter|butyrospermum parkii", tags: "nut", goodTypes: "Dry");
            A("Coconut Oil", "Cocos Nucifera Oil", "A rich plant oil.", "Balms, hair oils, body care", note: "tends to clog pores", aliases: "cocos nucifera oil|cocos nucifera", tags: "nut", avoidTypes: "Oily|Mix", avoidCond: "acne");
            A("Sweet Almond Oil", "Prunus Amygdalus Dulcis Oil", "A soft, nourishing nut oil.", "Body oils, creams, lip care", aliases: "prunus amygdalus dulcis oil|almond oil|prunus amygdalus dulcis", tags: "nut", goodTypes: "Dry");
            A("Argan Oil", "Argania Spinosa Kernel Oil", "A nourishing oil pressed from argan kernels.", "Face oils, hair oils", aliases: "argania spinosa kernel oil|argania spinosa", tags: "nut", goodTypes: "Dry|Normal");
            A("Macadamia Oil", "Macadamia Ternifolia Seed Oil", "A rich nut oil.", "Creams, hair care", aliases: "macadamia ternifolia seed oil|macadamia integrifolia seed oil", tags: "nut");
            A("Jojoba Oil", "Simmondsia Chinensis Seed Oil", "A liquid wax very close to the skin's own sebum.", "Face oils, cleansers, lip care", aliases: "simmondsia chinensis seed oil|simmondsia chinensis", goodTypes: "Oily|Mix|Dry|Normal");
            A("Rosehip Oil", "Rosa Canina Fruit Oil", "A light oil rich in fatty acids.", "Face oils, serums", aliases: "rosa canina fruit oil|rosa canina seed oil");
            A("Castor Oil", "Ricinus Communis Seed Oil", "A thick, glossy oil.", "Lipsticks, lip glosses, brow products", aliases: "ricinus communis seed oil|ricinus communis");
            A("Lanolin", "Lanolin", "A rich emollient from sheep's wool.", "Lip balms, nipple creams, ointments", note: "is a known contact allergen for some people", aliases: "lanolin alcohol|wool fat|adeps lanae|wool wax", tags: "lanolin", avoidCond: "eczema|contact");
            A("Beeswax", "Cera Alba", "A natural wax that thickens and protects.", "Lip balms, lipsticks, mascaras", aliases: "cera alba|cera flava");
            A("Candelilla Wax", "Euphorbia Cerifera Wax", "A plant wax that gives structure.", "Lipsticks, balms", aliases: "euphorbia cerifera wax|candelilla cera");
            A("Carnauba Wax", "Copernicia Cerifera Wax", "A hard plant wax that gives structure and shine.", "Lipsticks, mascaras", aliases: "copernicia cerifera wax|copernicia cerifera cera");
            A("Isopropyl Myristate", "Isopropyl Myristate", "A light emollient that helps products sink in.", "Lotions, makeup removers", note: "can clog pores", avoidTypes: "Oily", avoidCond: "acne");
            A("Isopropyl Palmitate", "Isopropyl Palmitate", "A light emollient.", "Lotions, foundations", note: "can clog pores", avoidTypes: "Oily", avoidCond: "acne");
            A("Cetearyl Alcohol", "Cetearyl Alcohol", "A fatty alcohol that thickens creams. Despite the name, it is not drying.", "Creams, conditioners, lotions", aliases: "cetyl alcohol|stearyl alcohol|cetostearyl alcohol|behenyl alcohol");
            A("Caprylic/Capric Triglyceride", "Caprylic/Capric Triglyceride", "A light emollient made from coconut oil and glycerin.", "Moisturizers, serums, makeup", aliases: "caprylic capric triglyceride");
            A("Stearic Acid", "Stearic Acid", "A fatty acid that thickens and stabilises creams.", "Creams, cleansers, soaps");

            // ---- cleansing agents ----
            A("Sodium Lauryl Sulfate", "Sodium Lauryl Sulfate", "SLS: a strong cleansing agent that foams well.", "Shampoos, body washes, toothpaste", note: "can strip the skin and irritate", aliases: "sls|sodium dodecyl sulfate", tags: "sulfate", avoidTypes: "Dry", avoidCond: "eczema|rosacea|contact|psoriasis");
            A("Sodium Laureth Sulfate", "Sodium Laureth Sulfate", "SLES: a cleansing agent, milder than SLS.", "Shampoos, face and body washes", note: "can still be drying", aliases: "sles|sodium lauryl ether sulfate", tags: "sulfate", avoidTypes: "Dry", avoidCond: "eczema");
            A("Ammonium Lauryl Sulfate", "Ammonium Lauryl Sulfate", "A sulfate cleansing agent.", "Shampoos, body washes", note: "can be drying", aliases: "ammonium laureth sulfate", tags: "sulfate", avoidTypes: "Dry", avoidCond: "eczema");
            A("Cocamidopropyl Betaine", "Cocamidopropyl Betaine", "A mild cleansing agent that boosts foam.", "Cleansers, shampoos", note: "is an occasional contact allergen", avoidCond: "contact");
            A("Sodium Cocoyl Isethionate", "Sodium Cocoyl Isethionate", "A gentle cleansing agent.", "Cleansing bars, face washes");
            A("Decyl Glucoside", "Decyl Glucoside", "A gentle, sugar-based cleansing agent.", "Baby washes, face cleansers", aliases: "coco-glucoside|lauryl glucoside|coco glucoside");

            // ---- preservatives ----
            A("Phenoxyethanol", "Phenoxyethanol", "A widely used preservative.", "Almost any water-based product");
            A("Methylparaben", "Methylparaben", "A paraben preservative that stops bacteria and mould.", "Creams, makeup, shampoos", tags: "paraben");
            A("Propylparaben", "Propylparaben", "A paraben preservative.", "Creams, makeup, shampoos", tags: "paraben");
            A("Ethylparaben", "Ethylparaben", "A paraben preservative.", "Creams, makeup", tags: "paraben");
            A("Butylparaben", "Butylparaben", "A paraben preservative.", "Creams, makeup", risk: "caution", note: "is restricted to low concentrations in some regions", tags: "paraben");
            A("Methylisothiazolinone", "Methylisothiazolinone", "A preservative.", "Rinse-off products such as shampoos and washes", risk: "caution", note: "is a frequent cause of contact allergy and is restricted in leave-on products in some regions", aliases: "mi|mit", avoidCond: "eczema|contact|rosacea");
            A("Methylchloroisothiazolinone", "Methylchloroisothiazolinone", "A preservative, usually paired with methylisothiazolinone.", "Rinse-off products", risk: "caution", note: "is a frequent cause of contact allergy", aliases: "mci|cmit|kathon cg", avoidCond: "eczema|contact|rosacea");
            A("DMDM Hydantoin", "DMDM Hydantoin", "A preservative.", "Shampoos, conditioners, lotions", risk: "caution", note: "releases small amounts of formaldehyde", tags: "formaldehyde", avoidCond: "eczema|contact");
            A("Imidazolidinyl Urea", "Imidazolidinyl Urea", "A preservative.", "Creams, makeup", risk: "caution", note: "releases small amounts of formaldehyde", tags: "formaldehyde", avoidCond: "eczema|contact");
            A("Diazolidinyl Urea", "Diazolidinyl Urea", "A preservative.", "Creams, makeup", risk: "caution", note: "releases small amounts of formaldehyde", tags: "formaldehyde", avoidCond: "eczema|contact");
            A("Quaternium-15", "Quaternium-15", "A preservative.", "Shampoos, creams", risk: "caution", note: "releases formaldehyde and is a known sensitiser", aliases: "quaternium 15", tags: "formaldehyde", avoidCond: "eczema|contact");
            A("Bronopol", "2-Bromo-2-Nitropropane-1,3-Diol", "A preservative.", "Shampoos, creams", risk: "caution", note: "can release formaldehyde", aliases: "2-bromo-2-nitropropane-1,3-diol", tags: "formaldehyde", avoidCond: "eczema|contact");
            A("Formaldehyde", "Formaldehyde", "A preservative and hardener.", "Some nail hardeners and hair-smoothing treatments", risk: "harmful", note: "is a known sensitiser and is banned or tightly restricted in cosmetics", aliases: "formalin|methanal|methylene glycol", tags: "formaldehyde");
            A("Benzyl Alcohol", "Benzyl Alcohol", "A preservative that is also a fragrance component.", "Creams, makeup, hair care", note: "is a listed fragrance allergen", tags: "fragrance", avoidCond: "contact");
            A("Sodium Benzoate", "Sodium Benzoate", "A mild preservative.", "Cleansers, toners, creams");
            A("Potassium Sorbate", "Potassium Sorbate", "A mild preservative.", "Creams, cleansers");
            A("Ethylhexylglycerin", "Ethylhexylglycerin", "A skin conditioner that supports preservatives.", "Creams, deodorants");
            A("Triclosan", "Triclosan", "An antibacterial agent.", "Some soaps and deodorants", risk: "caution", note: "is restricted in several countries");
            A("Disodium EDTA", "Disodium EDTA", "Keeps the formula stable.", "Almost any water-based product", aliases: "tetrasodium edta|edta");

            // ---- fragrance ----
            A("Fragrance", "Parfum", "A scent blend. The label does not have to list what is in it.", "Perfumed skincare, makeup and hair care", risk: "caution", note: "is an umbrella term that can cover dozens of undisclosed scent chemicals and is one of the most common causes of cosmetic allergy", aliases: "parfum|perfume|aroma|flavor|flavour", tags: "fragrance", avoidCond: "eczema|rosacea|contact|psoriasis", umbrella: true);
            A("Limonene", "Limonene", "A citrus-scented fragrance component.", "Perfumed products", note: "is a fragrance allergen, especially once it has been exposed to air", aliases: "d-limonene", tags: "fragrance", avoidCond: "contact|eczema");
            A("Linalool", "Linalool", "A floral-scented fragrance component.", "Perfumed products", note: "is a fragrance allergen, especially once it has been exposed to air", tags: "fragrance", avoidCond: "contact|eczema");
            A("Citronellol", "Citronellol", "A rose-scented fragrance component.", "Perfumed products", note: "is a listed fragrance allergen", tags: "fragrance", avoidCond: "contact");
            A("Geraniol", "Geraniol", "A rose-scented fragrance component.", "Perfumed products", note: "is a listed fragrance allergen", tags: "fragrance", avoidCond: "contact");
            A("Citral", "Citral", "A lemon-scented fragrance component.", "Perfumed products", note: "is a listed fragrance allergen", tags: "fragrance", avoidCond: "contact");
            A("Eugenol", "Eugenol", "A clove-scented fragrance component.", "Perfumed products", note: "is a listed fragrance allergen", tags: "fragrance", avoidCond: "contact");
            A("Coumarin", "Coumarin", "A sweet, hay-scented fragrance component.", "Perfumed products", note: "is a listed fragrance allergen", tags: "fragrance", avoidCond: "contact");
            A("Lavender Oil", "Lavandula Angustifolia Oil", "An essential oil used for scent.", "Perfumed skincare, bath products", note: "is an essential oil and can cause contact allergy", aliases: "lavandula angustifolia oil|lavandula angustifolia flower oil|lavandula angustifolia", tags: "essentialoil|fragrance", avoidCond: "eczema|contact|rosacea");
            A("Peppermint Oil", "Mentha Piperita Oil", "A cooling essential oil.", "Lip balms, foot care", note: "is an essential oil and can irritate", aliases: "mentha piperita oil|mentha piperita leaf oil", tags: "essentialoil", avoidCond: "rosacea|eczema|contact");
            A("Eucalyptus Oil", "Eucalyptus Globulus Leaf Oil", "A strongly scented essential oil.", "Balms, bath products", note: "is an essential oil and can irritate", aliases: "eucalyptus globulus leaf oil", tags: "essentialoil", avoidCond: "rosacea|eczema|contact");
            A("Orange Peel Oil", "Citrus Aurantium Dulcis Peel Oil", "A citrus essential oil used for scent.", "Perfumed skincare", note: "is an essential oil and can cause contact allergy", aliases: "citrus aurantium dulcis peel oil|citrus sinensis peel oil", tags: "essentialoil|fragrance", avoidCond: "eczema|contact");
            A("Lemon Peel Oil", "Citrus Limon Peel Oil", "A citrus essential oil used for scent.", "Perfumed skincare", note: "is an essential oil, can irritate and can make skin more sensitive to the sun", aliases: "citrus limon peel oil|citrus limon fruit oil", tags: "essentialoil|fragrance", avoidCond: "eczema|contact");
            A("Rose Oil", "Rosa Damascena Flower Oil", "A floral essential oil used for scent.", "Perfumed skincare", note: "is an essential oil and can cause contact allergy", aliases: "rosa damascena flower oil", tags: "essentialoil|fragrance", avoidCond: "contact");
            A("Menthol", "Menthol", "Gives a cooling sensation.", "Lip plumpers, foot care, after-shave", note: "can irritate", avoidCond: "rosacea|eczema");
            A("Camphor", "Camphor", "Gives a cooling, tingling sensation.", "Balms, acne products", note: "can irritate", avoidCond: "rosacea|eczema");

            // ---- alcohols ----
            A("Alcohol Denat.", "Alcohol Denat.", "A quick-drying alcohol that thins a formula and helps it dry fast.", "Toners, setting sprays, sunscreens, perfumes", note: "can dry out and irritate the skin", aliases: "alcohol denat|denatured alcohol|sd alcohol|sd alcohol 40|sd alcohol 40-b|ethanol|alcohol", avoidTypes: "Dry", avoidCond: "eczema|rosacea|contact|psoriasis");
            A("Isopropyl Alcohol", "Isopropyl Alcohol", "A quick-drying alcohol.", "Toners, nail products", note: "can dry out and irritate the skin", aliases: "isopropanol", avoidTypes: "Dry", avoidCond: "eczema|rosacea");

            // ---- chemical UV filters ----
            A("Oxybenzone", "Benzophenone-3", "A chemical UV filter.", "Sunscreens", risk: "caution", note: "can cause contact and sun-triggered allergy", aliases: "benzophenone-3|benzophenone 3", avoidCond: "contact|eczema");
            A("Octinoxate", "Ethylhexyl Methoxycinnamate", "A chemical UV filter.", "Sunscreens, foundations with SPF", aliases: "ethylhexyl methoxycinnamate|octyl methoxycinnamate");
            A("Avobenzone", "Butyl Methoxydibenzoylmethane", "A chemical UVA filter.", "Sunscreens", aliases: "butyl methoxydibenzoylmethane");
            A("Octocrylene", "Octocrylene", "A chemical UV filter.", "Sunscreens", note: "is an occasional contact allergen", avoidCond: "contact");
            A("Homosalate", "Homosalate", "A chemical UVB filter.", "Sunscreens");

            // ---- colour and makeup bases ----
            A("Mica", "Mica", "A mineral that gives shimmer.", "Eyeshadows, highlighters, lip products", aliases: "ci 77019");
            A("Iron Oxides", "Iron Oxides", "Mineral pigments in red, yellow and black.", "Foundations, eyeshadows, lipsticks", aliases: "ci 77491|ci 77492|ci 77499");
            A("Carmine", "Carmine", "A red pigment made from insects.", "Lipsticks, blushes, eyeshadows", note: "is a known allergen for some people", aliases: "ci 75470|cochineal|carminic acid", avoidCond: "contact");
            A("Talc", "Talc", "A soft mineral powder that absorbs oil.", "Powders, blushes, eyeshadows");
            A("Bismuth Oxychloride", "Bismuth Oxychloride", "Gives a pearly finish.", "Mineral foundations, powders", note: "can cause itching or irritation", aliases: "ci 77163", avoidCond: "rosacea|acne|eczema");
            A("Silica", "Silica", "A powder that absorbs oil and blurs shine.", "Primers, powders, foundations", goodTypes: "Oily|Mix");
            A("Kaolin", "Kaolin", "A clay that absorbs oil.", "Clay masks, powders", note: "can be drying", avoidTypes: "Dry", goodTypes: "Oily|Mix");
            A("Polybutene", "Polybutene", "Gives gloss and stickiness.", "Lip glosses, lipsticks");
            A("Lead Acetate", "Lead Acetate", "A colour additive formerly used in hair dyes.", "Old-style progressive hair dyes", risk: "harmful", note: "contains lead and is banned in cosmetics in many countries", aliases: "lead");
            A("Mercury", "Mercury", "A skin-lightening agent found in some unregulated creams.", "Unregulated skin-lightening creams", risk: "harmful", note: "is toxic; the US FDA allows mercury compounds only as a preservative in eye-area products, at no more than 65 parts per million (21 CFR 700.13)", aliases: "mercurous chloride|calomel|mercuric iodide|ammoniated mercury|mercuric|mercurio|thimerosal|phenylmercuric acetate|phenylmercuric nitrate");

            // ---- prohibited or restricted in cosmetics by the US FDA (21 CFR Part 700 and 250.250) ----
            // Source: fda.gov, "Prohibited & Restricted Ingredients in Cosmetics". Works offline, like the rest of this list.
            A("Bithionol", "Bithionol", "An antibacterial agent once used in soaps and creams.", "Not allowed in cosmetics", risk: "harmful", note: "is prohibited in cosmetics by the US FDA because it may cause photocontact sensitization (21 CFR 700.11)");
            A("Chloroform", "Chloroform", "A solvent.", "Not allowed in cosmetics", risk: "harmful", note: "is prohibited as a cosmetic ingredient by the US FDA because it causes cancer in animals (21 CFR 700.18)", aliases: "trichloromethane");
            A("Methylene Chloride", "Methylene Chloride", "A solvent once used in aerosol products such as hair sprays.", "Not allowed in cosmetics", risk: "harmful", note: "is prohibited in cosmetics by the US FDA because it causes cancer in animals (21 CFR 700.19)", aliases: "dichloromethane");
            A("Vinyl Chloride", "Vinyl Chloride", "A gas once used as an aerosol propellant.", "Not allowed in aerosol cosmetics", risk: "harmful", note: "is prohibited in aerosol cosmetics by the US FDA because it causes cancer and other health problems (21 CFR 700.14)");
            A("Halogenated Salicylanilides", "Tribromsalan", "A group of antibacterial agents once used in soaps.", "Not allowed in cosmetics", risk: "harmful", note: "is prohibited in cosmetics by the US FDA because it may cause serious skin disorders (21 CFR 700.15)", aliases: "tribromsalan|dibromsalan|metabromsalan|tetrachlorosalicylanilide|halogenated salicylanilide");
            A("Hexachlorophene", "Hexachlorophene", "An antibacterial preservative.", "Rarely used; restricted", risk: "caution", note: "is restricted by the US FDA to at most 0.1 percent, only when no other preservative works, and never in products for the lips or other mucous membranes (21 CFR 250.250)");
            A("Zirconium (in aerosols)", "Zirconium", "Zirconium-containing complexes were used in aerosol antiperspirants.", "Not allowed in aerosol cosmetics", risk: "caution", note: "is prohibited in aerosol cosmetics by the US FDA because of its effect on the lungs and the skin granulomas it can cause (21 CFR 700.16)", aliases: "zirconium complexes|sodium zirconium lactate");
            A("Chlorofluorocarbon Propellants", "Chlorofluorocarbons", "Gases once used to push product out of spray cans.", "Not allowed in aerosol cosmetics", risk: "caution", note: "is prohibited in cosmetic aerosols by the US FDA (21 CFR 700.23)", aliases: "cfc|chlorofluorocarbon|trichlorofluoromethane|dichlorodifluoromethane");
        }
    }
}
