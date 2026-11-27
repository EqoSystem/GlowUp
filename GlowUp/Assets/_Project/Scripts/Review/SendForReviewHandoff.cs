namespace GlowUp.Review
{
    /// <summary>
    /// What AI Smart Scan already knows when the user taps "Send for review": the barcode and any brand or name read so far.
    /// The Send for Review screen takes it once (Take) and fills its boxes with it; nothing else is carried over.
    /// </summary>
    public static class SendForReviewHandoff
    {
        public static string Barcode = "", Brand = "", ProductName = "";

        public static void Set(string barcode, string brand, string productName)
        {
            Barcode = (barcode ?? "").Trim(); Brand = (brand ?? "").Trim(); ProductName = (productName ?? "").Trim();
        }

        /// <summary>Returns the values and forgets them, so a later visit starts empty.</summary>
        public static (string barcode, string brand, string productName) Take()
        {
            var taken = (Barcode, Brand, ProductName);
            Barcode = ""; Brand = ""; ProductName = "";
            return taken;
        }
    }
}
