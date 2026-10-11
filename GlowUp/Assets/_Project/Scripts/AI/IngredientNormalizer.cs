using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public static class IngredientNormalizer
{
    // Dictionary mapping known aliases/synonyms to a single canonical name
    private static readonly Dictionary<string, string> AliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "parfum", "Fragrance" },
        { "perfume", "Fragrance" },
        { "aroma", "Fragrance" },
        { "flavor", "Fragrance" },
        { "aqua", "Water" },
        { "purified water", "Water" },
        { "h2o", "Water" },
        { "vit b3", "Niacinamide" },
        { "nicotinamide", "Niacinamide" }
    };

    /// <summary>
    /// Cleans and normalizes a raw OCR ingredient string.
    /// </summary>
    public static string Normalize(string rawIngredient)
    {
        if (string.IsNullOrWhiteSpace(rawIngredient))
            return string.Empty;

        // 1. Convert to lowercase for matching, strip extra punctuation/whitespace
        string cleaned = rawIngredient.Trim();
        cleaned = Regex.Replace(cleaned, @"[^\w\s-]", ""); // Keep letters, numbers, spaces, hyphens
        cleaned = Regex.Replace(cleaned, @"\s+", " ");     // Collapse multiple spaces

        // 2. Check alias mapping for canonical conversion
        if (AliasMap.TryGetValue(cleaned, out string canonicalName))
        {
            return canonicalName;
        }

        // 3. Default to Title Case format if no alias is found
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLower());
    }

    /// <label>
    /// Parses a raw comma- or semicolon-separated OCR block into a list of unique normalized ingredients.
    /// </label>
    public static List<string> ParseAndNormalizeBatch(string rawOcrText)
    {
        var normalizedList = new List<string>();
        if (string.IsNullOrEmpty(rawOcrText)) return normalizedList;

        // Split by common ingredient list separators (comma, semicolon, newline)
        char[] separators = { ',', ';', '\n', '\r' };
        string[] rawItems = rawOcrText.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var item in rawItems)
        {
            string canonical = Normalize(item);
            if (!string.IsNullOrEmpty(canonical) && !normalizedList.Contains(canonical))
            {
                normalizedList.Add(canonical);
            }
        }

        return normalizedList;
    }
}