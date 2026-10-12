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
    /// they join into "GlowUp", then the finished word ascends out through the top of the screen into a soft light
    /// (like going to heaven) and the app opens.
    ///
    /// The scene is built in its END position (the finished word in the middle), so it looks right in Edit mode. Timings,
    /// distances and the way it ends are on the StartupIntroController object.
    ///
    /// Taken out on 13 October 2026 because the project owner asked: the "Made with Unity" plate that knocked the word
    /// off the screen, and the words "Tap to skip" (a tap still skips; StartupIntroController > Tap Skips).
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

            // (the little gold star beside the word was taken out on 12 October 2026: the project owner asked for it to go)

            // ---- the light the word rises into ----
            // A soft, pale warm glow under the top edge of the card, behind the words. It cannot be seen in Edit mode (the
            // scene shows the moment BEFORE the word rises); the app fades it in during the ascent
            // (StartupIntroController > Heaven Light Strength). Its colour is this Image's own colour: a pale gold, light
            // enough to read as light on the pale page (the theme's gold is a darker metal colour and looked brown here).
            Image light = A.RawIcon(stage.transform, "HeavenLight", IconFactory.SoftDisc, 900, new Color32(255, 224, 160, 0));
            A.At(light.transform, A.TC, new Vector2(0, 40), new Vector2(980, 900));
            light.preserveAspect = false;
            light.transform.SetSiblingIndex(aura.transform.GetSiblingIndex());

            // ---- controller ----
            var controller = A.Controller<StartupIntroController>("StartupIntroController");
            A.Set(controller, "wordmark", (RectTransform)mark.transform);
            A.Set(controller, "glowWord", glow.rectTransform); A.Set(controller, "upWord", up.rectTransform);
            A.Set(controller, "aura", aura);
            A.Set(controller, "heavenLight", light);
            A.Set(controller, "stage", stage.GetComponent<CanvasGroup>());

            A.Finish(s);

            // after the shared finish (which may restyle things): the light is there but cannot be seen until the word rises
            Color hidden = light.color; hidden.a = 0f; light.color = hidden;
            EditorUtility.SetDirty(light);
            SceneBuilderCore.Save(s.scene);

            BuildSettingsSync.PutFirst(SceneNames.Startup);
            Debug.Log("[GlowUp] StartupScene built and put first in Build Settings. It is shown in its end position (the finished word in the middle). Timings and the ascent are on the StartupIntroController object.");
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
