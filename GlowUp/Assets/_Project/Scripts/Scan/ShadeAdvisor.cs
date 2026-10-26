using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// Colour-analysis style shade suggestions: for each skin tone chosen at onboarding
    /// (Fair, Light, Medium, Tan, Deep, Dark) and each area of the face, six shades that
    /// usually flatter that depth of skin. Edit the hex codes below to change the suggestions.
    /// Areas: Lips, Cheeks, Eyes, Face.
    /// </summary>
    public static class ShadeAdvisor
    {
        public const string Lips = "Lips", Cheeks = "Cheeks", Eyes = "Eyes", Face = "Face", Brows = "Brows";
        /// <summary>The parts of the face, in the order of the AR Try-On tabs. Brows can only be shown by live face tracking.</summary>
        public static readonly string[] Areas = { Lips, Cheeks, Eyes, Face, Brows };
        static readonly string[] Tones = { "Fair", "Light", "Medium", "Tan", "Deep", "Dark" };

        // the skin-tone colours used on the onboarding tiles (also the base for foundation suggestions)
        static readonly string[] ToneHex = { "#F7F4E2", "#F2DEC9", "#D4A86F", "#B69772", "#9D855D", "#5F453B" };

        static readonly string[][] LipShades =
        {
            new[] { "#F2A7B1", "#E98A9B", "#F5A38C", "#E0607E", "#C24B62", "#B33F58" },   // Fair: soft pinks, peach, raspberry
            new[] { "#EE9CA7", "#E57C8C", "#F08F7A", "#D45D79", "#C0465E", "#A63A50" },   // Light
            new[] { "#E58B8B", "#D96C6C", "#E2765A", "#C44D58", "#A8384B", "#8E2F3C" },   // Medium: warm rose, terracotta, berry
            new[] { "#DB7F6E", "#CF6A5A", "#C85A46", "#B5463F", "#9C3B45", "#7E2A33" },   // Tan
            new[] { "#C96F5E", "#B9523F", "#A8403A", "#8E2F3A", "#7A2440", "#5E1F2E" },   // Deep: brick, wine
            new[] { "#B85C4A", "#A1402F", "#8C2E2E", "#7B2338", "#5F1C33", "#4A1626" }    // Dark: deep red, plum
        };

        static readonly string[][] CheekShades =
        {
            new[] { "#F7B6C2", "#F4A9A8", "#F9C1B1", "#EFA3B5", "#F2B5A0", "#E995A6" },
            new[] { "#F2A0AF", "#F09C8C", "#F5B49C", "#E88CA0", "#EE9F86", "#DE7F92" },
            new[] { "#E88B8B", "#E8826E", "#EE9A77", "#D9707F", "#E07B5F", "#C96472" },
            new[] { "#DE7A6B", "#D96B50", "#E08A60", "#C85C66", "#CF6645", "#B4505C" },
            new[] { "#CC6652", "#C2553E", "#D07848", "#B0485A", "#B85234", "#94384A" },
            new[] { "#B9523F", "#A8402E", "#C06A3A", "#96324A", "#9E3F28", "#7A2640" }
        };

        static readonly string[][] EyeShades =
        {
            new[] { "#E9D3C0", "#D8B49C", "#C9A0A8", "#B58A72", "#A7B5A0", "#8E7A8C" },   // champagne, taupe, mauve, soft bronze, sage, plum-grey
            new[] { "#E6CDB5", "#D2A98C", "#C4959F", "#B07E5E", "#9DAA8E", "#84708A" },
            new[] { "#E0BE98", "#C99A6C", "#B88078", "#9C6A45", "#7F8F6B", "#74587A" },   // gold, bronze, rose-brown, copper, olive, plum
            new[] { "#D9B07E", "#C08A52", "#A96A55", "#8E5B36", "#6F7F55", "#68486E" },
            new[] { "#CFA066", "#B57C3E", "#9A5540", "#7C4A2A", "#5F6F45", "#5A3A66" },
            new[] { "#C99A52", "#AD7430", "#8F4A35", "#6E3F22", "#4F5F3A", "#4C2E5C" }
        };

        // brows: taupe and ash on fair skin, deeper browns to soft black on deep skin (cool, neutral and warm in each row)
        static readonly string[][] BrowShades =
        {
            new[] { "#A08872", "#8E7660", "#9C8068", "#7A6250", "#6E5A4C", "#5A4A40" },
            new[] { "#8E7460", "#7C624E", "#86684F", "#6C5444", "#5E4A3E", "#4E3E35" },
            new[] { "#7A5E48", "#6A503D", "#745438", "#5A4434", "#4E3B2F", "#3E3029" },
            new[] { "#66503E", "#5A4433", "#604430", "#4C392C", "#3F3027", "#33281F" },
            new[] { "#4E3C30", "#433328", "#4A3326", "#382B22", "#2E241D", "#241C17" },
            new[] { "#3A2D25", "#30251E", "#352419", "#271E18", "#1F1813", "#161210" }
        };

        /// <summary>Which area of the face a product type is applied to.</summary>
        public static string AreaFor(string cosmeticType)
        {
            string t = (cosmeticType ?? "").ToLowerInvariant();
            if (t.Contains("lip")) return Lips;
            if (t.Contains("brow")) return Brows;              // before "eye": an eyebrow pencil is a brow product
            if (t.Contains("blush") || t.Contains("bronzer") || t.Contains("highlighter") || t.Contains("contour")) return Cheeks;
            if (t.Contains("eye") || t.Contains("mascara")) return Eyes;
            if (t.Contains("foundation") || t.Contains("concealer") || t.Contains("powder") || t.Contains("primer") || t.Contains("setting")) return Face;
            return Lips;        // a kind with no part of its own; lips are the default preview
        }

        /// <summary>The colour a face powder is shown in: white. Its box or its beige look in the pan says nothing about how it looks on skin.</summary>
        public const string PowderShade = "#FFFFFF";

        /// <summary>True for a loose or pressed face powder (not a powder blush, bronzer, eyeshadow, highlighter or foundation).</summary>
        public static bool IsFacePowder(string cosmeticType)
        {
            string t = (cosmeticType ?? "").ToLowerInvariant();
            return t.Contains("powder") && !t.Contains("blush") && !t.Contains("bronzer") && !t.Contains("eye") && !t.Contains("highlighter")
                && !t.Contains("foundation") && !t.Contains("brow") && !t.Contains("contour");
        }

        /// <summary>The shade to use for a product: white for a face powder, otherwise the colour that was found.</summary>
        public static string ShadeFor(string cosmeticType, string foundHex) => IsFacePowder(cosmeticType) ? PowderShade : (foundHex ?? "");

        /// <summary>True when the product type is a colour cosmetic (something AR Try-On can show).</summary>
        public static bool IsColourCosmetic(string cosmeticType)
        {
            string t = (cosmeticType ?? "").ToLowerInvariant();
            return t.Contains("lip") || t.Contains("blush") || t.Contains("bronzer") || t.Contains("highlighter") || t.Contains("eye")
                || t.Contains("mascara") || t.Contains("foundation") || t.Contains("concealer") || t.Contains("powder");
        }

        /// <summary>Six suggested shades (hex) for a skin tone and area.</summary>
        public static List<string> Recommend(string skinTone, string area)
        {
            int i = Array.FindIndex(Tones, x => string.Equals(x, (skinTone ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0) i = 2;                                    // no tone saved: use the middle of the range
            if (area == Cheeks) return new List<string>(CheekShades[i]);
            if (area == Eyes) return new List<string>(EyeShades[i]);
            if (area == Brows) return new List<string>(BrowShades[i]);
            if (area == Face) return FoundationShades(i);
            return new List<string>(LipShades[i]);
        }

        /// <summary>Foundation: the skin tone itself plus slightly lighter / deeper and warmer / cooler variants.</summary>
        static List<string> FoundationShades(int toneIndex)
        {
            var list = new List<string>();
            if (!ColorUtility.TryParseHtmlString(ToneHex[toneIndex], out Color baseColor)) baseColor = new Color(0.83f, 0.66f, 0.44f);
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            (float dh, float ds, float dv)[] variants =
            {
                (0f, 0f, 0f), (0f, -0.04f, 0.05f), (0f, 0.05f, -0.06f), (0.012f, 0.05f, 0f), (-0.012f, -0.03f, 0f), (0.008f, 0.08f, -0.10f)
            };
            foreach (var (dh, ds, dv) in variants)
            {
                Color c = Color.HSVToRGB(Mathf.Repeat(h + dh, 1f), Mathf.Clamp01(s + ds), Mathf.Clamp01(v + dv));
                list.Add("#" + ColorUtility.ToHtmlStringRGB(c));
            }
            return list;
        }

        public static Color Parse(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            string h = hex.Trim(); if (!h.StartsWith("#")) h = "#" + h;
            return ColorUtility.TryParseHtmlString(h, out var c) ? c : fallback;
        }
    }
}
