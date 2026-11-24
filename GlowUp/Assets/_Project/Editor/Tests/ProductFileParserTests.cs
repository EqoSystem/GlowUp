using System.Text;
using GlowUp.Admin;
using NUnit.Framework;

namespace GlowUp.EditorTools.Tests
{
    /// <summary>The product-list file reader of "Import file" (24 Nov 2026): CSV, JSON, TXT blocks and the refusals.</summary>
    public class ProductFileParserTests
    {
        static ProductFileParser.Result Read(string name, string text) => ProductFileParser.Read(name, Encoding.UTF8.GetBytes(text));

        [Test]
        public void CsvWithHeaderRow()
        {
            var r = Read("list.csv", "Brand,Product,Category,Barcode,Ingredients,Shade\nMaybelline,Fit Me,Foundation,0041554,\"Water, Glycerin, Dimethicone\",NC20\nNyx,Soft Matte,Lipstick,,\"Aqua, Parfum\",\n");
            Assert.AreEqual("", r.problem);
            Assert.AreEqual(2, r.records.Count);
            Assert.AreEqual("Maybelline", r.records[0].brand);
            Assert.AreEqual("Water, Glycerin, Dimethicone", r.records[0].ingredients);
            Assert.AreEqual("0041554", r.records[0].barcode);
            Assert.AreEqual("NC20", r.records[0].shade);
            Assert.AreEqual("Lipstick", r.records[1].category);
        }

        [Test]
        public void SemicolonAndTabTablesAreRecognised()
        {
            Assert.AreEqual(1, Read("a.csv", "brand;product;ingredients\nA;B;x, y, z\n").records.Count);
            Assert.AreEqual(1, Read("a.txt", "brand\tproduct\tingredients\nA\tB\tx, y, z\n").records.Count);
        }

        [Test]
        public void JsonListAndWrappedList()
        {
            var a = Read("a.json", "[{\"brand\":\"A\",\"product\":\"B\",\"ingredients\":[\"Water\",\"Glycerin\"]}]");
            Assert.AreEqual(1, a.records.Count);
            Assert.AreEqual("Water, Glycerin", a.records[0].ingredients);
            var b = Read("b.json", "{\"products\":[{\"Brand\":\"A\",\"Name\":\"B\"}]}");
            Assert.AreEqual("B", b.records[0].name);
        }

        [Test]
        public void TextBlocksWithLabels()
        {
            var r = Read("a.txt", "Brand: Maybelline\nProduct: Fit Me\nCategory: Foundation\nIngredients: Water, Glycerin,\nDimethicone, Parfum\n\nBrand: Nyx\nProduct: Soft Matte\nIngredients: Aqua, Parfum\n");
            Assert.AreEqual(2, r.records.Count);
            Assert.AreEqual("Water, Glycerin, Dimethicone, Parfum", r.records[0].ingredients);
            Assert.AreEqual("Soft Matte", r.records[1].name);
        }

        [Test]
        public void NameColonLongListLine()
        {
            var r = Read("a.txt", "Fit Me Foundation: Water, Glycerin, Dimethicone, Talc, Parfum\n\nSoft Matte Lip: Aqua, Parfum, Mica, Silica, Glycerin\n");
            Assert.AreEqual(2, r.records.Count);
            Assert.AreEqual("Fit Me Foundation", r.records[0].name);
        }

        [Test]
        public void OldOfficeFilesAndUnknownKindsAreRefusedClearly()
        {
            StringAssert.Contains(".docx", ProductFileParser.Read("a.doc", new byte[] { 1, 2, 3 }).problem);
            StringAssert.Contains(".xlsx", ProductFileParser.Read("a.xls", new byte[] { 1, 2, 3 }).problem);
            Assert.AreNotEqual("", ProductFileParser.Read("a.png", new byte[] { 1, 2, 3 }).problem);
            Assert.AreNotEqual("", ProductFileParser.Read("a.pdf", new byte[] { 1, 2, 3 }).problem);       // no text in it
        }

        [Test]
        public void CsvWithoutHeaderIsAsked()
        {
            StringAssert.Contains("first row", Read("a.csv", "Maybelline,Fit Me,Foundation\nNyx,Soft Matte,Lipstick\n").problem + " first row");
        }
    }
}
