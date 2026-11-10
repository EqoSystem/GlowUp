// LIVE SCAN REGRESSION GUARD (New13 brief, item 10.0, 2026-11-10).
//
// Runs the live scan's matching code (the compiled Assembly-CSharp.dll, not a copy) on a fixed catalogue and 40+ fixed inputs, and
// writes one line per call with everything it returned. The lines are compared byte for byte with tests/live-scan-golden/golden.txt.
// Any difference = exit code 1. The photo path must never change these outputs: new photo logic lives in its own files.
//
// What it covers (the pure parts of the live scan chain): ProductIndex.InText / Search / Exact / SameNameDifferentKind / ByBarcode,
// ScanShortlist.For / Typed, ShadeCode.Key / Find / HexFor, ProductScope.TypeFromLabel / Judge / JudgeLabel, ProductMatcher.Find,
// TextKey.Normalize / Compact, and SmartScanFlowController.ComputeShadeMatchCounts (private static, through reflection).
// What it does not cover: the per-frame state inside SmartScanFlowController (ring, timers, the ranking flow over several frames),
// which needs a running scene. Those methods are guarded by the rule "the photo path does not edit them" and by git diff.
//
//   dotnet run --project tools/LiveScanGolden -p:AppAssembly=<dll> -- record   (writes the golden file; only for the first baseline)
//   dotnet run --project tools/LiveScanGolden -p:AppAssembly=<dll> -- check    (compares; exit 1 on any difference)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GlowUp.Data;
using GlowUp.Scan;

static class Program
{
    static readonly StringBuilder Out = new StringBuilder();
    static string Esc(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");
    static void Line(string what, string result) => Out.Append(Esc(what)).Append(" => ").Append(Esc(result)).Append('\n');
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string Q(string s) => s == null ? "null" : "\"" + s + "\"";

    static ProductData P(string id, string brand, string name, string type, string barcode = "", string[] codes = null, string[] shades = null, string[] aliases = null, int ingredients = 0, string[] list = null)
    {
        var ing = list != null ? new List<string>(list) : Enumerable.Range(0, ingredients).Select(i => "Ingredient " + i).ToList();
        return new ProductData
        {
            id = id, brandName = brand, brandId = TextKey.Slug(brand), name = name, cosmeticType = type, barcode = barcode, status = "Active",
            shadeCodes = new List<string>(codes ?? new string[0]), shades = new List<string>(shades ?? new string[0]),
            aliases = new List<string>(aliases ?? new string[0]), ingredients = ing
        };
    }

    static List<ProductData> Catalogue() => new List<ProductData>
    {
        P("fda_1", "Maybelline", "Superstay Matte Ink Liquid Lipstick", "Lipstick", "0041554496536", new[] { "15", "20", "65", "80" }, new[] { "#C24B62", "#A0303F", "#B5655A", "#8E2C3A" }, null, 0, new[] { "Dimethicone", "Trimethylsiloxysilicate", "Isododecane", "Nylon-611/Dimethicone Copolymer", "CI 77491" }),
        P("fda_2", "Maybelline", "Superstay 30H Active Wear Foundation", "Foundation", "", new[] { "112", "120", "128", "220", "310" }, new[] { "#E8C4A6", "#E2BC9C", "#DDB28F", "#D2A27C", "#B98257" }, new[] { "Super Stay Full Coverage Foundation" }, 30),
        P("fda_3", "Maybelline", "Fit Me Matte + Poreless Foundation", "Foundation", "3600531324506", new[] { "110", "115", "120", "128", "220" }, new[] { "#F0D2B8", "#EBC9AC", "#E5BE9F", "#DDB28F", "#CFA07A" }, null, 25),
        P("fda_4", "Maybelline", "Fit Me Concealer", "Concealer", "", new[] { "10", "15", "20", "25" }, null, null, 20),
        P("fda_5", "Maybelline", "Fit Me Matte + Poreless Powder", "Powder", "", new[] { "115", "120", "220" }, null, null, 12),
        P("fda_6", "L'Or\u00e9al Paris", "Color Riche Intense Volume Matte", "Lipstick", "", new[] { "200", "300", "601" }, null, null, 0),
        P("fda_7", "L'Or\u00e9al Paris", "True Match Super-Blendable Foundation", "Foundation", "3600522806035", new[] { "N4", "W5", "C3" }, null, null, 28),
        P("fda_8", "Absidy", "Complexion Blur Translucent Perfecting Powder", "Powder", "", null, null, new[] { "Complexion Blur Powder" }, 18),
        P("obf_9", "NIVEA", "Lip Tint", "Lip Tint", "", null, null, null, 5),
        P("fda_10", "Careline", "Lip Tint", "Lip Tint", "", new[] { "01", "02", "03" }, null, null, 0),
        P("fda_11", "Careline", "Lip and Cheek Tint", "Lip & Cheek Tint", "", null, null, null, 9),
        P("fda_12", "Ever Bilena", "Advance Matte Lipstick", "Lipstick", "4800047000123", new[] { "EB01", "EB02" }, null, null, 11),
        P("fda_13", "Ever Bilena", "EB Advance Two Way Cake", "Powder", "", null, null, new[] { "EBA Two Way Cake" }, 14),
        P("fda_14", "Issy & Co", "Velvet Lip Tint", "Lip Tint", "", new[] { "Rosewood", "Cherry" }, null, null, 7),
        P("fda_15", "Colourette", "Colourpop Lip and Cheek", "Lip & Cheek Tint", "", null, null, null, 8),
        P("fda_16", "BLK Cosmetics", "Universal All Day Lip and Cheek Matte Tint", "Lip & Cheek Tint", "", null, null, null, 10),
        P("fda_17", "Happy Skin", "Second Skin Foundation", "Foundation", "", new[] { "Light", "Medium", "Tan" }, null, null, 21),
        P("fda_18", "Sunnies Face", "Fluffmatte Lipstick", "Lipstick", "", null, null, null, 6),
        P("fda_19", "Maybelline", "Lash Sensational Mascara", "Mascara", "0041554433265", null, null, null, 15),
        P("fda_20", "Maybelline", "Hyper Easy Liquid Liner", "Eyeliner", "", null, null, null, 9),
        P("fda_21", "Detail Cosmetics", "Skin Tint Serum Foundation", "Foundation", "", null, null, null, 12)
    };

    static readonly string[] FrontTexts =
    {
        "MAYBELLINE\nNEW YORK\nSUPERSTAY\nMATTE INK\nLIQUID LIPSTICK\n65 SEDUCTRESS",
        "SUPER STAY\nMATTE INK\nmaybelline",
        "MAYBELLINE SUPERSTAY 30H ACTIVE WEAR FOUNDATION 128 WARM NUDE",
        "SUPER\nSTAY\n30H\nACTIVE WEAR\nFOUNDATION\n120",
        "superstay 3OH active wear foundati0n",
        "FIT ME\nMATTE + PORELESS\nFOUNDATION\n220 NATURAL BEIGE",
        "FIT ME\nMATTE + PORELESS\nPOWDER\n120",
        "fit me concealer 15 fair",
        "FIT ME",
        "L'OREAL PARIS\nCOLOR RICHE\nINTENSE VOLUME MATTE\n601",
        "LOREAL TRUE MATCH SUPER BLENDABLE FOUNDATION N4",
        "ABSIDY\nCOMPLEXION BLUR\nPOWDER",
        "complexion blur translucent perfecting powder",
        "NIVEA LIP TINT",
        "CARELINE\nLIP TINT\n02",
        "Careline lip & cheek tint",
        "EVER BILENA ADVANCE MATTE LIPSTICK EB02",
        "EBA TWO WAY CAKE",
        "ISSY & CO VELVET LIP TINT ROSEWOOD",
        "COLOURETTE COLOURPOP LIP AND CHEEK",
        "BLK UNIVERSAL ALL DAY LIP AND CHEEK MATTE TINT",
        "HAPPY SKIN SECOND SKIN FOUNDATION MEDIUM",
        "SUNNIES FACE FLUFFMATTE",
        "LASH SENSATIONAL\nWASHABLE MASCARA\nMAYBELLINE",
        "HYPER EASY LIQUID LINER",
        "DETAIL SKIN TINT SERUM FOUNDATION",
        "MAYBELLINE NEW YORK",
        "LIPSTICK",
        "FOUNDATION SPF 15",
        "CETAPHIL GENTLE SKIN CLEANSER",
        "SAFEGUARD PURE WHITE BAR SOAP",
        "COLGATE TOOTHPASTE",
        "NIVEA SOFT MOISTURISING CREME",
        "Ingredients: Water, Glycerin, Dimethicone, CI 77891",
        "",
        "    ",
        "M4YBELL1NE SUPERST4Y MATTE 1NK",
        "superstay matte ink lipstick 20 pioneer",
        "VELVET LIP TINT CHERRY",
        "COLOR RICHE 300"
    };

    static readonly (string brand, string name)[] Typed =
    {
        ("Maybelline", "Superstay Matte Ink"), ("", "fit me foundation"), ("Careline", "lip tint"), ("", "lip tint"), ("L'Oreal", "color riche"),
        ("Absidy", "complexion blur powder"), ("", "two way cake"), ("Nonexistent Brand", "Glow Serum"), ("", ""), ("Maybeline", "supersty 30h")
    };

    static readonly string[] Barcodes = { "0041554496536", "41554496536", " 0 041554 496536 ", "3600531324506", "3600522806035", "4800047000123", "0041554433265", "123", "", "9999999999999" };

    static readonly (string read, string pid)[] ShadeReads =
    {
        ("65", "fda_1"), ("065", "fda_1"), ("6S", "fda_1"), ("128", "fda_2"), ("12B", "fda_2"), ("220 NATURAL BEIGE", "fda_3"), ("N4", "fda_7"), ("n 4", "fda_7"),
        ("EB02", "fda_12"), ("ROSEWOOD", "fda_14"), ("999", "fda_1"), ("", "fda_1"), ("02", "fda_10"), ("Medium", "fda_17")
    };

    static readonly (string type, string name)[] ScopeCases =
    {
        ("Lipstick", "Superstay Matte Ink"), ("Foundation", "Fit Me"), ("", "Cetaphil Gentle Skin Cleanser"), ("Moisturizer", "Nivea Creme"), ("", "Lip Tint"),
        ("Serum", "Skin Tint Serum Foundation"), ("Soap", "Safeguard"), ("Mascara", ""), ("", ""), ("Sunscreen", "UV Shield SPF 50"), ("Powder", "Two Way Cake")
    };

    static string Cands(List<ProductIndex.Candidate> list) =>
        "[" + string.Join("; ", (list ?? new List<ProductIndex.Candidate>()).Select(c => c.product.id + " s=" + F(c.score) + " b=" + c.brandAgrees + " x=" + c.exactName + " w=" + c.leftOutWeak + " e=" + F(c.evidence))) + "]";

    static string Fields(object o)
    {
        if (o == null) return "null";
        var parts = new List<string>();
        foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            object v = f.GetValue(o);
            string s = v == null ? "null" : v is ProductData pd ? pd.id : v is float fl ? F(fl) : v is string str ? Q(str) : v is IEnumerable e && !(v is string) ? "[" + string.Join(",", e.Cast<object>().Select(x => x?.ToString())) + "]" : v.ToString();
            parts.Add(f.Name + "=" + s);
        }
        return "{" + string.Join(" ", parts) + "}";
    }

    static string Try(Func<string> f) { try { return f(); } catch (Exception e) { return "EXC " + e.GetType().Name; } }

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "check";
        string golden = args.Length > 1 ? args[1] : Path.Combine("tests", "live-scan-golden", "golden.txt");
        // the first call into the app code touches Unity's native side once (it does not exist here); later calls run normally
        try { TextKey.Normalize("warm up"); ProductIndex.Build(new List<ProductData>()); } catch { }
        try { Console.Error.WriteLine("warm-up 2"); ScanShortlist.Typed(ProductIndex.Build(new List<ProductData>()), "a", "b"); } catch { }

        List<ProductData> catalogue = Catalogue();
        ProductIndex index = ProductIndex.Build(catalogue, new Dictionary<string, string> { { "eb two way cake", "fda_13" } });
        Line("index.Count", index.Count.ToString(CultureInfo.InvariantCulture));

        foreach (string t in FrontTexts)
        {
            Line("Normalize " + Q(t), Try(() => Q(TextKey.Normalize(t)) + " compact=" + Q(TextKey.Compact(t))));
            string kind = Try(() => ProductScope.TypeFromLabel(t));
            Line("TypeFromLabel " + Q(t), Q(kind));
            Line("JudgeLabel " + Q(t), Try(() => Fields(ProductScope.JudgeLabel(t))));
            Line("InText3 " + Q(t), Try(() => Cands(index.InText(t))));
            Line("InTextAll " + Q(t), Try(() => Cands(index.InText(t, index.Count, kind.StartsWith("EXC") ? "" : kind))));
            Line("ShortlistFor " + Q(t), Try(() => "[" + string.Join("; ", ScanShortlist.For(index, t, null).Select(Fields)) + "]"));
            Line("ShortlistFor4 " + Q(t), Try(() => "[" + string.Join("; ", ScanShortlist.For(index, t, null, new[] { "fda_1" }, 4).Select(Fields)) + "]"));
        }
        foreach (var (brand, name) in Typed)
        {
            Line("Search " + Q(brand) + " " + Q(name), Try(() => Cands(index.Search(brand, name, 5))));
            Line("Exact " + Q(brand) + " " + Q(name), Try(() => index.Exact(brand, name)?.id ?? "null"));
            Line("SameNameDifferentKind " + Q(brand) + " " + Q(name), Try(() => "[" + string.Join(",", index.SameNameDifferentKind(brand, name).Select(p => p.id)) + "]"));
            Line("Typed " + Q(brand) + " " + Q(name), Try(() => "[" + string.Join("; ", ScanShortlist.Typed(index, name, brand).Select(Fields)) + "]"));
        }
        foreach (string bc in Barcodes)
            Line("ByBarcode " + Q(bc), Try(() => (index.ByBarcode(bc)?.id ?? "null") + " key=" + Q(ProductIndex.BarcodeKey(bc))));
        foreach (var (read, pid) in ShadeReads)
        {
            ProductData p = index.ById(pid);
            Line("ShadeCode " + Q(read) + " " + pid, Try(() => "key=" + Q(ShadeCode.Key(read)) + " folded=" + Q(ShadeCode.Folded(ShadeCode.Key(read))) + " find=" + ShadeCode.Find(read, p.shadeCodes) + " hex=" + Q(ShadeCode.HexFor(read, p.shadeCodes, p.shades))));
        }
        foreach (var (type, name) in ScopeCases)
            Line("Judge " + Q(type) + " " + Q(name), Try(() => Fields(ProductScope.Judge(type, name)) + " makeupType=" + Q(ProductScope.MakeupType(type, name))));
        var lists = new[]
        {
            new List<string> { "Dimethicone", "Trimethylsiloxysilicate", "Isododecane", "Nylon-611/Dimethicone Copolymer", "CI 77491" },
            new List<string> { "Isododecane", "Dimethicone", "CI 77491" },
            new List<string> { "Water", "Glycerin" },
            new List<string>()
        };
        foreach (var l in lists)
        {
            Line("ProductMatcher " + string.Join("|", l), Try(() => ProductMatcher.Find(l, catalogue)?.id ?? "null"));
            Line("ProductMatcher(brand maybelline) " + string.Join("|", l), Try(() => ProductMatcher.Find(l, catalogue, "maybelline")?.id ?? "null"));
        }
        MethodInfo shadeCounts = typeof(SmartScanFlowController).GetMethod("ComputeShadeMatchCounts", BindingFlags.NonPublic | BindingFlags.Static);
        var tokenSets = new[] { new[] { "65", "superstay" }, new[] { "128", "120" }, new[] { "n4" }, new[] { "rosewood", "velvet" }, new string[0] };
        foreach (var tokens in tokenSets)
        {
            var dict = tokens.ToDictionary(t => t, t => 1);
            Line("ComputeShadeMatchCounts " + string.Join("|", tokens), Try(() =>
            {
                var r = (Dictionary<string, int>)shadeCounts.Invoke(null, new object[] { catalogue, dict });
                return "{" + string.Join(",", r.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + ":" + k.Value)) + "}";
            }));
        }

        string now = Out.ToString();
        if (mode == "record")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden));
            File.WriteAllText(golden, now, new UTF8Encoding(false));
            Console.WriteLine("Recorded " + now.Split('\n').Length + " lines to " + golden);
            return 0;
        }
        if (!File.Exists(golden)) { Console.WriteLine("No golden file at " + golden + ": run with 'record' on the unchanged code first."); return 2; }
        string was = File.ReadAllText(golden);
        if (was == now) { Console.WriteLine("LIVE SCAN GOLDEN: IDENTICAL (" + (now.Split('\n').Length - 1) + " outputs)"); return 0; }
        string[] a = was.Split('\n'), b = now.Split('\n');
        int shown = 0;
        for (int i = 0; i < Math.Max(a.Length, b.Length) && shown < 20; i++)
        {
            string x = i < a.Length ? a[i] : "(none)", y = i < b.Length ? b[i] : "(none)";
            if (x != y) { Console.WriteLine("- " + x + "\n+ " + y); shown++; }
        }
        File.WriteAllText(golden + ".actual", now, new UTF8Encoding(false));
        Console.WriteLine("LIVE SCAN GOLDEN: DIFFERENT. The live scan matching changed. Actual output in " + golden + ".actual");
        return 1;
    }
}
