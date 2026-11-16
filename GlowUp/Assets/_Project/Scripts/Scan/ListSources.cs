using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// WHERE AN INGREDIENT LIST MAY COME FROM, and how much each place is trusted. Best first:
    ///   1. the user's own pack, read by the camera;
    ///   2. the product as the GlowUp inventory stores it;
    ///   3. the brand's official website or store page;
    ///   4. a large retailer's page that copies the pack's list;
    ///   5. Open Beauty Facts, as a second opinion;
    ///   6. any other page that can be shown.
    /// Ingredient DATABASES (CosIng, INCI Decoder, EWG, CosDNA ...) explain what an ingredient is; they are not taken as
    /// the source of one product's list. A list is only accepted when it comes with a page the app can show: an
    /// AI answer that names no usable page is "Ingredient list not found", never a list.
    ///
    /// Two lists of the same product can also be compared here ("Matched 2 sources", or what differs).
    /// Pure logic, tested in Editor/Tests/ListSourceTests.cs. A source is written "page title | https://address".
    /// </summary>
    public static class ListSources
    {
        public enum Kind { Pack, Inventory, BrandSite, Retailer, OpenBeautyFacts, Other, Database, Unusable }

        /// <summary>Sites that explain ingredients. They may back up what an ingredient IS, not what one product contains.</summary>
        public static readonly string[] Databases =
        {
            "incidecoder.com", "ewg.org", "cosdna.com", "ec.europa.eu", "cosmeticsinfo.org", "skincarisma.com", "skinsort.com", "cosmily.com",
            "inci.guide", "paulaschoice.com", "wikipedia.org", "pubchem.ncbi.nlm.nih.gov", "specialchem.com", "ulprospector.com", "lesielle.com"
        };

        /// <summary>Large shops whose product pages copy the list printed on the pack.</summary>
        public static readonly string[] Retailers =
        {
            "watsons", "lazada", "shopee", "sephora", "ulta.com", "boots.com", "lookfantastic", "beautymnl", "zalora", "amazon.", "walmart.com", "target.com",
            "superdrug.com", "cultbeauty", "nykaa", "yesstyle", "stylevana", "sasa.com", "guardian.com", "oliveyoung", "feelunique", "beautybay", "notino", "douglas.",
            "priceline.com.au", "chemistwarehouse", "mercurydrug", "southstardrug", "landers.ph", "sm-markets", "smmarkets", "calyxta", "glamourbox"
        };

        /// <summary>Addresses that only pass the reader on (a search engine's redirect): the page's real site is in its title.</summary>
        static readonly string[] Redirects = { "vertexaisearch.cloud.google.com", "google.com/url", "bing.com/ck" };

        /// <summary>The web address in a source ("" when it has none that can be opened).</summary>
        public static string AddressOf(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            int at = source.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
            int secure = source.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
            if (secure >= 0 && (at < 0 || secure < at)) at = secure;
            if (at < 0) return "";
            int end = at;
            while (end < source.Length && !char.IsWhiteSpace(source[end])) end++;
            return source.Substring(at, end - at).TrimEnd('.', ',', ';', ')', '"');
        }

        /// <summary>The page's title in a source ("" when the source is only an address).</summary>
        public static string TitleOf(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            int cut = source.LastIndexOf(" | ", StringComparison.Ordinal);
            string title = cut > 0 ? source.Substring(0, cut) : AddressOf(source).Length == 0 ? source : "";
            return title.Trim();
        }

        /// <summary>"www.maybelline.com.ph" out of "https://www.maybelline.com.ph/face/..." (lower case, "" when there is none).</summary>
        public static string HostOf(string address)
        {
            if (string.IsNullOrEmpty(address)) return "";
            int start = address.IndexOf("://", StringComparison.Ordinal);
            start = start < 0 ? 0 : start + 3;
            int end = address.IndexOfAny(new[] { '/', '?', '#' }, start);
            return (end < 0 ? address.Substring(start) : address.Substring(start, end - start)).ToLowerInvariant();
        }

        /// <summary>What kind of place a source is. `brandName` = the brand of the product the list is for (may be empty).</summary>
        public static Kind KindOf(string source, string brandName)
        {
            string address = AddressOf(source);
            if (address.Length == 0) return Kind.Unusable;                 // nothing the app could show
            string host = HostOf(address);
            int afterScheme = address.IndexOf("://", StringComparison.Ordinal);
            string where = (afterScheme < 0 ? address : address.Substring(afterScheme + 3)).ToLowerInvariant();      // the site and the page, without "https://"
            // a search engine's redirect hides the real site: its title says which site it is ("absidy.com")
            foreach (string redirect in Redirects)
                if (where.Contains(redirect))
                {
                    string title = TitleOf(source).ToLowerInvariant();
                    if (title.Length == 0) return Kind.Other;
                    host = title; where = title;
                    break;
                }
            if (where.Contains("openbeautyfacts.org")) return Kind.OpenBeautyFacts;
            foreach (string site in Databases) if (where.Contains(site)) return Kind.Database;
            // the brand's own site: its name stands in the site's name ("maybelline.com.ph", "shop.absidy.com")
            string brand = TextKey.Compact(brandName);
            string compactHost = TextKey.Compact(host);
            bool ownSite = brand.Length >= 4 && compactHost.Contains(brand);
            // a brand of three letters ("NYX") must stand at the start of the site's name: three letters turn up anywhere
            if (!ownSite && brand.Length == 3) ownSite = (host.StartsWith("www.", StringComparison.Ordinal) ? host.Substring(4) : host).StartsWith(brand, StringComparison.Ordinal);
            if (!ownSite && brand.Length >= 4)
            {
                // "Maybelline New York" -> maybelline.com: the first word of the brand is enough when it is long
                string[] words = TextKey.Words(brandName);
                ownSite = words.Length > 1 && words[0].Length >= 5 && compactHost.Contains(words[0]);
            }
            foreach (string shop in Retailers) if (where.Contains(shop)) return ownSite && !host.Contains(shop.Trim('.')) ? Kind.BrandSite : Kind.Retailer;
            return ownSite ? Kind.BrandSite : Kind.Other;
        }

        /// <summary>The place of a kind in the order of trust: 0 = best.</summary>
        public static int Rank(Kind kind)
        {
            switch (kind)
            {
                case Kind.Pack: return 0;
                case Kind.Inventory: return 1;
                case Kind.BrandSite: return 2;
                case Kind.Retailer: return 3;
                case Kind.OpenBeautyFacts: return 4;
                case Kind.Other: return 5;
                case Kind.Database: return 8;
                default: return 9;
            }
        }

        /// <summary>True when a place of this kind may be the source of one product's ingredient list.</summary>
        public static bool CanCarryAList(Kind kind) => kind != Kind.Database && kind != Kind.Unusable;

        /// <summary>
        /// The sources a product's list may rest on, best first: pages that can be shown and are not ingredient databases,
        /// each address once. An empty answer means: no usable source, so the list is NOT accepted.
        /// </summary>
        public static List<string> Usable(IEnumerable<string> sources, string brandName)
        {
            var kept = new List<KeyValuePair<int, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int order = 0;
            if (sources != null)
                foreach (string source in sources)
                {
                    Kind kind = KindOf(source, brandName);
                    if (!CanCarryAList(kind) || !seen.Add(AddressOf(source))) continue;
                    kept.Add(new KeyValuePair<int, string>(Rank(kind) * 1000 + order++, source.Trim()));
                }
            kept.Sort((x, y) => x.Key.CompareTo(y.Key));
            var result = new List<string>();
            foreach (KeyValuePair<int, string> entry in kept) result.Add(entry.Value);
            return result;
        }

        /// <summary>In words, for the result screen: "the brand's website", "a retailer's page", "Open Beauty Facts" ...</summary>
        public static string Describe(Kind kind)
        {
            switch (kind)
            {
                case Kind.Pack: return "your pack";
                case Kind.Inventory: return "the GlowUp product list";
                case Kind.BrandSite: return "the brand's website";
                case Kind.Retailer: return "a retailer's page";
                case Kind.OpenBeautyFacts: return "Open Beauty Facts";
                case Kind.Database: return "an ingredient database";
                default: return "another page";
            }
        }

        // ---------------------------------------------------------------------------------------
        //  WHAT KIND OF PAGE a source is, in words a dermatologist or pharmacist can check
        // ---------------------------------------------------------------------------------------
        // A label shown next to every source button. SHARED WORD FOR WORD with the admin website (_Shared/glowupRules.js,
        // sourceKind) and the Assist with AI server (api/assist-ingredient.js): all three are tested against the
        // "sourcekind" and "sourcelabel" lines of Editor/Tests/SharedRuleCases.txt. It only LABELS a page; which pages may
        // carry a product's list is still decided by KindOf / Usable above.

        public const string KindRegulator = "regulator", KindMedical = "medical", KindMaker = "maker", KindReference = "reference",
                            KindPublic = "public", KindRetailer = "retailer", KindUnverified = "other";

        /// <summary>Regulators and governments (any .gov or .gov.xx site counts too).</summary>
        public static readonly string[] Regulators =
        {
            "europa.eu", "fda.gov", "fda.gov.ph", "nih.gov", "who.int", "aseancosmetics.org", "asean.org", "health.gov.au", "tga.gov.au", "canada.ca", "gov.uk", "echa.europa.eu"
        };

        /// <summary>Medical and scientific bodies and publishers (any .edu or .edu.xx site counts too).</summary>
        public static readonly string[] MedicalBodies =
        {
            "cir-safety.org", "cir-reports.cir-safety.org", "aad.org", "dermnetnz.org", "contactderm.org", "sciencedirect.com", "wiley.com", "springer.com",
            "nature.com", "jamanetwork.com", "bmj.com", "mayoclinic.org", "clevelandclinic.org"
        };

        /// <summary>Ingredient databases and consumer references: they explain an ingredient, they are not peer-reviewed.</summary>
        public static readonly string[] References =
        {
            "personalcarecouncil.org", "incibeauty.com", "incidecoder.com", "paulaschoice.com", "ewg.org", "cosmeticsinfo.org", "cosmileeurope.eu"
        };

        /// <summary>Public lists that anybody can edit.</summary>
        public static readonly string[] PublicLists = { "openbeautyfacts.org" };

        /// <summary>
        /// Large shops for the LABEL, by their whole web address (matched like the lists above, never as a piece of a name, so
        /// "watsons-deals.xyz" is not Watsons). Retailers above stays as it is for KindOf.
        /// </summary>
        public static readonly string[] RetailerSites =
        {
            "watsons.com.ph", "watsons.com.my", "watsons.com.sg", "watsons.co.th", "watsons.com.hk", "watsons.com.tw", "watsons.co.id", "watsons.vn",
            "lazada.com.ph", "lazada.com.my", "lazada.sg", "lazada.co.th", "lazada.co.id", "lazada.vn",
            "shopee.ph", "shopee.com.my", "shopee.sg", "shopee.co.th", "shopee.co.id", "shopee.vn", "shopee.tw",
            "sephora.com", "sephora.ph", "sephora.sg", "sephora.my", "sephora.co.uk", "sephora.com.au", "sephora.fr",
            "ulta.com", "boots.com", "lookfantastic.com", "beautymnl.com", "zalora.com.ph", "zalora.com.my", "zalora.sg",
            "amazon.com", "amazon.co.uk", "amazon.de", "amazon.co.jp", "amazon.com.au", "amazon.ca", "walmart.com", "target.com",
            "superdrug.com", "cultbeauty.com", "cultbeauty.co.uk", "nykaa.com", "yesstyle.com", "stylevana.com", "sasa.com", "sasa.com.hk",
            "guardian.com.sg", "guardian.com.my", "oliveyoung.com", "oliveyoung.co.kr", "feelunique.com", "beautybay.com", "notino.com", "notino.co.uk",
            "douglas.de", "priceline.com.au", "chemistwarehouse.com.au", "mercurydrug.com", "southstardrug.com.ph", "landers.ph", "smmarkets.ph"
        };

        // The MAKER's own site is never guessed from a name (anybody can register "brandcosmetics.com"): it is the brand's
        // `website` (BrandData.website), which only an admin sets on the website after checking that the brand owns it.

        static readonly Regex GovSite = new Regex(@"\.gov(\.[a-z]{2})?$"), EduSite = new Regex(@"\.edu(\.[a-z]{2})?$");
        // only a PLAIN address is labelled: http(s), a host of a-z 0-9 . - only (no user name, no other letters), an optional
        // port, and printable ASCII after it. Anything else (a backslash, "user@", look-alike letters) is an Other website.
        static readonly Regex StrictAddress = new Regex("^https?://([a-z0-9.-]+)(?::[0-9]{1,5})?([/?#][!-~]*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex SiteNameOnly = new Regex("^[a-z0-9.-]+$");
        const char Backslash = (char)92;

        /// <summary>
        /// The kind of page of a source line ("page title | https://address", or an address): regulator, medical, maker,
        /// reference, public, retailer or other. `confirmedWebsite` = the brand's confirmed website (BrandData.website); empty =
        /// no page is the maker's.
        /// </summary>
        public static string SourceKind(string source, string confirmedWebsite)
        {
            string host = LabelHost(source);
            if (host.Length == 0) return KindUnverified;
            if (OnSite(host, Regulators) || GovSite.IsMatch(host)) return KindRegulator;
            if (OnSite(host, MedicalBodies) || EduSite.IsMatch(host)) return KindMedical;
            string maker = MakerHost(confirmedWebsite);
            if (maker.Length > 0 && OnSite(host, new[] { maker })) return KindMaker;
            if (OnSite(host, References)) return KindReference;
            if (OnSite(host, PublicLists)) return KindPublic;
            if (OnSite(host, RetailerSites)) return KindRetailer;
            return KindUnverified;
        }

        static string WithoutWww(string host) => host.StartsWith("www.", StringComparison.Ordinal) ? host.Substring(4) : host;

        /// <summary>
        /// The site a source line points to, lower case, without "www." ("" for an address that is not plain: see StrictAddress).
        /// A search engine's pass-through address (known by its OWN site, never by words elsewhere in the address) hides the
        /// real site: then the page title stands in for it, when that title is a site name and nothing else.
        /// </summary>
        public static string LabelHost(string source)
        {
            string host = PlainHost(AddressOf(source), out bool redirect);
            if (host.Length == 0) return "";
            if (redirect)
            {
                host = TitleOf(source).ToLowerInvariant().Trim().TrimEnd('.');
                if (!SiteNameOnly.IsMatch(host)) return "";
            }
            return WithoutWww(host);
        }

        /// <summary>
        /// The site of a brand's CONFIRMED website ("maybelline.com.ph" for "https://www.maybelline.com.ph/en"); "" when there is
        /// none, or it is not a plain address, or it is a search engine's pass-through address.
        /// </summary>
        public static string MakerHost(string confirmedWebsite)
        {
            string host = PlainHost(AddressOf(confirmedWebsite), out bool redirect);
            return host.Length == 0 || redirect ? "" : WithoutWww(host);
        }

        /// <summary>The lower-case host of a plain address ("" when it is not plain), and whether it is a search engine's pass-through address.</summary>
        static string PlainHost(string address, out bool redirect)
        {
            redirect = false;
            Match m = StrictAddress.Match(address ?? "");
            if (!m.Success || address.IndexOf(Backslash) >= 0) return "";
            string host = m.Groups[1].Value.ToLowerInvariant().TrimEnd('.');
            string path = m.Groups[2].Value;
            int cut = path.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) path = path.Substring(0, cut);
            string site = WithoutWww(host);
            redirect = site == "vertexaisearch.cloud.google.com" || (site == "google.com" && path.StartsWith("/url", StringComparison.Ordinal))
                       || (site == "bing.com" && path.StartsWith("/ck", StringComparison.Ordinal));
            return host;
        }

        static bool OnSite(string host, string[] sites)
        {
            foreach (string site in sites) if (host == site || host.EndsWith("." + site, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>The words shown for a kind of page (the same words as on the admin website).</summary>
        public static string LabelOf(string kind)
        {
            switch (kind)
            {
                case KindRegulator: return "Regulator / government";
                case KindMedical: return "Medical / scientific body";
                case KindMaker: return "Manufacturer's own site";
                case KindReference: return "Ingredient database";
                case KindPublic: return "Public list (anyone can edit)";
                case KindRetailer: return "Retailer listing";
                default: return "Other website";
            }
        }

        /// <summary>One sentence of advice for a careful reader, per kind of page (the same words as on the admin website).</summary>
        public static string AdviceOf(string kind)
        {
            switch (kind)
            {
                case KindRegulator: return "An official body. The strongest kind of source here.";
                case KindMedical: return "A medical or scientific publisher. A dermatologist or pharmacist can check the cited study or review.";
                case KindMaker: return "The brand's own website, confirmed by a GlowUp admin. Its list should match the pack; marketing claims are not evidence.";
                case KindReference: return "Explains what an ingredient is. Not peer-reviewed, and never proof of what one product contains.";
                case KindPublic: return "A public list anybody can edit. Use it as a second opinion only.";
                case KindRetailer: return "A shop's copy of the pack's list. It can be out of date: compare it with your pack.";
                default: return "Not recognised. Do not rely on it without checking elsewhere.";
            }
        }

        /// <summary>The line under every list of source links, for people who are careful about links (the same words as on the admin website).</summary>
        public const string LinkGuide = "Links open another website and GlowUp does not control it. Check the address in the browser bar, never enter personal details, and let a dermatologist or pharmacist confirm any claim you rely on.";

        // ---------------------------------------------------------------------------------------
        //  Two lists of the same product
        // ---------------------------------------------------------------------------------------
        /// <summary>What two ingredient lists of one product have in common.</summary>
        public class Comparison
        {
            public int shared, firstCount, secondCount;
            /// <summary>Names only the first / only the second list has (a few of them, for showing).</summary>
            public List<string> onlyInFirst = new List<string>(), onlyInSecond = new List<string>();
            public int onlyInFirstCount, onlyInSecondCount;
            /// <summary>The share of the longer list that both have: 1 = the same ingredients.</summary>
            public float Agreement => Math.Max(firstCount, secondCount) == 0 ? 0f : shared / (float)Math.Max(firstCount, secondCount);
            /// <summary>True when the lists say the same (the camera may misread a name or two: see AgreeFrom).</summary>
            public bool Agree => firstCount >= MinimumToCompare && secondCount >= MinimumToCompare && Agreement >= AgreeFrom;
            /// <summary>True when both lists are long enough to say anything.</summary>
            public bool Comparable => firstCount >= MinimumToCompare && secondCount >= MinimumToCompare;
        }

        /// <summary>Two lists agree when at least this share of the longer one is in both.</summary>
        public static float AgreeFrom = 0.85f;
        /// <summary>A list shorter than this is not compared (three names prove nothing).</summary>
        public static int MinimumToCompare = 4;
        /// <summary>How many differing names are kept for showing.</summary>
        public static int NamesShown = 5;

        /// <summary>
        /// Compares two lists by WHAT the ingredients are, not how they are written: "Aqua" and "Water" are the same, and
        /// so are capitals, accents and the order. Names behind "may contain" are left out by the caller (they depend
        /// on the shade).
        /// </summary>
        public static Comparison Compare(IEnumerable<string> first, IEnumerable<string> second)
        {
            var result = new Comparison();
            var a = Keys(first, out List<string> namesA); var b = Keys(second, out List<string> namesB);
            result.firstCount = a.Count; result.secondCount = b.Count;
            var setB = new HashSet<string>(b); var setA = new HashSet<string>(a);
            for (int i = 0; i < a.Count; i++)
            {
                if (setB.Contains(a[i])) result.shared++;
                else { result.onlyInFirstCount++; if (result.onlyInFirst.Count < NamesShown) result.onlyInFirst.Add(namesA[i]); }
            }
            for (int i = 0; i < b.Count; i++)
                if (!setA.Contains(b[i])) { result.onlyInSecondCount++; if (result.onlyInSecond.Count < NamesShown) result.onlyInSecond.Add(namesB[i]); }
            return result;
        }

        /// <summary>
        /// True for a name that depends on the SHADE, not on the product: a colour index number ("CI 77491"). The shades
        /// of one product share a base formula and differ in these, so they are left out when two lists are compared.
        /// </summary>
        public static bool DependsOnShade(string name)
        {
            string key = TextKey.Compact(name);
            if (key.StartsWith("ci", StringComparison.Ordinal)) key = key.Substring(2);
            if (key.Length != 5) return false;
            foreach (char c in key) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>
        /// The base formula of a list: its names without those behind "may contain" / "+/-" and without colour index
        /// numbers. That is the part two sources of the same product must agree on.
        /// </summary>
        public static List<string> BaseList(IEnumerable<string> names, IEnumerable<string> mayContain = null)
        {
            var apart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mayContain != null) foreach (string name in mayContain) if (!string.IsNullOrWhiteSpace(name)) apart.Add(name.Trim());
            var kept = new List<string>();
            if (names != null)
                foreach (string name in names)
                    if (!string.IsNullOrWhiteSpace(name) && !apart.Contains(name.Trim()) && !DependsOnShade(name)) kept.Add(name.Trim());
            return kept;
        }

        /// <summary>Another place that has a list of the same product.</summary>
        public class Other
        {
            public Kind kind = Kind.Other;
            /// <summary>"name | address" ("" for the GlowUp product list, which has no page).</summary>
            public string source = "";
            /// <summary>When it was last edited (Unix seconds; 0 = not known).</summary>
            public long dateUnix;
            /// <summary>Its base formula (see BaseList).</summary>
            public List<string> names = new List<string>();
        }

        /// <summary>What checking a list against the other places gave.</summary>
        public class Check
        {
            /// <summary>"matched" (another place has the same list), "differs", or "" (nothing to compare with).</summary>
            public string outcome = "";
            public Other with;
            public Comparison comparison;
        }

        public const string Matched = "matched", Differs = "differs";

        /// <summary>
        /// Checks the list that is shown against the other places that have one. A place that agrees wins ("Matched 2
        /// sources"); when none agrees, the most trusted place that can be compared is named with what differs. The list
        /// that is shown stays the shown one either way: it came from the more trusted place.
        /// </summary>
        public static Check Decide(IList<string> shownBase, IList<Other> others)
        {
            var check = new Check();
            if (shownBase == null || others == null) return check;
            Other agreeing = null, differing = null; Comparison agreed = null, differed = null;
            foreach (Other other in others)
            {
                if (other == null) continue;
                Comparison c = Compare(shownBase, other.names);
                if (!c.Comparable) continue;
                if (c.Agree) { if (agreeing == null || Rank(other.kind) < Rank(agreeing.kind)) { agreeing = other; agreed = c; } }
                else if (differing == null || Rank(other.kind) < Rank(differing.kind)) { differing = other; differed = c; }
            }
            if (agreeing != null) { check.outcome = Matched; check.with = agreeing; check.comparison = agreed; }
            else if (differing != null) { check.outcome = Differs; check.with = differing; check.comparison = differed; }
            return check;
        }

        /// <summary>One key per different ingredient (the app's own name for it when it knows the ingredient), in the list's order.</summary>
        static List<string> Keys(IEnumerable<string> names, out List<string> shown)
        {
            var keys = new List<string>(); shown = new List<string>();
            var once = new HashSet<string>();
            if (names == null) return keys;
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                // the app's own entry for it, when the name (or a close spelling of it) is known: "Aqua" and "Water" are one
                // ingredient. Only its family being known ("... Extract") does not make two names the same.
                IngredientKnowledge.Match known = IngredientKnowledge.Find(name);
                bool sure = known.Found && (known.kind == IngredientKnowledge.MatchKind.Exact || known.kind == IngredientKnowledge.MatchKind.Close);
                string key = sure ? "k:" + TextKey.Normalize(string.IsNullOrWhiteSpace(known.data.techName) ? known.data.name : known.data.techName)
                                  : "n:" + TextKey.Squash(TextKey.Normalize(name));
                if (key.Length <= 2 || !once.Add(key)) continue;
                keys.Add(key); shown.Add(name.Trim());
            }
            return keys;
        }
    }
}
