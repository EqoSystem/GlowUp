using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// Which texture (finish) AR Try-On starts with, and where that choice came from (panel revision, 22 Nov 2026).
    /// Order of trust: what the scan read from the product, then the texture the user last chose themselves, then a guess from the kind
    /// of product (gloss = glossy, foundation or concealer = matte), then the default. The screen says which one it used, so the
    /// texture is never a silent guess. Pure logic, tested in Editor/Tests/ArTexturePreferenceTests.cs.
    /// </summary>
    public static class ArTexturePreference
    {
        public enum Source { Product, Preference, Kind, Default }

        const string Key = "GlowUp.Ar.PreferredFinish";

        /// <summary>The finish the user chose last by tapping a texture tab ("" = none yet).</summary>
        public static string Preferred
        {
            get { try { return PlayerPrefs.GetString(Key, ""); } catch (System.Exception) { return ""; } }
            set { try { PlayerPrefs.SetString(Key, value ?? ""); PlayerPrefs.Save(); } catch (System.Exception) { } }
        }

        public static string Choose(string productFinish, string cosmeticType, string[] names, string defaultFinish, out Source source)
            => Choose(productFinish, Preferred, cosmeticType, names, defaultFinish, out source);

        public static string Choose(string productFinish, string preferred, string cosmeticType, string[] names, string defaultFinish, out Source source)
        {
            names = names ?? new string[0];
            if (System.Array.IndexOf(names, productFinish ?? "") >= 0) { source = Source.Product; return productFinish; }
            if (System.Array.IndexOf(names, preferred ?? "") >= 0) { source = Source.Preference; return preferred; }
            string type = (cosmeticType ?? "").ToLowerInvariant();
            string guess = type.Contains("gloss") ? "glossy" : type.Contains("foundation") || type.Contains("concealer") ? "matte" : "";
            if (guess.Length > 0 && System.Array.IndexOf(names, guess) >= 0) { source = Source.Kind; return guess; }
            source = Source.Default;
            return defaultFinish;
        }

        public static string SourceLine(Source source)
        {
            switch (source)
            {
                case Source.Product: return "Texture: read from this product when it was scanned. Tap another to try it.";
                case Source.Preference: return "Texture: the one you chose last time. Tap another to change it.";
                case Source.Kind: return "Texture: a guess from the kind of product, because the scan did not tell. Tap another to try it.";
                default: return "Texture: a standard starting point, because the scan did not tell. Tap another to try it.";
            }
        }
    }
}
