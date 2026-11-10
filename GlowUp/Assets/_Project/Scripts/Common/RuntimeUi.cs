using System;
using System.Collections.Generic;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Small pictures drawn by code at run time (7 November 2026): the status-message icons, the spinner arc, the up arrow
    /// and a rounded box. The Editor's IconFactory cannot be used in a build, and these parts are added to screens built
    /// earlier without a scene rebuild. Each picture is drawn once and kept.
    /// </summary>
    public static class RuntimeIcons
    {
        static readonly Dictionary<string, Sprite> made = new Dictionary<string, Sprite>();
        const int Size = 96;

        public static Sprite Warning => Get("warning", (x, y) =>
            TriangleOutline(x, y) || (Mathf.Abs(x) < 0.07f && y > -0.18f && y < 0.32f) || Dot(x, y, 0f, -0.36f, 0.075f));
        public static Sprite Info => Get("info", (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return (r < 0.9f && r > 0.76f) || Dot(x, y, 0f, 0.38f, 0.08f) || (Mathf.Abs(x) < 0.07f && y > -0.45f && y < 0.18f);
        });
        public static Sprite Check => Get("check", (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return (r < 0.9f && r > 0.76f) || Segment(x, y, -0.4f, 0f, -0.1f, -0.3f, 0.08f) || Segment(x, y, -0.1f, -0.3f, 0.42f, 0.28f, 0.08f);
        });
        public static Sprite ArrowUp => Get("arrow_up", (x, y) =>
            Segment(x, y, 0f, -0.6f, 0f, 0.55f, 0.1f) || Segment(x, y, 0f, 0.6f, -0.45f, 0.15f, 0.1f) || Segment(x, y, 0f, 0.6f, 0.45f, 0.15f, 0.1f));
        /// <summary>Three quarters of a ring: turned by <see cref="Spinner"/>.</summary>
        public static Sprite SpinnerArc => Get("spinner", (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 0.86f || r < 0.62f) return false;
            float angle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            return angle < 0f || angle > 80f;
        });
        public static Sprite Circle => Get("circle", (x, y) => x * x + y * y < 0.98f);
        /// <summary>A tick alone (no ring), drawn left to right: Image.Type.Filled with Horizontal fill "draws" it.</summary>
        public static Sprite Tick => Get("tick", (x, y) => Segment(x, y, -0.5f, 0.02f, -0.14f, -0.34f, 0.11f) || Segment(x, y, -0.14f, -0.34f, 0.52f, 0.32f, 0.11f));
        /// <summary>A thin ring.</summary>
        public static Sprite Ring => Get("ring", (x, y) => { float r = x * x + y * y; return r < 0.96f && r > 0.72f; });
        /// <summary>Four corner brackets and a line across: the live scan icon.</summary>
        public static Sprite ScanFrame => Get("scan_frame", (x, y) =>
        {
            const float a = 0.8f, len = 0.32f, h = 0.075f;
            bool corner = false;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                    corner |= Segment(x, y, sx * a, sy * a, sx * (a - len), sy * a, h) || Segment(x, y, sx * a, sy * a, sx * a, sy * (a - len), h);
            return corner || Segment(x, y, -0.5f, 0f, 0.5f, 0f, h);
        });

        /// <summary>A rounded box for 9-slicing (Image.Type.Sliced).</summary>
        public static Sprite RoundedBox
        {
            get
            {
                if (made.TryGetValue("box", out Sprite s) && s != null) return s;
                const int n = 64, radius = 24;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, radius, n - radius), cy = Mathf.Clamp(y + 0.5f, radius, n - radius);
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        byte a = (byte)(Mathf.Clamp01(radius + 0.5f - d) * 255f);
                        px[y * n + x] = new Color32(255, 255, 255, a);
                    }
                tex.SetPixels32(px); tex.Apply(false, true);
                s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                s.name = "runtime_rounded_box";
                made["box"] = s;
                return s;
            }
        }

        static Sprite Get(string name, Func<float, float, bool> inside)
        {
            if (made.TryGetValue(name, out Sprite s) && s != null) return s;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int hits = 0;                                   // 4 x 4 samples per pixel: soft edges
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float u = (x + (sx + 0.5f) / 4f) / Size * 2f - 1f, v = (y + (sy + 0.5f) / 4f) / Size * 2f - 1f;
                            if (inside(u, v)) hits++;
                        }
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 16));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            s.name = "runtime_icon_" + name;
            made[name] = s;
            return s;
        }

        static bool Dot(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) < r * r;

        static bool Segment(float x, float y, float ax, float ay, float bx, float by, float half)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            float px = ax + t * dx - x, py = ay + t * dy - y;
            return px * px + py * py < half * half;
        }

        static bool TriangleOutline(float x, float y)
        {
            const float h = 0.07f;
            return Segment(x, y, 0f, 0.78f, -0.86f, -0.68f, h) || Segment(x, y, 0f, 0.78f, 0.86f, -0.68f, h) || Segment(x, y, -0.86f, -0.68f, 0.86f, -0.68f, h);
        }
    }

    /// <summary>The theme's colours and fonts at run time, with safe values when a scene has no ThemeManager.</summary>
    public static class RuntimeTheme
    {
        /// <summary>Editor previews only (RuntimeUiGallery): the palette and fonts to draw with when no ThemeManager runs.</summary>
        public static ThemeProfile.Palette PreviewPalette;
        public static bool PreviewDark;
        public static ThemeProfile PreviewProfile;

        public static ThemeProfile.Palette Palette
        {
            get
            {
                if (PreviewPalette != null) return PreviewPalette;
                if (ThemeManager.Instance != null) return ThemeManager.Instance.Palette;
                return new ThemeProfile.Palette
                {
                    background = new Color32(255, 255, 255, 255), surface = Color.white, surfaceAlt = new Color32(250, 240, 242, 255),
                    heading = new Color32(43, 36, 38, 255), mainText = new Color32(43, 36, 38, 255), secondaryText = new Color32(110, 98, 101, 255),
                    buttonFill = new Color32(201, 69, 109, 255), buttonText = Color.white, accent = new Color32(201, 69, 109, 255),
                    accentText = new Color32(176, 74, 105, 255), warning = new Color32(194, 58, 58, 255), caution = new Color32(154, 106, 8, 255),
                    success = new Color32(31, 138, 95, 255)
                };
            }
        }

        public static bool IsDark => PreviewPalette != null ? PreviewDark : ThemeManager.Instance != null && ThemeManager.Instance.IsDark;

        public static TMP_FontAsset Font(FontRole role, TMP_Text fallback = null)
        {
            TMP_FontAsset font = PreviewProfile != null ? PreviewProfile.GetFont(role) : ThemeManager.Instance != null && ThemeManager.Instance.Profile != null ? ThemeManager.Instance.Profile.GetFont(role) : null;
            return font != null ? font : fallback != null ? fallback.font : TMP_Settings.defaultFontAsset;
        }

        /// <summary>`over` laid on `under` at `amount` (0 to 1), fully opaque.</summary>
        public static Color Mix(Color under, Color over, float amount)
        {
            Color c = Color.Lerp(under, over, amount); c.a = 1f; return c;
        }
    }

    /// <summary>Little builders for the parts added at run time.</summary>
    public static class RuntimeUiKit
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
        }

        public static Image Box(Transform parent, string name, Color color, bool rounded = true)
        {
            RectTransform rt = Rect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            if (rounded) { image.sprite = RuntimeIcons.RoundedBox; image.type = Image.Type.Sliced; }
            image.color = color;
            return image;
        }

        public static Image Icon(Transform parent, string name, Sprite sprite, Color color, float size)
        {
            RectTransform rt = Rect(name, parent);
            rt.sizeDelta = new Vector2(size, size);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite; image.color = color; image.raycastTarget = false; image.preserveAspect = true;
            return image;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string words, float size, Color color, TMP_FontAsset font, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            RectTransform rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = words; t.fontSize = size; t.color = color; t.alignment = align;
            t.enableWordWrapping = true; t.overflowMode = TextOverflowModes.Overflow; t.raycastTarget = false;
            return t;
        }

        /// <summary>A pill button with a label (filled, or outlined when `outlined`). At least 132 x 132 units = 44 dp at GlowUp's 1080-wide layout.</summary>
        public static Button Pill(Transform parent, string name, string words, Color fill, Color text, TMP_FontAsset font, bool outlined = false)
        {
            Image back = Box(parent, name, outlined ? RuntimeTheme.Palette.surface : fill);
            back.raycastTarget = true;
            var button = back.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            if (outlined)
            {
                var outline = back.gameObject.AddComponent<Outline>();
                outline.effectColor = fill; outline.effectDistance = new Vector2(2f, -2f);
            }
            TextMeshProUGUI label = Label(back.transform, "Label", words, 40, text, font);
            label.enableWordWrapping = false; label.fontStyle = FontStyles.Bold;
            Stretch(label.rectTransform, 24, 24, 0, 0);
            var element = back.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 132; element.preferredHeight = 132; element.flexibleWidth = 1;
            return button;
        }
    }
}
