using GlowUp.Common;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using A = GlowUp.EditorTools.AppKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Scene > 0 Start-Up Animation.
    /// Builds StartupScene, the first scene of the app: the word "Glow" comes down with a glowing aura, "Up" comes up,
    /// they join into "GlowUp", then a "Made with Unity" plate knocks the word off the screen and the app opens.
    ///
    /// The scene is built in its END position (the finished word in the middle, the plate parked at the left outside the
    /// screen), so it looks right in Edit mode. Texts, timings, distances and the way it ends are on the
    /// StartupIntroController object.
    ///
    /// The builder also puts StartupScene FIRST in File > Build Settings, because the app starts with the first scene of
    /// that list. Unity's own "Made with Unity" start-up screen still comes before it: a Personal licence cannot remove it.
    /// </summary>
    public static class StartupSceneBuilder
    {
        const float WordSize = 190, WordGap = 22, WordHeight = 250;
        static readonly Vector2 Middle = new Vector2(0, 70);

        [MenuItem("GlowUp/Build Scene/0 Start-Up Animation", false, 100)]
        public static void Build()
        {
            A.AppScreen s = A.Begin(SceneNames.Startup, null, false, "StartupCard");
            Transform card = s.card;

            // everything that takes part in the animation; it fades out as one at the end
            GameObject stage = A.Node(card, "IntroStage", typeof(CanvasGroup));
            A.Fill(stage.transform);

            // ---- the glowing aura (behind the words) ----
            Image aura = A.Icon(stage.transform, "Aura", IconFactory.SoftDisc, 1020, ThemeRole.Accent);
            A.At(aura.transform, A.MC, Middle, new Vector2(1020, 640));
            aura.preserveAspect = false;
            aura.color = new Color(1f, 1f, 1f, 0.5f);          // how strong it shows in Edit mode; the app fades it in and makes it breathe

            // ---- the two words ----
            GameObject mark = A.Node(stage.transform, "Wordmark");
            A.At(mark.transform, A.MC, Middle, new Vector2(960, WordHeight));
            TextMeshProUGUI glow = A.Label(mark.transform, "GlowWord", "Glow", WordSize, ThemeRole.Heading, FontRole.Title, TextAlignmentOptions.Center);
            TextMeshProUGUI up = A.Label(mark.transform, "UpWord", "Up", WordSize, ThemeRole.ButtonFill, FontRole.Title, TextAlignmentOptions.Center);      // the pink of the logo

            // side by side, centred as one word: the widths come from the title font
            ThemeProfile profile = SceneBuilderCore.EnsureThemeProfile();
            if (profile != null && profile.titleFont != null) { glow.font = profile.titleFont; up.font = profile.titleFont; }
            float glowWidth = WidthOf(glow, 430), upWidth = WidthOf(up, 230);
            float whole = glowWidth + WordGap + upWidth;
            A.At(glow.transform, A.MC, new Vector2(-whole / 2f + glowWidth / 2f, 0), new Vector2(glowWidth + 12, WordHeight));
            A.At(up.transform, A.MC, new Vector2(whole / 2f - upWidth / 2f, 0), new Vector2(upWidth + 12, WordHeight));

            // ---- the little star that flashes when they join (top right of the word, as in the logo) ----
            Image sparkle = A.Icon(stage.transform, "Sparkle", IconFactory.Star4, 96, ThemeRole.Gold);
            A.At(sparkle.transform, A.MC, Middle + new Vector2(whole / 2f + 36, WordHeight * 0.36f), new Vector2(96, 96));

            // ---- the plate that knocks the word off ----
            // It ends in the middle of the screen. In Edit mode it is parked at the left, outside the screen, so the finished
            // word can be seen; the app puts it at its starting place itself (StartupIntroController > Plate Start).
            Image plate = A.Panel(stage.transform, "MadeWithUnityPlate", ThemeRole.Heading, 1f, 130f);
            A.At(plate.transform, A.MC, Middle, new Vector2(760, 150));
            TextMeshProUGUI plateText = A.Label(plate.transform, "PlateText", "Made with Unity", 62, ThemeRole.Surface, FontRole.Heading, TextAlignmentOptions.Center);
            A.Fill(plateText.transform, 30, 0, 30, 0);
            A.Shrink(plateText, 40);

            TextMeshProUGUI skip = A.Label(stage.transform, "SkipHintText", "Tap to skip", 28, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center);
            A.BottomStrip(skip.transform, 70, 44);

            // ---- controller ----
            var controller = A.Controller<StartupIntroController>("StartupIntroController");
            A.Set(controller, "wordmark", (RectTransform)mark.transform);
            A.Set(controller, "glowWord", glow.rectTransform); A.Set(controller, "upWord", up.rectTransform);
            A.Set(controller, "aura", aura); A.Set(controller, "sparkle", sparkle);
            A.Set(controller, "plate", plate.rectTransform); A.Set(controller, "plateText", plateText);
            A.Set(controller, "stage", stage.GetComponent<CanvasGroup>());

            A.Finish(s);

            // after the shared finish (which may restyle things): the plate waits outside the screen in Edit mode. Its END
            // place, the middle, is what the script reads when the app starts, so it is kept on the script.
            SerializedObject so = new SerializedObject(controller);
            SerializedProperty rest = so.FindProperty("plateRest");
            if (rest != null) { rest.vector2Value = Middle; so.ApplyModifiedPropertiesWithoutUndo(); }
            plate.rectTransform.anchoredPosition = new Vector2(-1500f, Middle.y);
            sparkle.color = new Color(sparkle.color.r, sparkle.color.g, sparkle.color.b, 1f);
            SceneBuilderCore.Save(s.scene);

            BuildSettingsSync.PutFirst(SceneNames.Startup);
            Debug.Log("[GlowUp] StartupScene built and put first in Build Settings. It is shown in its end position; the plate waits at the left outside the screen. Timings and the ending are on the StartupIntroController object.");
        }

        /// <summary>The width a word needs at its font size (a guess when the font cannot be measured here).</summary>
        static float WidthOf(TextMeshProUGUI text, float guess)
        {
            try
            {
                text.ForceMeshUpdate(true, true);
                float width = text.GetPreferredValues(text.text).x;
                if (width > 20f && width < 2000f) return Mathf.Ceil(width);
            }
            catch (System.Exception) { }
            return guess;
        }
    }
}
