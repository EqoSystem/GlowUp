using GlowUp.Theme;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// New21 colour guideline §9 (WCAG 2.2 AA): every text colour of the app palette reaches 4.5 : 1 on every surface it can
    /// sit on, in light and dark mode. Rose Mist (surfaceAlt) is the darkest light surface and was where status colours failed.
    /// </summary>
    public class PaletteContrastTests
    {
        static float Lum(Color c)
        {
            float F(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * F(c.r) + 0.7152f * F(c.g) + 0.0722f * F(c.b);
        }

        static float Ratio(Color a, Color b)
        {
            float x = Lum(a), y = Lum(b);
            return (Mathf.Max(x, y) + 0.05f) / (Mathf.Min(x, y) + 0.05f);
        }

        static void CheckPalette(string name, ThemeProfile.Palette p)
        {
            var surfaces = new (string, Color)[] { ("background", p.background), ("surface", p.surface), ("surfaceAlt", p.surfaceAlt), ("inputBackground", p.inputBackground) };
            var texts = new (string, Color)[]
            {
                ("heading", p.heading), ("mainText", p.mainText), ("secondaryText", p.secondaryText), ("inputText", p.inputText),
                ("inputPlaceholder", p.inputPlaceholder), ("success", p.success), ("caution", p.caution), ("warning", p.warning),
                ("accentText", p.accentText), ("info", p.info)
            };
            foreach (var (tn, tc) in texts)
                foreach (var (sn, sc) in surfaces)
                {
                    float r = Ratio(tc, sc);
                    Assert.GreaterOrEqual(r, 4.5f, $"{name}: {tn} on {sn} is {r:0.00} : 1 (needs 4.5)");
                }
            Assert.GreaterOrEqual(Ratio(p.buttonText, p.buttonFill), 4.5f, $"{name}: button label on the pink button");
        }

        [Test] public void LightPaletteMeetsAA() => CheckPalette("light", ThemeProfile.LightDefault());
        [Test] public void DarkPaletteMeetsAA() => CheckPalette("dark", ThemeProfile.DarkDefault());

        [Test]
        public void IconsAndLabelsOnStatusFillsAreReadable()
        {
            foreach (var p in new[] { ThemeProfile.LightDefault(), ThemeProfile.DarkDefault() })
                foreach (var fill in new[] { p.success, p.caution, p.warning, p.secondaryText })
                {
                    float r = ContrastInk.Ratio(ContrastInk.On(fill), fill);
                    Assert.GreaterOrEqual(r, 4.5f, $"ink on status fill #{ColorUtility.ToHtmlStringRGB(fill)} is {r:0.00} : 1");
                }
        }

        [Test]
        public void DarkModeIsCharcoalNotPureBlack()
        {
            var d = ThemeProfile.DarkDefault();
            Assert.Greater(Lum(d.background), 0.005f, "dark background is a deep charcoal, not pure black");
            Assert.Greater(Lum(d.surface), Lum(d.background), "cards sit one step above the background");
        }
    }
}
