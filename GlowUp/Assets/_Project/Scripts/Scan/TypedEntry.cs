namespace GlowUp.Scan
{
    /// <summary>
    /// WHAT DID THE USER TYPE? The scan screen's "Type it in" pop-up has one big box. People put different things in it:
    /// an ingredient list copied from a pack, a few ingredients they worry about, one single ingredient, or (most often
    /// when they do not have the pack at hand) the PRODUCT'S NAME. The app must not answer all of these with "type at
    /// least two ingredients": it has to understand which it is.
    ///
    ///  - Ingredients: at least one ingredient the app knows, or two or more names in the shape of a list. They are
    ///    analysed as they are. (One known ingredient alone can be checked too: "is fragrance all right for me?")
    ///  - Product: a line of words that names no ingredient ("Maybelline Fit Me foundation", "careline blush on"). Its
    ///    ingredient list is then looked up: in the app's own product list, then on the web.
    ///  - Nothing: the box is empty.
    ///
    /// Pure logic: no scene, no internet. It uses the app's own ingredient reader (IngredientTextParser).
    /// </summary>
    public static class TypedEntry
    {
        public enum Kind { Nothing, Ingredients, Product }

        public class Reading
        {
            public Kind kind = Kind.Nothing;
            /// <summary>What the ingredient reader made of the text (also when it turned out to be a product).</summary>
            public IngredientTextParser.ParsedList list = new IngredientTextParser.ParsedList();
            /// <summary>For Kind.Product: the product as typed, in one line.</summary>
            public string product = "";
        }

        /// <summary>The longest text that is still taken for a product's name (a longer one without any ingredient is not understood).</summary>
        public static int LongestProductName = 140;

        public static Reading Understand(string text)
        {
            var reading = new Reading();
            string typed = (text ?? "").Trim();
            if (typed.Length == 0) return reading;
            reading.list = IngredientTextParser.Read(typed);

            // an ingredient the app knows is in it, or it is a list of two or more names: ingredients
            if (reading.list.ingredients.Count >= 1 || reading.list.Count >= 2) { reading.kind = Kind.Ingredients; return reading; }

            // no ingredient in it. One or two short lines of words name a product.
            string oneLine = System.Text.RegularExpressions.Regex.Replace(typed, @"\s+", " ").Trim();
            if (oneLine.Length >= 2 && oneLine.Length <= LongestProductName) { reading.kind = Kind.Product; reading.product = oneLine; }
            return reading;
        }
    }
}
