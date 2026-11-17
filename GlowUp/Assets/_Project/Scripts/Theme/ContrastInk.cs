using UnityEngine;

namespace GlowUp.Theme
{
    /// <summary>
    /// New21 colour guideline §9: an icon or a label drawn on a coloured fill (status disc, pill, badge) uses whichever of white
    /// or dark ink reads better on that fill, measured by the WCAG contrast ratio. A fixed white icon fails on the lighter
    /// dark-mode status colours (2.1 to 2.6 : 1, under the 3 : 1 an indicator needs).
    /// </summary>
    public static class ContrastInk
    {
        /// <summary>Rose Cocoa night: the dark ink used on light fills.</summary>
        public static readonly Color Dark = new Color32(33, 29, 32, 255);

        public static float Luminance(Color c)
        {
            float F(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * F(c.r) + 0.7152f * F(c.g) + 0.0722f * F(c.b);
        }

        public static float Ratio(Color a, Color b)
        {
            float x = Luminance(a), y = Luminance(b);
            return (Mathf.Max(x, y) + 0.05f) / (Mathf.Min(x, y) + 0.05f);
        }

        /// <summary>White or <paramref name="dark"/> (default <see cref="Dark"/>), whichever has the higher contrast on <paramref name="fill"/>.</summary>
        public static Color On(Color fill, Color? dark = null)
        {
            Color d = dark ?? Dark;
            return Ratio(Color.white, fill) >= Ratio(d, fill) ? Color.white : d;
        }
    }
}
