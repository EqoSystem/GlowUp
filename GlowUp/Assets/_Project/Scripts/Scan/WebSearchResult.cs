using System;
using System.Collections.Generic;

namespace GlowUp.Scan
{
    /// <summary>
    /// WHAT THE WEB SEARCH OF A PRODUCT PHOTO FOUND (11 November 2026). Filled from the server's answer (api/scan-snapshot.js,
    /// "details") and shown on the Web Search Result screen next to the usual safety analysis. Every link is https and comes from a
    /// page the server opened itself; nothing here is checked by GlowUp yet (the entry waits for an admin in Pending Snapshots).
    /// </summary>
    public class WebSearchResult
    {
        public class Source
        {
            public string url = "", site = "", label = "", tier = "";
            public bool hasList;
        }

        public class Shade
        {
            public string code = "", hex = "";
        }

        /// <summary>Brand and product name as one comparable text: the result screen shows these facts only for the product they belong to.</summary>
        public string key = "";

        // ---- the brand
        public string brandName = "", market = "Global";
        public string brandWebsite = "", brandLogoUrl = "", brandImageUrl = "";
        /// <summary>True when a page on the brand's own site was found (the website is then the maker's; otherwise it is the AI's suggestion).</summary>
        public bool brandWebsiteOfficial;

        // ---- the product
        public string productName = "", cosmeticType = "", description = "";
        public string productPhotoUrl = "", productPageUrl = "";
        /// <summary>The minimum age of use as the maker states it; 0 = not stated.</summary>
        public int minimumAge;
        public List<Shade> shades = new List<Shade>();
        public List<string> ingredients = new List<string>();

        // ---- the sources: the maker's site (or an unverified one), a shop, and one more page
        public Source primary, secondary, third;

        public static string KeyOf(string brand, string product) =>
            ((brand ?? "") + "|" + (product ?? "")).Trim().ToLowerInvariant();

        /// <summary>True when this result is about the product the scan ended with.</summary>
        public bool IsAbout(string brand, string product) => key.Length > 1 && key == KeyOf(brand, product);

        // ---------------------------------------------------------------------------------------
        //  Reading the server's "details" (plain Dictionary from the app's JSON reader; untrusted, so every value is checked)
        // ---------------------------------------------------------------------------------------
        public static WebSearchResult From(Dictionary<string, object> d)
        {
            var r = new WebSearchResult();
            if (d == null) return r;
            r.market = Str(d, "market", 20) == "Global" ? "Global" : "Global";
            r.description = Str(d, "description", 300);
            r.brandWebsite = Link(d, "brandWebsite");
            r.brandWebsiteOfficial = d.TryGetValue("brandWebsiteOfficial", out object o) && o is bool b && b;
            r.brandLogoUrl = Link(d, "brandLogoUrl");
            r.brandImageUrl = Link(d, "brandImageUrl");
            r.productPhotoUrl = Link(d, "productPhotoUrl");
            r.productPageUrl = Link(d, "productPageUrl");
            if (d.TryGetValue("minimumAge", out object age) && age is IConvertible)
            {
                try { int years = Convert.ToInt32(age); r.minimumAge = years > 0 && years <= 18 ? years : 0; } catch (Exception) { r.minimumAge = 0; }
            }
            if (d.TryGetValue("shades", out object rawShades) && rawShades is List<object> shadeList)
                foreach (object item in shadeList)
                {
                    if (!(item is Dictionary<string, object> s) || r.shades.Count >= 6) continue;
                    var shade = new Shade { code = Str(s, "code", 30), hex = Hex(Str(s, "hex", 7)) };
                    if (shade.code.Length > 0 || shade.hex.Length > 0) r.shades.Add(shade);
                }
            if (d.TryGetValue("sources", out object rawTiers) && rawTiers is Dictionary<string, object> tiers)
            {
                r.primary = SourceOf(tiers, "primary");
                r.secondary = SourceOf(tiers, "secondary");
                r.third = SourceOf(tiers, "third");
            }
            return r;
        }

        static Source SourceOf(Dictionary<string, object> tiers, string slot)
        {
            if (!tiers.TryGetValue(slot, out object raw) || !(raw is Dictionary<string, object> s)) return null;
            string url = Link(s, "url");
            if (url.Length == 0) return null;
            string tier = Str(s, "tier", 20);
            if (tier != "official" && tier != "unverified" && tier != "retailer") tier = "other";
            return new Source { url = url, site = Str(s, "site", 80), tier = tier, label = LabelOf(tier), hasList = s.TryGetValue("hasList", out object h) && h is bool yes && yes };
        }

        /// <summary>The words for a kind of source (the app's own words: the server's label is not shown as it came).</summary>
        public static string LabelOf(string tier) =>
            tier == "official" ? "Official manufacturer site" : tier == "unverified" ? "Unverified site" : tier == "retailer" ? "E-commerce retailer" : "Other site";

        static string Str(Dictionary<string, object> d, string key, int most)
        {
            if (d == null || !d.TryGetValue(key, out object v) || !(v is string s)) return "";
            var sb = new System.Text.StringBuilder(Math.Min(s.Length, most));
            foreach (char c in s)
            {
                if (sb.Length >= most) break;
                sb.Append(char.IsControl(c) || c == '<' || c == '>' ? ' ' : c);       // shown as text, never as markup
            }
            return sb.ToString().Trim();
        }

        /// <summary>An https address of at most 300 characters with a real host and no user name in front of it; "" otherwise.</summary>
        public static string Link(Dictionary<string, object> d, string key)
        {
            string s = Str(d, key, 301);
            if (s.Length == 0 || s.Length > 300 || !s.StartsWith("https://", StringComparison.Ordinal)) return "";
            return Uri.TryCreate(s, UriKind.Absolute, out Uri uri) && uri.Host.Contains(".") && string.IsNullOrEmpty(uri.UserInfo) && s.IndexOf((char)92) < 0 ? s : "";
        }

        static string Hex(string s)
        {
            if (s.Length != 7 || s[0] != '#') return "";
            for (int i = 1; i < 7; i++) if (!Uri.IsHexDigit(s[i])) return "";
            return s.ToUpperInvariant();
        }
    }
}
