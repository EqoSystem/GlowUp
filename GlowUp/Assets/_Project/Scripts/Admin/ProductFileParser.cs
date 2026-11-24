using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using GlowUp.Data;

namespace GlowUp.Admin
{
    /// <summary>
    /// Reads a list of products with their ingredients from a file the staff member picks (24 Nov 2026):
    /// .xlsx and .csv (a table with a header row), .json (a list of objects), .docx and .txt (a table, or blocks of "Brand:", "Product:",
    /// "Ingredients:" lines), and .pdf when it holds real text (a scanned PDF has none: take photos instead). Old .doc and .xls files are refused
    /// with a clear sentence. Pure code with no Unity calls, so it is tested in the Editor (Editor/Tests/ProductFileParserTests.cs).
    /// Nothing is trusted: every record is a "needs review" offer, never final (see ProductFileImport).
    /// </summary>
    public static class ProductFileParser
    {
        public class Record { public string brand = "", name = "", category = "", barcode = "", ingredients = "", shade = ""; public bool Any => name.Length > 0 || brand.Length > 0 || ingredients.Length > 0; }
        public class Result { public List<Record> records = new List<Record>(); public string problem = ""; }

        public const int MaxRecords = 300;

        // header words (lower case, letters only) that name each column
        static readonly string[][] Heads =
        {
            new[] { "brand", "brandname", "maker", "manufacturer" },
            new[] { "product", "productname", "name", "item", "title" },
            new[] { "category", "type", "kind", "cosmetictype", "producttype" },
            new[] { "barcode", "upc", "ean", "gtin", "code" },
            new[] { "ingredients", "ingredient", "inci", "ingredientlist", "composition" },
            new[] { "shade", "shadecode", "color", "colour", "shadename" }
        };

        public static Result Read(string fileName, byte[] bytes)
        {
            var result = new Result();
            string ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
            try
            {
                if (bytes == null || bytes.Length == 0) { result.problem = "The file is empty."; return result; }
                switch (ext)
                {
                    case ".doc": result.problem = "This is an old Word file (.doc). Open it and save it as .docx, then import it again."; return result;
                    case ".xls": result.problem = "This is an old Excel file (.xls). Open it and save it as .xlsx or .csv, then import it again."; return result;
                    case ".json": FromJson(Encoding.UTF8.GetString(bytes), result); break;
                    case ".xlsx": FromRows(XlsxRows(bytes), result); break;
                    case ".docx": FromText(DocxText(bytes), result); break;
                    case ".pdf":
                        string text = PdfText(bytes);
                        if (text.Trim().Length < 20) { result.problem = "No text could be read from this PDF. A scanned or photographed PDF has none: use Gallery or Camera instead, or export the list as Excel or CSV."; return result; }
                        FromText(text, result); break;
                    case ".csv": case ".tsv": case ".txt": case "": FromText(DecodeText(bytes), result); break;
                    default: result.problem = "GlowUp reads Word (.docx), Excel (.xlsx), PDF, JSON, CSV and TXT files."; return result;
                }
            }
            catch (Exception e) { result.problem = "The file could not be read (" + e.GetType().Name + "). Check that it is not damaged or password protected."; return result; }
            if (result.records.Count > MaxRecords) result.records.RemoveRange(MaxRecords, result.records.Count - MaxRecords);
            if (result.records.Count == 0 && result.problem.Length == 0)
                result.problem = "No products were found. Use a header row (Brand, Product, Category, Barcode, Ingredients, Shade) or blocks of lines such as \"Brand: ...\", \"Product: ...\", \"Ingredients: ...\".";
            return result;
        }

        static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            return Encoding.UTF8.GetString(bytes);
        }

        // ------------------------------------------------------------------ tables
        static string Key(string header) => Regex.Replace((header ?? "").ToLowerInvariant(), "[^a-z]", "");

        /// <summary>Which column holds which field, from a header row. -1 = no such column. Null when fewer than two known headers are present.</summary>
        static int[] Columns(IList<string> header)
        {
            var at = new int[Heads.Length];
            for (int f = 0; f < at.Length; f++) at[f] = -1;
            int known = 0;
            for (int c = 0; c < header.Count; c++)
            {
                string k = Key(header[c]);
                for (int f = 0; f < Heads.Length; f++)
                    if (at[f] < 0 && Array.IndexOf(Heads[f], k) >= 0) { at[f] = c; known++; break; }
            }
            return known >= 2 && (at[1] >= 0 || at[0] >= 0) ? at : null;
        }

        static void FromRows(List<List<string>> rows, Result result)
        {
            if (rows.Count == 0) return;
            int[] at = Columns(rows[0]);
            if (at == null) { result.problem = "The first row must name the columns, for example Brand, Product, Category, Barcode, Ingredients, Shade."; return; }
            for (int r = 1; r < rows.Count; r++)
            {
                List<string> row = rows[r];
                string Cell(int f) => at[f] >= 0 && at[f] < row.Count ? Tidy(row[at[f]]) : "";
                var rec = new Record { brand = Cell(0), name = Cell(1), category = Cell(2), barcode = Digits(Cell(3)), ingredients = Cell(4), shade = Cell(5) };
                if (rec.Any) result.records.Add(rec);
            }
        }

        static string Tidy(string s) => Regex.Replace((s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " "), @"\s+", " ").Trim();
        static string Digits(string s) { var b = new StringBuilder(); foreach (char c in s ?? "") if (c >= '0' && c <= '9') b.Append(c); return b.ToString(); }

        static List<string> SplitLine(string line, char delimiter)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; } else cell.Append(c); }
                else if (c == '"') quoted = true;
                else if (c == delimiter) { cells.Add(cell.ToString()); cell.Length = 0; }
                else cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells;
        }

        /// <summary>Splits delimited text into rows, keeping line breaks inside quotes.</summary>
        static List<List<string>> Delimited(string text, char delimiter)
        {
            var rows = new List<List<string>>();
            var line = new StringBuilder();
            bool quoted = false;
            foreach (char c in text)
            {
                if (c == '"') quoted = !quoted;
                if ((c == '\n' || c == '\r') && !quoted)
                {
                    if (line.ToString().Trim().Length > 0) rows.Add(SplitLine(line.ToString(), delimiter));
                    line.Length = 0;
                    continue;
                }
                line.Append(c);
            }
            if (line.ToString().Trim().Length > 0) rows.Add(SplitLine(line.ToString(), delimiter));
            return rows;
        }

        // ------------------------------------------------------------------ text (txt, docx, pdf, csv)
        static void FromText(string text, Result result)
        {
            text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            string first = null;
            foreach (string l in text.Split('\n')) if (l.Trim().Length > 0) { first = l; break; }
            if (first == null) return;
            foreach (char d in new[] { '\t', ';', ',' })
            {
                if (first.IndexOf(d) < 0) continue;
                List<string> head = SplitLine(first, d);
                if (Columns(head) != null) { FromRows(Delimited(text, d), result); return; }
            }
            FromBlocks(text, result);
        }

        static readonly Regex LabelLine = new Regex(@"^\s*(brand|product(?: name)?|name|category|type|kind|barcode|upc|ean|ingredients?|inci|shade(?: code)?|colou?r)\s*[:\-]\s*(.*)$", RegexOptions.IgnoreCase);

        /// <summary>Blocks separated by an empty line (or by a new "Brand:" / "Product:" line). A line "Name: a, b, c, d, e" with many commas is an ingredient list.</summary>
        static void FromBlocks(string text, Result result)
        {
            Record cur = null;
            string openField = null;                 // the field a following line continues (an ingredient list over several lines)
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) { Close(result, ref cur); openField = null; continue; }
                Match m = LabelLine.Match(line);
                if (m.Success)
                {
                    string label = m.Groups[1].Value.ToLowerInvariant(), value = m.Groups[2].Value.Trim();
                    bool isName = label.StartsWith("product") || label == "name";
                    if (cur != null && ((isName && cur.name.Length > 0) || (label == "brand" && cur.brand.Length > 0))) { Close(result, ref cur); openField = null; }
                    if (cur == null) cur = new Record();
                    openField = null;
                    if (label == "brand") cur.brand = value;
                    else if (isName) cur.name = value;
                    else if (label == "category" || label == "type" || label == "kind") cur.category = value;
                    else if (label == "barcode" || label == "upc" || label == "ean") cur.barcode = Digits(value);
                    else if (label.StartsWith("shade") || label.StartsWith("colo")) cur.shade = value;
                    else { cur.ingredients = value; openField = "ingredients"; }
                    continue;
                }
                if (openField == "ingredients" && cur != null) { cur.ingredients += (cur.ingredients.EndsWith(",") ? " " : ", ") + line; continue; }
                // "Maybelline Fit Me Foundation: Water, Dimethicone, ..."  (a name, a colon, a long comma list)
                int colon = line.IndexOf(':');
                if (colon > 2 && CountOf(line, ',') >= 4)
                {
                    Close(result, ref cur);
                    cur = new Record { name = line.Substring(0, colon).Trim(), ingredients = line.Substring(colon + 1).Trim() };
                    openField = "ingredients";
                    continue;
                }
                if (cur == null) { cur = new Record { name = line }; continue; }          // a lone line opens a product named by it
                if (cur.name.Length == 0) cur.name = line;
                else if (CountOf(line, ',') >= 4) { cur.ingredients = line; openField = "ingredients"; }
            }
            Close(result, ref cur);
        }

        static void Close(Result result, ref Record cur) { if (cur != null && cur.Any) result.records.Add(cur); cur = null; }
        static int CountOf(string s, char c) { int n = 0; foreach (char x in s) if (x == c) n++; return n; }

        // ------------------------------------------------------------------ json
        static void FromJson(string json, Result result)
        {
            object root = MiniJson.Deserialize(json);
            var list = root as List<object>;
            var top = root as Dictionary<string, object>;
            if (list == null && top != null)
                foreach (string k in new[] { "products", "items", "data", "records" })
                {
                    object inner;
                    if (top.TryGetValue(k, out inner) && inner is List<object>) { list = (List<object>)inner; break; }
                }
            if (list == null) { result.problem = "The JSON file must hold a list of products, for example [{\"brand\":\"...\",\"product\":\"...\",\"ingredients\":\"...\"}]."; return; }
            foreach (object item in list)
            {
                var o = item as Dictionary<string, object>;
                if (o == null) continue;
                var rec = new Record();
                foreach (KeyValuePair<string, object> kv in o)
                {
                    string k = Key(kv.Key);
                    string v;
                    var many = kv.Value as List<object>;
                    if (many != null) { var parts = new List<string>(); foreach (object x in many) parts.Add(Tidy(Convert.ToString(x))); v = string.Join(", ", parts.ToArray()); }
                    else v = Tidy(Convert.ToString(kv.Value));
                    if (Array.IndexOf(Heads[0], k) >= 0) rec.brand = v;
                    else if (Array.IndexOf(Heads[1], k) >= 0) rec.name = v;
                    else if (Array.IndexOf(Heads[2], k) >= 0) rec.category = v;
                    else if (Array.IndexOf(Heads[3], k) >= 0) rec.barcode = Digits(v);
                    else if (Array.IndexOf(Heads[4], k) >= 0) rec.ingredients = v;
                    else if (Array.IndexOf(Heads[5], k) >= 0) rec.shade = v;
                }
                if (rec.Any) result.records.Add(rec);
            }
        }

        // ------------------------------------------------------------------ xlsx and docx (zip files of XML)
        static string EntryText(ZipArchive zip, string path)
        {
            ZipArchiveEntry e = zip.GetEntry(path);
            if (e == null) return null;
            using (Stream s = e.Open()) using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        static List<List<string>> XlsxRows(byte[] bytes)
        {
            var rows = new List<List<string>>();
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                var shared = new List<string>();
                string sst = EntryText(zip, "xl/sharedStrings.xml");
                if (sst != null)
                    foreach (Match si in Regex.Matches(sst, @"<si>(.*?)</si>", RegexOptions.Singleline))
                    {
                        var b = new StringBuilder();
                        foreach (Match t in Regex.Matches(si.Groups[1].Value, @"<t[^>]*>(.*?)</t>", RegexOptions.Singleline)) b.Append(Unescape(t.Groups[1].Value));
                        shared.Add(b.ToString());
                    }
                string sheet = EntryText(zip, "xl/worksheets/sheet1.xml");
                if (sheet == null) throw new InvalidDataException("no sheet");
                foreach (Match row in Regex.Matches(sheet, @"<row[^>]*>(.*?)</row>", RegexOptions.Singleline))
                {
                    var cells = new List<string>();
                    foreach (Match c in Regex.Matches(row.Groups[1].Value, @"<c ([^>]*?)(?:/>|>(.*?)</c>)", RegexOptions.Singleline))
                    {
                        string attrs = c.Groups[1].Value, body = c.Groups[2].Value;
                        Match r = Regex.Match(attrs, "r=\"([A-Z]+)\\d+\"");
                        int col = r.Success ? ColumnIndex(r.Groups[1].Value) : cells.Count;
                        while (cells.Count < col) cells.Add("");
                        string value = "";
                        Match v = Regex.Match(body, @"<v>(.*?)</v>", RegexOptions.Singleline);
                        int idx;
                        if (attrs.Contains("t=\"s\"") && v.Success && int.TryParse(v.Groups[1].Value, out idx) && idx >= 0 && idx < shared.Count) value = shared[idx];
                        else if (attrs.Contains("t=\"inlineStr\"")) { Match t = Regex.Match(body, @"<t[^>]*>(.*?)</t>", RegexOptions.Singleline); value = t.Success ? Unescape(t.Groups[1].Value) : ""; }
                        else if (v.Success) value = Unescape(v.Groups[1].Value);
                        cells.Add(value);
                    }
                    rows.Add(cells);
                }
            }
            return rows;
        }

        static int ColumnIndex(string letters) { int n = 0; foreach (char c in letters) n = n * 26 + (c - 'A' + 1); return n - 1; }

        static string DocxText(byte[] bytes)
        {
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                string xml = EntryText(zip, "word/document.xml");
                if (xml == null) throw new InvalidDataException("no document");
                var lines = new StringBuilder();
                // a table row = one line of cells split by tabs; a paragraph outside a table = one line
                foreach (Match block in Regex.Matches(xml, @"<w:tr[ >].*?</w:tr>|<w:p[ >].*?</w:p>", RegexOptions.Singleline))
                {
                    string b = block.Value;
                    if (b.StartsWith("<w:tr"))
                    {
                        var cells = new List<string>();
                        foreach (Match tc in Regex.Matches(b, @"<w:tc[ >].*?</w:tc>", RegexOptions.Singleline)) cells.Add(Tidy(RunText(tc.Value)));
                        lines.Append(string.Join("\t", cells.ToArray())).Append('\n');
                    }
                    else if (!InsideRow(xml, block.Index)) lines.Append(RunText(b)).Append('\n');
                }
                return lines.ToString();
            }
        }

        // a paragraph already counted inside a table row must not be added again
        static bool InsideRow(string xml, int at)
        {
            int open = xml.LastIndexOf("<w:tr", at, StringComparison.Ordinal), close = xml.LastIndexOf("</w:tr>", at, StringComparison.Ordinal);
            return open >= 0 && open > close;
        }

        static string RunText(string xml)
        {
            var b = new StringBuilder();
            foreach (Match t in Regex.Matches(xml, @"<w:t[^>]*>(.*?)</w:t>|<w:tab/>|<w:br/>", RegexOptions.Singleline))
            {
                if (t.Value.StartsWith("<w:tab") || t.Value.StartsWith("<w:br")) b.Append(' ');
                else b.Append(Unescape(t.Groups[1].Value));
            }
            return b.ToString();
        }

        static string Unescape(string s) => (s ?? "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");

        // ------------------------------------------------------------------ pdf (text only)
        /// <summary>The text of a PDF that stores real text (not a picture of a page). Flate-compressed content streams, Tj / TJ strings.</summary>
        static string PdfText(byte[] bytes)
        {
            Encoding latin1 = Encoding.GetEncoding(28591);
            string latin = latin1.GetString(bytes);
            var text = new StringBuilder();
            int at = 0;
            while (true)
            {
                int s = latin.IndexOf("stream", at, StringComparison.Ordinal);
                if (s < 0) break;
                int start = s + 6;
                if (start < latin.Length && latin[start] == '\r') start++;
                if (start < latin.Length && latin[start] == '\n') start++;
                int end = latin.IndexOf("endstream", start, StringComparison.Ordinal);
                if (end < 0) break;
                at = end + 9;
                if (s >= 3 && string.CompareOrdinal(latin, s - 3, "end", 0, 3) == 0) continue;        // the "stream" of "endstream"
                int dictStart = latin.LastIndexOf("<<", s, StringComparison.Ordinal);
                string dict = dictStart >= 0 ? latin.Substring(dictStart, s - dictStart) : "";
                var raw = new byte[Math.Max(0, end - start)];
                Array.Copy(bytes, start, raw, 0, raw.Length);
                byte[] data = raw;
                if (dict.Contains("FlateDecode"))
                {
                    try
                    {
                        using (var input = new MemoryStream(raw, 2, Math.Max(0, raw.Length - 2)))        // skip the 2-byte zlib header
                        using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
                        using (var output = new MemoryStream()) { inflate.CopyTo(output); data = output.ToArray(); }
                    }
                    catch (Exception) { continue; }
                }
                string content = latin1.GetString(data);
                if (content.IndexOf("BT", StringComparison.Ordinal) < 0) continue;
                foreach (Match op in Regex.Matches(content, @"\[((?:[^\]\\]|\\.)*)\]\s*TJ|\(((?:[^()\\]|\\.)*)\)\s*(?:Tj|')|\bT\*|\bET\b|[-\d.]+\s+[-\d.]+\s+T[dD]", RegexOptions.Singleline))
                {
                    string v = op.Value;
                    if (v.EndsWith("TJ")) { foreach (Match str in Regex.Matches(op.Groups[1].Value, @"\(((?:[^()\\]|\\.)*)\)")) text.Append(PdfString(str.Groups[1].Value)); }
                    else if (v.StartsWith("(")) text.Append(PdfString(op.Groups[2].Value));
                    else text.Append('\n');
                }
                text.Append('\n');
            }
            return Regex.Replace(text.ToString(), @"\n{3,}", "\n\n");
        }

        static string PdfString(string s)
        {
            var b = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { b.Append(c); continue; }
                char n = s[++i];
                if (n == 'n' || n == 'r') b.Append('\n');
                else if (n == 't') b.Append(' ');
                else if (n >= '0' && n <= '7')
                {
                    int v = n - '0', k = 0;
                    while (k < 2 && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7') { v = v * 8 + (s[++i] - '0'); k++; }
                    b.Append((char)v);
                }
                else b.Append(n);
            }
            return b.ToString();
        }
    }
}
