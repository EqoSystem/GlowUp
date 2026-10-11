using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Generates simple white UI icons (eye, closed eye, moon, sun) as PNG sprites so the UI
    /// doesn't depend on font glyphs. Files are created once; replace them with your own art
    /// any time (same file name) and every reference keeps working.
    /// </summary>
    public static class IconFactory
    {
        const string Folder = "Assets/_Project/Sprites/Icons/";

        public static Sprite EyeOpen => Get("icon_eye_open", (x, y) => EyeShape(x, y, false));
        public static Sprite EyeClosed => Get("icon_eye_closed", (x, y) => EyeShape(x, y, true));
        public static Sprite Moon => Get("icon_moon_line", (x, y) =>
            (Ring(x, y, 0.72f, 0.08f) && !Disc(x - 0.44f, y - 0.32f, 0.7f)) || (Ring(x - 0.44f, y - 0.32f, 0.7f, 0.08f) && Disc(x, y, 0.76f)));
        public static Sprite Sun => Get("icon_sun_line", (x, y) => Ring(x, y, 0.32f, 0.08f) || Rays(x, y, 0.56f, 0.86f, 0.07f));

        static bool EyeShape(float x, float y, bool slash)
        {
            float edge = 0.55f * (1 - x * x);
            bool outline = Mathf.Abs(x) < 1 && Mathf.Abs(y) <= edge && !(Mathf.Abs(y) <= edge - 0.11f);
            bool pupil = x * x + y * y < 0.065f;
            bool line = slash && Mathf.Abs(x + y) / 1.4142f < 0.075f && Mathf.Abs(x) < 0.85f;
            return outline || pupil || line;
        }

        static bool Moon_(float x, float y)
        {
            bool outer = x * x + y * y < 0.62f * 0.62f * 1.6f && x * x + y * y < 0.5f;
            bool cut = (x - 0.32f) * (x - 0.32f) + (y - 0.2f) * (y - 0.2f) < 0.36f * 0.9f;
            return outer && !cut;
        }

        static bool Sun_(float x, float y)
        {
            float r2 = x * x + y * y;
            if (r2 < 0.16f) return true;
            float r = Mathf.Sqrt(r2);
            float a = Mathf.Atan2(y, x);
            return r > 0.55f && r < 0.88f && Mathf.Cos(a * 8f) > 0.9f;
        }

        public static Sprite Back => Get("icon_back", (x, y) => Stroke(x, y, 0.14f, new[] { 0.3f, 0.7f, -0.3f, 0f, 0.3f, -0.7f }));
        public static Sprite Arrow => Get("icon_arrow", (x, y) => Stroke(x, y, 0.13f, new[] { -0.7f, 0f, 0.65f, 0f }) || Stroke(x, y, 0.13f, new[] { 0.15f, 0.55f, 0.65f, 0f, 0.15f, -0.55f }));
        public static Sprite Check => Get("icon_check", (x, y) => Stroke(x, y, 0.15f, new[] { -0.65f, 0f, -0.2f, -0.5f, 0.65f, 0.5f }));
        public static Sprite Close => Get("icon_close", (x, y) => Stroke(x, y, 0.13f, new[] { -0.55f, 0.55f, 0.55f, -0.55f }) || Stroke(x, y, 0.13f, new[] { -0.55f, -0.55f, 0.55f, 0.55f }));
        public static Sprite Info => Get("icon_info", (x, y) => { float r = Mathf.Sqrt(x * x + y * y); bool ring = r < 0.9f && r > 0.75f; bool dot = (x * x + (y - 0.4f) * (y - 0.4f)) < 0.01f; bool bar = Mathf.Abs(x) < 0.07f && y > -0.45f && y < 0.2f; return ring || dot || bar; });

        /// <summary>True when (x,y) lies within half-thickness t of a polyline given as x0,y0,x1,y1,...</summary>
        static bool Stroke(float x, float y, float t, float[] pts)
        {
            for (int i = 0; i + 3 < pts.Length; i += 2)
            {
                float ax = pts[i], ay = pts[i + 1], bx = pts[i + 2], by = pts[i + 3];
                float dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
                float k = l2 > 0 ? Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / l2) : 0;
                float px = ax + k * dx - x, py = ay + k * dy - y;
                if (px * px + py * py < t * t) return true;
            }
            return false;
        }

        /// <summary>
        /// Fully rounded 9-sliced rectangle (corner radius 64 px, 4 px stretchy centre). Used by every
        /// button, input, tile and card; PillCorners rescales the corners to the object's height.
        /// </summary>
        public static Sprite Pill
        {
            get
            {
                const string name = "ui_pill";
                string path = Folder + name + ".png";
                var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (existing != null) return existing;

                Directory.CreateDirectory(Folder);
                const int N = 132; const float r = 64f, half = 66f;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float px = Mathf.Abs(x + 0.5f - N / 2f) - (half - r), py = Mathf.Abs(y + 0.5f - N / 2f) - (half - r);
                        float d = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude + Mathf.Min(Mathf.Max(px, py), 0) - r;
                        tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(0.5f - d)));
                    }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.filterMode = FilterMode.Bilinear;
                imp.spriteBorder = new Vector4(64, 64, 64, 64);
                imp.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
        }

        public static Sprite ChevronDown => Get("icon_chevron_down", (x, y) => Stroke(x, y, 0.13f, new[] { -0.6f, 0.3f, 0f, -0.3f, 0.6f, 0.3f }));

        // ---------------------------------------------------------------------------------------
        //  App icons (white; tinted by the Image colour). Coordinates run from -1 to 1, y up.
        // ---------------------------------------------------------------------------------------
        static bool Disc(float x, float y, float r) => x * x + y * y < r * r;
        /// <summary>Eight short rays round the middle, from radius `from` to `to`, each `width` wide (a sun's rays, a gear's teeth).</summary>
        static bool Rays(float x, float y, float from, float to, float width)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r < from || r > to) return false;
            float a = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(a, Mathf.Round(a / 45f) * 45f)) * Mathf.Deg2Rad * r < width;
        }
        static bool Ring(float x, float y, float r, float half) => Mathf.Abs(Mathf.Sqrt(x * x + y * y) - r) < half;
        static bool Oval(float x, float y, float rx, float ry) => (x * x) / (rx * rx) + (y * y) / (ry * ry) < 1f;
        static bool Box(float x, float y, float cx, float cy, float halfW, float halfH) => Mathf.Abs(x - cx) < halfW && Mathf.Abs(y - cy) < halfH;
        static bool Star(float x, float y, float size) => Mathf.Pow(Mathf.Abs(x) / size, 0.667f) + Mathf.Pow(Mathf.Abs(y) / size, 0.667f) < 1f;

        public static Sprite History => Get("icon_history", (x, y) => Ring(x, y, 0.80f, 0.085f) || Stroke(x, y, 0.08f, new[] { 0f, 0.48f, 0f, 0f, 0.36f, -0.22f }));
        public static Sprite Scan => Get("icon_scan", (x, y) =>
            Stroke(x, y, 0.09f, new[] { -0.8f, 0.4f, -0.8f, 0.8f, -0.4f, 0.8f }) || Stroke(x, y, 0.09f, new[] { 0.4f, 0.8f, 0.8f, 0.8f, 0.8f, 0.4f }) ||
            Stroke(x, y, 0.09f, new[] { 0.8f, -0.4f, 0.8f, -0.8f, 0.4f, -0.8f }) || Stroke(x, y, 0.09f, new[] { -0.4f, -0.8f, -0.8f, -0.8f, -0.8f, -0.4f }) ||
            Stroke(x, y, 0.07f, new[] { -0.5f, 0f, 0.5f, 0f }));
        public static Sprite Sparkle => Get("icon_sparkle", (x, y) => Star(x + 0.14f, y + 0.14f, 0.78f) || Star(x - 0.62f, y - 0.62f, 0.32f));
        /// <summary>One four-point star (the twinkles round the Skin Profile icon).</summary>
        public static Sprite Star4 => Get("icon_star4", (x, y) => Star(x, y, 0.95f));
        public static Sprite User => Get("icon_user_line", (x, y) =>
            Ring(x, y - 0.4f, 0.3f, 0.08f) || (Ring(x, y + 0.88f, 0.76f, 0.08f) && y > -0.8f) || Stroke(x, y, 0.08f, new[] { -0.72f, -0.8f, 0.72f, -0.8f }));
        public static Sprite SignOut => Get("icon_sign_out", (x, y) =>
            Stroke(x, y, 0.08f, new[] { -0.05f, 0.75f, -0.7f, 0.75f, -0.7f, -0.75f, -0.05f, -0.75f }) ||
            Stroke(x, y, 0.08f, new[] { -0.25f, 0f, 0.78f, 0f }) || Stroke(x, y, 0.08f, new[] { 0.45f, 0.33f, 0.78f, 0f, 0.45f, -0.33f }));
        public static Sprite Trash => Get("icon_trash", (x, y) =>
            Stroke(x, y, 0.08f, new[] { -0.66f, 0.52f, 0.66f, 0.52f }) || Stroke(x, y, 0.08f, new[] { -0.22f, 0.74f, 0.22f, 0.74f }) ||
            Stroke(x, y, 0.08f, new[] { -0.5f, 0.5f, -0.4f, -0.78f, 0.4f, -0.78f, 0.5f, 0.5f }) ||
            Stroke(x, y, 0.055f, new[] { -0.15f, 0.24f, -0.15f, -0.5f }) || Stroke(x, y, 0.055f, new[] { 0.15f, 0.24f, 0.15f, -0.5f }));
        public static Sprite Pencil => Get("icon_pencil", (x, y) =>
            Stroke(x, y, 0.16f, new[] { -0.38f, -0.38f, 0.58f, 0.58f }) || Stroke(x, y, 0.07f, new[] { -0.78f, -0.78f, -0.5f, -0.5f }) ||
            Stroke(x, y, 0.06f, new[] { -0.78f, -0.78f, -0.3f, -0.72f }) || Stroke(x, y, 0.06f, new[] { -0.78f, -0.78f, -0.72f, -0.3f }));
        public static Sprite ListIcon => Get("icon_list", (x, y) =>
            Disc(x + 0.68f, y - 0.5f, 0.11f) || Disc(x + 0.68f, y, 0.11f) || Disc(x + 0.68f, y + 0.5f, 0.11f) ||
            Stroke(x, y, 0.08f, new[] { -0.36f, 0.5f, 0.78f, 0.5f }) || Stroke(x, y, 0.08f, new[] { -0.36f, 0f, 0.78f, 0f }) || Stroke(x, y, 0.08f, new[] { -0.36f, -0.5f, 0.78f, -0.5f }));
        public static Sprite Warning => Get("icon_warning", (x, y) =>
            Stroke(x, y, 0.09f, new[] { 0f, 0.8f, -0.84f, -0.66f, 0.84f, -0.66f, 0f, 0.8f }) ||
            Stroke(x, y, 0.085f, new[] { 0f, 0.34f, 0f, -0.14f }) || Disc(x, y + 0.4f, 0.1f));
        public static Sprite Plus => Get("icon_plus", (x, y) => Stroke(x, y, 0.11f, new[] { -0.6f, 0f, 0.6f, 0f }) || Stroke(x, y, 0.11f, new[] { 0f, -0.6f, 0f, 0.6f }));
        public static Sprite Minus => Get("icon_minus", (x, y) => Stroke(x, y, 0.11f, new[] { -0.6f, 0f, 0.6f, 0f }));
        public static Sprite CameraIcon => Get("icon_camera", (x, y) =>
            Stroke(x, y, 0.08f, new[] { -0.8f, 0.42f, 0.8f, 0.42f, 0.8f, -0.58f, -0.8f, -0.58f, -0.8f, 0.42f }) ||
            Stroke(x, y, 0.08f, new[] { -0.28f, 0.42f, -0.16f, 0.66f, 0.16f, 0.66f, 0.28f, 0.42f }) || Ring(x, y + 0.08f, 0.27f, 0.075f));
        public static Sprite Swap => Get("icon_swap", (x, y) =>
            Stroke(x, y, 0.08f, new[] { -0.7f, 0.32f, 0.7f, 0.32f }) || Stroke(x, y, 0.08f, new[] { 0.4f, 0.62f, 0.7f, 0.32f, 0.4f, 0.02f }) ||
            Stroke(x, y, 0.08f, new[] { 0.7f, -0.32f, -0.7f, -0.32f }) || Stroke(x, y, 0.08f, new[] { -0.4f, -0.02f, -0.7f, -0.32f, -0.4f, -0.62f }));

        // ---- settings page ----
        public static Sprite Gear => Get("icon_gear_line", (x, y) => Ring(x, y, 0.24f, 0.075f) || Ring(x, y, 0.58f, 0.075f) || Rays(x, y, 0.6f, 0.86f, 0.13f));
        public static Sprite Bell => Get("icon_bell_line", (x, y) =>
            Stroke(x, y, 0.078f, new[] { -0.68f, -0.4f, -0.5f, -0.24f, -0.48f, 0.16f, -0.34f, 0.5f, 0f, 0.64f, 0.34f, 0.5f, 0.48f, 0.16f, 0.5f, -0.24f, 0.68f, -0.4f, -0.68f, -0.4f }) ||
            Stroke(x, y, 0.078f, new[] { 0f, 0.64f, 0f, 0.82f }) || (Ring(x, y + 0.56f, 0.17f, 0.07f) && y < -0.58f));
        public static Sprite Sync => Get("icon_sync", (x, y) =>
            (Ring(x, y, 0.6f, 0.085f) && Mathf.Abs(y) > 0.16f) ||
            Stroke(x, y, 0.085f, new[] { 0.34f, 0.3f, 0.6f, 0.02f, 0.86f, 0.3f }) || Stroke(x, y, 0.085f, new[] { -0.34f, -0.3f, -0.6f, -0.02f, -0.86f, -0.3f }));
        public static Sprite Shield => Get("icon_shield", (x, y) =>
            Stroke(x, y, 0.085f, new[] { 0f, 0.82f, 0.66f, 0.56f, 0.66f, 0f, 0.4f, -0.5f, 0f, -0.84f, -0.4f, -0.5f, -0.66f, 0f, -0.66f, 0.56f, 0f, 0.82f }) ||
            Stroke(x, y, 0.085f, new[] { -0.28f, 0.02f, -0.06f, -0.22f, 0.32f, 0.26f }));
        /// <summary>The shield without its check mark (the Settings tile adds the check as its own object, so it can pop in).</summary>
        public static Sprite ShieldPlain => Get("icon_shield_plain", (x, y) =>
            Stroke(x, y, 0.085f, new[] { 0f, 0.82f, 0.66f, 0.56f, 0.66f, 0f, 0.4f, -0.5f, 0f, -0.84f, -0.4f, -0.5f, -0.66f, 0f, -0.66f, 0.56f, 0f, 0.82f }));
        /// <summary>A loudspeaker with two sound waves (tap sounds).</summary>
        public static Sprite Speaker => Get("icon_speaker_line", (x, y) =>
            Stroke(x, y, 0.078f, new[] { -0.82f, 0.22f, -0.52f, 0.22f, -0.14f, 0.56f, -0.14f, -0.56f, -0.52f, -0.22f, -0.82f, -0.22f, -0.82f, 0.22f }) ||
            (Ring(x + 0.14f, y, 0.4f, 0.07f) && x > 0.12f) || (Ring(x + 0.14f, y, 0.74f, 0.07f) && x > 0.36f));
        /// <summary>A thin ring (an empty check box of the scanner's checklist).</summary>
        public static Sprite RingThin => Get("icon_ring_thin", (x, y) => Ring(x, y, 0.82f, 0.09f));
        /// <summary>A lightning bolt (the flashlight button).</summary>
        public static Sprite Bolt => Get("icon_bolt", (x, y) =>
            Stroke(x, y, 0.09f, new[] { 0.22f, 0.86f, -0.42f, -0.06f, 0.06f, -0.06f, -0.22f, -0.86f, 0.42f, 0.1f, -0.04f, 0.1f, 0.22f, 0.86f }));
        /// <summary>A screen split by a line (the before/after button of AR Try-On).</summary>
        public static Sprite Compare => Get("icon_compare_line", (x, y) =>
            Stroke(x, y, 0.078f, new[] { -0.8f, 0.56f, 0.8f, 0.56f, 0.8f, -0.56f, -0.8f, -0.56f, -0.8f, 0.56f }) || Stroke(x, y, 0.078f, new[] { 0f, 0.86f, 0f, -0.86f }) ||
            Stroke(x, y, 0.06f, new[] { 0.3f, 0.2f, 0.52f, 0.2f }) || Stroke(x, y, 0.06f, new[] { 0.3f, -0.04f, 0.52f, -0.04f }) || Stroke(x, y, 0.06f, new[] { 0.3f, -0.28f, 0.52f, -0.28f }));
        /// <summary>A light bulb (the face light of AR Try-On).</summary>
        public static Sprite Bulb => Get("icon_bulb", (x, y) =>
            (Ring(x, y - 0.2f, 0.56f, 0.085f) && y > -0.22f) || Stroke(x, y, 0.085f, new[] { -0.3f, -0.26f, -0.3f, -0.5f, 0.3f, -0.5f, 0.3f, -0.26f }) ||
            Stroke(x, y, 0.08f, new[] { -0.2f, -0.76f, 0.2f, -0.76f }));
        static readonly float[] GlitterDots =
        {
            -0.7f, 0.6f, 0.13f,   -0.2f, -0.5f, 0.09f,   0.4f, 0.3f, 0.15f,   0.75f, -0.7f, 0.1f,
            -0.55f, -0.1f, 0.08f,   0.1f, 0.8f, 0.09f,   0.6f, -0.2f, 0.07f,   -0.05f, 0.15f, 0.11f
        };
        /// <summary>A few soft dots that repeat as a pattern: the sparkle of a shimmer or metallic finish.</summary>
        public static Sprite Glitter => Shape("ui_glitter", 64, 64, (u, v) =>
        {
            float a = 0f;
            for (int i = 0; i < GlitterDots.Length; i += 3)
            {
                float dx = u - GlitterDots[i], dy = v - GlitterDots[i + 1];
                a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / GlitterDots[i + 2]));
            }
            return White(a * a);
        }, 0f, true);
        public static Sprite Mail => Get("icon_mail", (x, y) =>
            Stroke(x, y, 0.08f, new[] { -0.8f, 0.52f, 0.8f, 0.52f, 0.8f, -0.52f, -0.8f, -0.52f, -0.8f, 0.52f }) ||
            Stroke(x, y, 0.075f, new[] { -0.8f, 0.52f, 0f, -0.06f, 0.8f, 0.52f }));
        public static Sprite Gauge => Get("icon_gauge", (x, y) =>
            (Ring(x, y + 0.2f, 0.74f, 0.085f) && y > -0.24f) || Stroke(x, y, 0.075f, new[] { 0f, -0.2f, 0.4f, 0.28f }) || Disc(x, y + 0.2f, 0.13f) ||
            Stroke(x, y, 0.085f, new[] { -0.74f, -0.24f, 0.74f, -0.24f }));
        public static Sprite Document => Get("icon_document", (x, y) =>
            Stroke(x, y, 0.08f, new[] { -0.54f, 0.8f, 0.54f, 0.8f, 0.54f, -0.8f, -0.54f, -0.8f, -0.54f, 0.8f }) ||
            Stroke(x, y, 0.06f, new[] { -0.28f, 0.38f, 0.28f, 0.38f }) || Stroke(x, y, 0.06f, new[] { -0.28f, 0.04f, 0.28f, 0.04f }) || Stroke(x, y, 0.06f, new[] { -0.28f, -0.3f, 0.1f, -0.3f }));
        /// <summary>The mark on every "AI" button: a ring with a four-point spark inside.</summary>
        public static Sprite Ai => Get("icon_ai", (x, y) => Ring(x, y, 0.84f, 0.07f) || Star(x, y, 0.5f));
        public static Sprite ChevronRight => Get("icon_chevron_right", (x, y) => Stroke(x, y, 0.13f, new[] { -0.3f, 0.6f, 0.3f, 0f, -0.3f, -0.6f }));

        // ---- cosmetic types ----
        public static Sprite Lipstick => Get("icon_lipstick_line", (x, y) =>
            Stroke(x, y, 0.075f, new[] { -0.32f, -0.84f, 0.32f, -0.84f, 0.32f, -0.14f, -0.32f, -0.14f, -0.32f, -0.84f }) ||
            Stroke(x, y, 0.075f, new[] { -0.22f, -0.14f, -0.22f, 0.2f, 0.22f, 0.2f, 0.22f, -0.14f }) ||
            Stroke(x, y, 0.075f, new[] { -0.2f, 0.2f, -0.2f, 0.56f, 0.2f, 0.84f, 0.2f, 0.2f }));
        public static Sprite Bottle => Get("icon_bottle_line", (x, y) =>
            Stroke(x, y, 0.075f, new[] { -0.44f, -0.84f, 0.44f, -0.84f, 0.44f, 0.16f, -0.44f, 0.16f, -0.44f, -0.84f }) ||
            Stroke(x, y, 0.075f, new[] { -0.16f, 0.16f, -0.16f, 0.46f }) || Stroke(x, y, 0.075f, new[] { 0.16f, 0.16f, 0.16f, 0.46f }) ||
            Stroke(x, y, 0.075f, new[] { -0.26f, 0.46f, 0.26f, 0.46f, 0.26f, 0.84f, -0.26f, 0.84f, -0.26f, 0.46f }));
        public static Sprite Jar => Get("icon_jar_line", (x, y) =>
            Stroke(x, y, 0.075f, new[] { -0.7f, 0.3f, 0.7f, 0.3f, 0.7f, 0.64f, -0.7f, 0.64f, -0.7f, 0.3f }) ||
            Stroke(x, y, 0.075f, new[] { -0.58f, 0.3f, -0.58f, -0.72f, 0.58f, -0.72f, 0.58f, 0.3f }));
        public static Sprite Compact => Get("icon_compact_line", (x, y) => Ring(x, y, 0.8f, 0.075f) || Ring(x, y, 0.44f, 0.07f));
        public static Sprite Palette => Get("icon_palette_line", (x, y) =>
            Stroke(x, y, 0.075f, new[] { -0.84f, 0.48f, 0.84f, 0.48f, 0.84f, -0.48f, -0.84f, -0.48f, -0.84f, 0.48f }) ||
            Ring(x + 0.48f, y, 0.15f, 0.06f) || Ring(x, y, 0.15f, 0.06f) || Ring(x - 0.48f, y, 0.15f, 0.06f));
        public static Sprite Wand => Get("icon_wand_line", (x, y) =>
            Stroke(x, y, 0.07f, new[] { -0.52f, -0.84f, -0.12f, -0.84f, -0.12f, 0.2f, -0.52f, 0.2f, -0.52f, -0.84f }) ||
            Stroke(x, y, 0.07f, new[] { 0.18f, -0.84f, 0.46f, -0.84f, 0.46f, -0.22f, 0.18f, -0.22f, 0.18f, -0.84f }) ||
            Stroke(x, y, 0.05f, new[] { 0.32f, -0.22f, 0.32f, 0.86f }) ||
            Stroke(x, y, 0.04f, new[] { 0.14f, 0.3f, 0.5f, 0.3f }) || Stroke(x, y, 0.04f, new[] { 0.14f, 0.48f, 0.5f, 0.48f }) || Stroke(x, y, 0.04f, new[] { 0.14f, 0.66f, 0.5f, 0.66f }));

        // ---------------------------------------------------------------------------------------
        //  Guides and try-on shapes (any size). shade(u, v): u and v run from -1 to 1, v up.
        // ---------------------------------------------------------------------------------------
        static Sprite Shape(string name, int w, int h, Func<float, float, Color> shade, float border = 0f, bool tile = false)
        {
            string path = Folder + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(Folder);
            const int SS = 2;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int py = 0; py < h; py++)
                for (int px = 0; px < w; px++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            var c = shade(((px + (sx + 0.5f) / SS) / w) * 2f - 1f, ((py + (sy + 0.5f) / SS) / h) * 2f - 1f);
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                        }
                    tex.SetPixel(px, py, a > 0 ? new Color(r / a, g / a, b / a, a / (SS * SS)) : Color.clear);
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
            if (border > 0f) imp.spriteBorder = new Vector4(border, border, border, border);
            if (tile)
            {
                var settings = new TextureImporterSettings();
                imp.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; settings.wrapMode = TextureWrapMode.Repeat;
                imp.SetTextureSettings(settings);
            }
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Color White(float alpha) => new Color(1, 1, 1, Mathf.Clamp01(alpha));

        /// <summary>Four corner brackets (9-sliced, so the corners keep their size): the scan frame.</summary>
        public static Sprite ScanFrame => Shape("guide_scan_frame", 128, 128, (u, v) =>
        {
            float x = Mathf.Abs(u * 64f), y = Mathf.Abs(v * 64f);
            const float half = 60f, r = 22f;
            float qx = x - (half - r), qy = y - (half - r);
            float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            bool ring = d <= 0f && d > -8f;
            return White(ring && x > 18f && y > 18f ? 1f : 0f);
        }, 56f);

        /// <summary>One cell of the guiding mesh (tiled): thin lines on two edges make a grid.</summary>
        public static Sprite GuideMesh => Shape("guide_mesh_cell", 64, 64, (u, v) =>
            White((u * 0.5f + 0.5f) * 64f < 2f || (v * 0.5f + 0.5f) * 64f < 2f ? 1f : 0f), 0f, true);

        /// <summary>A soft horizontal line: the sweep of the scanner.</summary>
        public static Sprite ScanLine => Shape("guide_scan_line", 256, 24, (u, v) =>
            White(Mathf.Sin(Mathf.PI * (u * 0.5f + 0.5f)) * Mathf.Exp(-v * v * 7f)));

        /// <summary>An arc that fades along its length: the loading spinner.</summary>
        public static Sprite SpinnerArc => Shape("guide_spinner_arc", 160, 160, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            if (r < 0.74f || r > 0.92f) return Color.clear;
            float t = Mathf.Atan2(v, u) / (2f * Mathf.PI); if (t < 0) t += 1f;
            return White((1f - t) * (1f - t));          // brightest at the head; the tail trails behind a clockwise turn
        });

        /// <summary>The full faint ring the spinner arc turns on.</summary>
        public static Sprite SpinnerTrack => Shape("guide_spinner_track", 160, 160, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            return White(r >= 0.74f && r <= 0.92f ? 1f : 0f);
        });

        /// <summary>A plain white square: progress fills, dim layers and anything that must NOT be rounded.</summary>
        public static Sprite Square => Shape("ui_square", 8, 8, (u, v) => Color.white);

        /// <summary>A shadow that is darkest at the left edge and gone at the right edge (the "drag here to remove" zone).</summary>
        public static Sprite ShadowFromLeft => Shape("ui_shadow_from_left", 128, 8, (u, v) => White(0.74f * Mathf.Pow(Mathf.Clamp01(0.5f - u * 0.5f), 1.25f)));

        /// <summary>A thin square frame with straight corners: the border of product tiles.</summary>
        public static Sprite SquareOutline => Shape("ui_square_outline", 24, 24,
            (u, v) => White(Mathf.Min(1f - Mathf.Abs(u), 1f - Mathf.Abs(v)) * 12f < 3f ? 1f : 0f), 8f);

        /// <summary>The glow sprite's soft margin around the square, in sprite pixels (= canvas units).</summary>
        public const float SquareGlowMargin = 24f;
        /// <summary>A square ring with a soft glow on both sides (straight corners): the aura of a tapped product tile.</summary>
        public static Sprite SquareGlow => Shape("ui_square_glow", 96, 96, (u, v) =>
        {
            float x = Mathf.Abs(u) * 48f - 24f, y = Mathf.Abs(v) * 48f - 24f;      // above 0 = outside the 48-pixel square in the middle
            float d = new Vector2(Mathf.Max(x, 0f), Mathf.Max(y, 0f)).magnitude + Mathf.Min(Mathf.Max(x, y), 0f);
            float a = d <= 0f && d > -4f ? 1f : 0f;                                 // the ring itself
            if (d > 0f) { float g = Mathf.Clamp01(1f - d / 24f); a = Mathf.Max(a, 0.75f * g * g); }
            if (d <= -4f) { float g = Mathf.Clamp01(1f - (-d - 4f) / 12f); a = Mathf.Max(a, 0.3f * g * g); }
            return White(a);
        }, 36f);

        /// <summary>A soft round blur: blush, and the face tint.</summary>
        public static Sprite SoftDisc => Shape("makeup_soft_disc", 128, 128, (u, v) =>
        {
            float k = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
            return White(k * k * (3f - 2f * k));
        });

        /// <summary>A lip shape with a soft edge.</summary>
        public static Sprite Lips => Shape("makeup_lips", 256, 128, (u, v) =>
        {
            float x = u, y = v * 0.5f;
            if (Mathf.Abs(x) >= 1f) return Color.clear;
            float curve = 1f - x * x;
            float bowSide = Mathf.Exp(-Mathf.Pow((Mathf.Abs(x) - 0.25f) / 0.18f, 2f)), bowDip = Mathf.Exp(-Mathf.Pow(x / 0.10f, 2f));
            float top = 0.30f * Mathf.Pow(curve, 0.8f) + 0.10f * bowSide - 0.07f * bowDip;
            float bottom = -0.42f * Mathf.Pow(curve, 0.9f);
            if (y >= top || y <= bottom) return Color.clear;
            return White(Mathf.Clamp01((top - y) / 0.045f) * Mathf.Clamp01((y - bottom) / 0.045f));
        });

        /// <summary>An eyelid shape that is strongest at the lash line and fades upwards (left eye; flip it for the right).</summary>
        public static Sprite EyeShadow => Shape("makeup_eyeshadow", 192, 112, (u, v) =>
        {
            if (Mathf.Abs(u) >= 1f) return Color.clear;
            float arc = Mathf.Sqrt(1f - u * u);
            float lash = -0.7f + 0.55f * arc, top = -0.15f + 1.05f * arc;
            if (v <= lash || v >= top) return Color.clear;
            float k = 1f - (v - lash) / Mathf.Max(0.01f, top - lash);
            return White(Mathf.Pow(k, 0.9f) * Mathf.Clamp01((v - lash) / 0.06f) * Mathf.Clamp01(arc * 2.5f));
        });

        /// <summary>The face guide: an oval outline with a faint guiding mesh (meridians and parallels).</summary>
        public static Sprite FaceGuide => Shape("guide_face", 512, 640, (u, v) =>
        {
            float taper = 1f - 0.16f * Mathf.Clamp01(-v) * Mathf.Clamp01(-v);       // a slightly narrower chin
            float uu = u / (0.92f * taper), vv = v / 0.92f;
            float r = Mathf.Sqrt(uu * uu + vv * vv);
            if (r > 1.02f) return Color.clear;
            if (Mathf.Abs(r - 1f) < 0.016f) return White(1f);
            if (r >= 1f) return Color.clear;
            float span = Mathf.Sqrt(Mathf.Max(0f, 1f - vv * vv));
            foreach (float k in new[] { -0.66f, -0.33f, 0f, 0.33f, 0.66f }) if (Mathf.Abs(uu - k * span) < 0.007f) return White(0.5f);
            foreach (float c in new[] { -0.55f, -0.2f, 0.15f, 0.5f }) if (Mathf.Abs(vv - c) < 0.0055f) return White(0.5f);
            return Color.clear;
        });

        // ---- round shapes that share the Pill geometry (radius 64 px), so PillCorners keeps them aligned with it ----
        /// <summary>Thin round outline: the border of input fields.</summary>
        public static Sprite PillOutline => Rounded("ui_pill_outline", 5f, 0f, 0f);
        /// <summary>Soft halo around a round shape, hollow in the middle: the Continue button's glow.</summary>
        public static Sprite PillGlow => Rounded("ui_pill_glow", 0f, 32f, 0f);
        /// <summary>Neon ring with a glow on both sides: selected tiles and checklist rows.</summary>
        public static Sprite NeonOutline => Rounded("ui_neon_outline", 7f, 24f, 14f);

        /// <summary>Sprite margin (in sprite pixels) around the shape for each glow sprite - PillCorners needs it.</summary>
        public const float PillGlowMargin = 32f, NeonOutlineMargin = 24f;

        static Sprite Rounded(string name, float ring, float glowOut, float glowIn)
        {
            string path = Folder + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(Folder);
            const float r = 64f; const int centre = 4;
            int margin = Mathf.CeilToInt(glowOut);
            int n = 2 * (int)r + centre + 2 * margin;
            float half = r + centre / 2f;                               // half-size of the round shape itself
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float px = Mathf.Abs(x + 0.5f - n / 2f) - (half - r), py = Mathf.Abs(y + 0.5f - n / 2f) - (half - r);
                    float d = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude + Mathf.Min(Mathf.Max(px, py), 0) - r;   // <0 inside, >0 outside
                    float a = 0f;
                    if (ring > 0f) a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(0.5f + d + ring);                    // band from -ring to 0
                    if (glowOut > 0f && d > -0.5f) { float g = Mathf.Clamp01(1f - d / glowOut); a = Mathf.Max(a, 0.75f * g * g * Mathf.Clamp01(d + 0.5f)); }
                    if (glowIn > 0f && d < -ring) { float g = Mathf.Clamp01(1f - (-d - ring) / glowIn); a = Mathf.Max(a, 0.35f * g * g); }
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.filterMode = FilterMode.Bilinear;
            float border = r + margin;
            imp.spriteBorder = new Vector4(border, border, border, border);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>9-sliced rounded outline with a soft outer glow (white; tinted by the Image colour).</summary>
        public static Sprite Outline
        {
            get
            {
                var s = GetColor("tile_outline", OutlineShade);
                string path = AssetDatabase.GetAssetPath(s);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                if (imp != null && imp.spriteBorder == Vector4.zero)
                {
                    imp.spriteBorder = new Vector4(48, 48, 48, 48);
                    imp.SaveAndReimport();
                    s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }
                return s;
            }
        }

        static Color OutlineShade(float x, float y)
        {
            const float half = 0.86f, r = 0.34f;
            float qx = Mathf.Abs(x) - (half - r), qy = Mathf.Abs(y) - (half - r);
            float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            if (d <= 0f && d > -0.075f) return Color.white;                    // crisp ring
            if (d > 0f && d < 0.14f) return new Color(1, 1, 1, 0.55f * (1f - d / 0.14f));   // outer glow
            return Color.clear;
        }

        // Brand-style colour icons (simple vector approximations - swap for official logo PNGs if you have them)
        public static Sprite Google => GetColor("icon_google", GoogleG);
        public static Sprite Facebook => GetColor("icon_facebook", FacebookF);

        static readonly Color GBlue = new Color32(66, 133, 244, 255), GRed = new Color32(234, 67, 53, 255),
                              GYellow = new Color32(251, 188, 5, 255), GGreen = new Color32(52, 168, 83, 255),
                              FbBlue = new Color32(24, 119, 242, 255);

        static Color GoogleG(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float a = Mathf.Atan2(y, x) * Mathf.Rad2Deg; if (a < 0) a += 360f;
            bool bar = x > -0.02f && x < 0.82f && y > -0.17f && y < 0.17f;
            if (bar) return GBlue;
            bool ring = r > 0.52f && r < 0.86f;
            if (!ring) return Color.clear;
            if (a > 0f && a < 40f) return Color.clear;                 // opening on the right
            if (a >= 40f && a < 150f) return GRed;
            if (a >= 150f && a < 215f) return GYellow;
            if (a >= 215f && a < 300f) return GGreen;
            return GBlue;
        }

        static Color FacebookF(float x, float y)
        {
            if (x * x + y * y > 0.92f * 0.92f) return Color.clear;
            bool stem = x > -0.04f && x < 0.18f && y > -1f && y < 0.46f;
            bool bar = x > -0.30f && x < 0.44f && y > 0.12f && y < 0.30f;
            float dx = x - 0.36f, dy = y - 0.46f, d = Mathf.Sqrt(dx * dx + dy * dy);
            bool hook = d > 0.18f && d < 0.40f && dx < 0.02f && dy > -0.02f;
            bool tip = x >= 0.34f && x < 0.58f && y > 0.62f && y < 0.84f;
            return (stem || bar || hook || tip) ? Color.white : FbBlue;
        }

        static Sprite Get(string name, Func<float, float, bool> inside)
            => GetColor(name, (x, y) => inside(x, y) ? Color.white : Color.clear);

        static Sprite GetColor(string name, Func<float, float, Color> shade)
        {
            string path = Folder + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(Folder);
            const int N = 128, SS = 3;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int py = 0; py < N; py++)
                for (int px = 0; px < N; px++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float x = ((px + (sx + 0.5f) / SS) / N) * 2f - 1f;
                            float y = ((py + (sy + 0.5f) / SS) / N) * 2f - 1f;
                            var c = shade(x * 1.05f, y * 1.05f);
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                        }
                    float n = SS * SS;
                    tex.SetPixel(px, py, a > 0 ? new Color(r / a, g / a, b / a, a / n) : Color.clear);
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
