using GlowUp.Common;
using GlowUp.Onboarding;
using GlowUp.Theme;
using GlowUp.Views;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Final pass every scene builder runs, so all scenes share one look:
    /// fixed-width canvas, the page background with a very faint grain, centred text inside every input field,
    /// input borders, fully round shapes, press feedback on buttons, font roles and the palette preview.
    /// Safe to run repeatedly (menu: GlowUp > Apply Theme To Open Scene).
    /// </summary>
    public static class ThemeKit
    {
        [MenuItem("GlowUp/Apply Theme To Open Scene")]
        public static void ApplyToOpenScene()
        {
            FinishScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        public static void FinishScene()
        {
            FixCanvasScaler();
            EnsurePaperBackground();
            foreach (var f in Object.FindObjectsOfType<TMP_InputField>(true))
            {
                if (f.lineType == TMP_InputField.LineType.SingleLine) OnboardingKit.NormalizeInput(f);     // multi-line boxes keep their own layout
                OnboardingKit.EnsureFocusBorder(f.gameObject);
            }
            foreach (var d in Object.FindObjectsOfType<TMP_Dropdown>(true)) OnboardingKit.EnsureFocusBorder(d.gameObject);
            RoundShapes();
            UiEffectsKit.FinishScene();             // tap sounds, screen change, edge light, the tile animation on onboarding buttons
            AddPressFeedback();
            AssignTypography();
            ThemePreview.Apply(false);
        }

        /// <summary>Canvas width is fixed at 1080 and the height follows the phone (1920 to 2400+).</summary>
        public static void FixCanvasScaler()
        {
            foreach (var c in Object.FindObjectsOfType<Canvas>(true))
            {
                if (c.transform.parent != null && c.transform.parent.GetComponentInParent<Canvas>(true) != null) continue;      // a canvas inside another one
                var s = c.GetComponent<CanvasScaler>();
                if (s == null) continue;
                s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                s.referenceResolution = new Vector2(1080, 1920);
                s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                s.matchWidthOrHeight = 0f;
            }
            if (Camera.main != null)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = SceneBuilderCore.EnsureThemeProfile().light.background;
            }
        }

        /// <summary>How strong the grain over the background is (0 = none, 1 = the old paper look). White should dominate, so it is faint.</summary>
        public const float GrainAlpha = 0.16f;

        /// <summary>Full-screen background in the theme's colour, with a very faint tiled grain on top of it.</summary>
        public static void EnsurePaperBackground()
        {
            Canvas root = null;
            foreach (var c in Object.FindObjectsOfType<Canvas>(true)) if (SceneBuilderCore.IsSceneCanvas(c)) { root = c; break; }
            if (root == null) return;

            Transform bg = OnboardingKit.Loose(root.transform, "DarkBackground");      // the scenes' existing background object
            if (bg == null) bg = root.transform.Find("PaperBackground");
            if (bg == null)
            {
                var go = new GameObject("PaperBackground", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root.transform, false); bg = go.transform;
            }
            if (bg.parent != root.transform) bg.SetParent(root.transform, false);
            bg.SetAsFirstSibling();
            OnboardingKit.Stretch(bg, 0, 0, 0, 0);
            var img = SceneBuilderCore.Ensure<Image>(bg.gameObject);
            img.raycastTarget = false; OnboardingKit.SetAlpha(img, 1f);
            SceneBuilderCore.Themed(bg.gameObject, ThemeRole.Background);

            Transform tex = bg.Find("PaperTexture");
            if (tex == null) { var go = new GameObject("PaperTexture", typeof(RectTransform), typeof(Image)); go.transform.SetParent(bg, false); tex = go.transform; }
            OnboardingKit.Stretch(tex, 0, 0, 0, 0);
            var ti = SceneBuilderCore.Ensure<Image>(tex.gameObject);
            ti.sprite = PaperTextureFactory.Paper; ti.type = Image.Type.Tiled; ti.raycastTarget = false; ti.color = new Color(1f, 1f, 1f, GrainAlpha);
        }

        /// <summary>Buttons, inputs, toggles, dropdowns and every rounded panel get the round sprite + PillCorners.</summary>
        public static void RoundShapes()
        {
            var pill = IconFactory.Pill;
            var builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            foreach (var img in Object.FindObjectsOfType<Image>(true))
            {
                bool round = img.sprite == null ? img.GetComponent<Selectable>() != null
                                                : (img.sprite == pill || img.sprite == builtin);
                if (!round) continue;
                if (img.sprite != pill) img.sprite = pill;
                img.type = Image.Type.Sliced;
                SceneBuilderCore.Ensure<PillCorners>(img.gameObject);
                EditorUtility.SetDirty(img);
            }
        }

        /// <summary>Ordinary buttons get the press scale-punch (Continue buttons and tiles have their own animation).</summary>
        public static void AddPressFeedback()
        {
            foreach (var b in Object.FindObjectsOfType<Button>(true))
            {
                if (b.GetComponent<ContinueButtonGlowAnimator>() != null || b.GetComponent<SelectableTile>() != null) continue;
                if (b.GetComponent<ShadeButton>() != null) continue;        // shade swatches have their own grow animation
                if (b.GetComponent<PressSweepEffect>() != null) continue;   // the tile animation has its own lift
                if (b.name == "Backdrop") continue;                         // the dim layer behind a pop-up must not move
                if (b.GetComponentInParent<BottomActionBar>(true) != null)  // the bottom bar: no tap animation at all (the brief of 11 October 2026)
                {
                    if (b.TryGetComponent(out ButtonPressFeedback old)) Object.DestroyImmediate(old);
                    continue;
                }
                SceneBuilderCore.Ensure<ButtonPressFeedback>(b.gameObject);
            }
        }

        /// <summary>Gives every text a font role (see ThemeProfile) and applies the project fonts.</summary>
        public static void AssignTypography()
        {
            var profile = SceneBuilderCore.EnsureThemeProfile();
            foreach (var t in Object.FindObjectsOfType<TMP_Text>(true))
            {
                var tf = SceneBuilderCore.Ensure<ThemedFont>(t.gameObject);
                if (!tf.KeepRole) tf.Role = Classify(t);                    // builders that know the role set it and tick Keep Role
                tf.Apply(profile);
                EditorUtility.SetDirty(tf);
                EditorUtility.SetDirty(t);
            }
        }

        static FontRole Classify(TMP_Text t)
        {
            string n = t.gameObject.name;
            if (n == "TitleText") return FontRole.Title;
            if (t.GetComponentInParent<TMP_InputField>(true) != null || t.GetComponentInParent<TMP_Dropdown>(true) != null) return FontRole.Input;
            if (t.GetComponentInParent<SelectableTile>(true) != null)                // tile texts sit under the tile's Button
                return n == "SubtitleText" || n == "DescText" || n == "BadgeText" ? FontRole.Body : FontRole.Heading;
            if (t.GetComponentInParent<Toggle>(true) != null) return FontRole.Label;
            if (t.GetComponentInParent<Button>(true) != null) return FontRole.Button;
            if (n.Contains("Header") || n.StartsWith("Subtitle") || n == "CalendarTitle") return FontRole.Heading;
            if (n == "ValueText") return FontRole.Input;
            if (n.EndsWith("_Label") || n.Contains("Badge") || n.Contains("Hint") || n.Contains("Feedback") || n == "StepText"
                || n.Contains("_Req") || n.Contains("Helper") || n.StartsWith("WD_"))
                return FontRole.Body;
            if (n == "Label" || n.EndsWith("Text")) return FontRole.Label;
            return FontRole.Body;
        }
    }
}
