// Ingredient-level precision / recall of the app's label reader (GlowUp.Scan.IngredientTextParser) on labelled examples.
//
//   dotnet run --project tools/ScannerEval -- tests/fixtures/ingredient-labels [--min-precision 0.9] [--min-recall 0.9]
//
// A fixture file is a JSON object or an array of objects:
//   { "id": "...", "kind": "synthetic" | "real", "note": "...", "ocrText": "text the OCR gave", "expected": ["names a person reads on the pack"] }
// What is measured is the step AFTER the camera and the text reader: text -> list of ingredient names. It says nothing about how well the
// camera reads a curved, glossy or dark label; only fixtures of kind "real" (OCR text taken from a phone, expected list typed from the pack)
// say something about the world, and the numbers of "synthetic" ones must never be quoted as scanner accuracy.
//
// Names are compared after the app's own name matching (Aqua = Water, CI 77891 = Titanium Dioxide ...); a name the app does not know is compared by
// its normalised spelling. "May contain" names count as expected names too (the reader returns them in the same list).
// Flag recall: of the expected ingredients that the app's built-in table rates caution or harmful, how many the reader found.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GlowUp.Data;
using GlowUp.Scan;

static class Program
{
    class Fixture { public string id, kind, note, ocrText; public List<string> expected; public List<string> mayContain; }

    static string Canonical(string name, out string risk)
    {
        risk = "";
        var match = IngredientTextParser.MatchEntry(name);
        // built-in entries have no id, so the entry is told by its name; two spellings the app treats as one entry (Tocopherol, Tocopheryl Acetate) are one name here
        if (match.data != null && !string.IsNullOrEmpty(match.data.name)) { risk = match.data.riskLevel ?? ""; return "known:" + match.data.name.ToLowerInvariant(); }
        return "text:" + TextKey.Squash(TextKey.Normalize(name));
    }

    static int Main(string[] args)
    {
        string folder = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "tests/fixtures/ingredient-labels";
        double minPrecision = Option(args, "--min-precision"), minRecall = Option(args, "--min-recall");
        if (!Directory.Exists(folder)) { Console.WriteLine("Fixture folder not found: " + folder); return 2; }

        // the first call into the app code touches Unity's native side once, which does not exist here; the code carries on after it
        try { IngredientTextParser.Read("Ingredients: Water"); } catch (Exception e) { Console.WriteLine("(warm-up: " + e.GetType().Name + " from Unity native code, expected outside the Editor)"); }

        var fixtures = new List<Fixture>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
        foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(f => f))
        {
            string json = File.ReadAllText(file);
            if (json.TrimStart().StartsWith("[")) fixtures.AddRange(JsonSerializer.Deserialize<List<Fixture>>(json, options));
            else fixtures.Add(JsonSerializer.Deserialize<Fixture>(json, options));
        }
        if (fixtures.Count == 0) { Console.WriteLine("No fixtures in " + folder); return 2; }

        var totals = new Dictionary<string, int[]>();      // kind -> tp, fp, fn, flaggedExpected, flaggedFound
        foreach (Fixture f in fixtures)
        {
            var expected = new Dictionary<string, string>();
            foreach (string name in (f.expected ?? new List<string>()).Concat(f.mayContain ?? new List<string>())) { string key = Canonical(name, out string risk); if (!expected.ContainsKey(key)) expected[key] = risk; }
            IngredientTextParser.ParsedList read = IngredientTextParser.Read(f.ocrText ?? "");
            var found = new HashSet<string>(read.inOrder.Select(n => Canonical(n, out string _)));
            int tp = expected.Keys.Count(found.Contains), fn = expected.Count - tp, fp = found.Count(k => !expected.ContainsKey(k));
            int flagged = expected.Count(e => e.Value == "caution" || e.Value == "harmful"), flaggedFound = expected.Count(e => (e.Value == "caution" || e.Value == "harmful") && found.Contains(e.Key));
            string kind = string.IsNullOrEmpty(f.kind) ? "synthetic" : f.kind;
            if (!totals.ContainsKey(kind)) totals[kind] = new int[5];
            int[] t = totals[kind]; t[0] += tp; t[1] += fp; t[2] += fn; t[3] += flagged; t[4] += flaggedFound;
            Console.WriteLine($"{(fp + fn == 0 ? "exact " : "differs")}  {f.id,-28} [{kind}]  precision {Ratio(tp, tp + fp)}  recall {Ratio(tp, tp + fn)}");
            if (fp + fn > 0)
            {
                foreach (var k in expected.Keys.Where(k => !found.Contains(k))) Console.WriteLine("          missed : " + k);
                foreach (var k in found.Where(k => !expected.ContainsKey(k))) Console.WriteLine("          extra  : " + k);
            }
        }

        Console.WriteLine();
        int failed = 0;
        foreach (var pair in totals)
        {
            int[] t = pair.Value;
            Console.WriteLine($"{pair.Key,-9} precision {Ratio(t[0], t[0] + t[1])}  recall {Ratio(t[0], t[0] + t[2])}  flag recall {(t[3] == 0 ? "n/a (no flagged ingredient in the examples)" : Ratio(t[4], t[3]) + $" ({t[4]} of {t[3]})")}");
            if (pair.Key == "real" || !totals.ContainsKey("real"))        // the minimums apply to the real examples; only without any, to the made-up ones
            {
                if (minPrecision > 0 && Fraction(t[0], t[0] + t[1]) < minPrecision) { Console.WriteLine($"  below the minimum precision {minPrecision:0.00}"); failed++; }
                if (minRecall > 0 && Fraction(t[0], t[0] + t[2]) < minRecall) { Console.WriteLine($"  below the minimum recall {minRecall:0.00}"); failed++; }
            }
        }
        if (!totals.ContainsKey("real")) Console.WriteLine("\nNo fixture of kind \"real\": these numbers say how the reader copes with the made-up examples, not how the scanner does on real packs.");
        return failed == 0 ? 0 : 1;
    }

    static double Fraction(int a, int b) => b == 0 ? 1.0 : (double)a / b;
    static string Ratio(int a, int b) => b == 0 ? "n/a " : Fraction(a, b).ToString("0.00");
    static double Option(string[] args, string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0; }
}
