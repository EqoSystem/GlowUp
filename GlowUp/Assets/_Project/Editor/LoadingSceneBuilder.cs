using GlowUp.Common;
using GlowUp.Scanning;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using A = GlowUp.EditorTools.AppKit;
using Kit = GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Scene > 11 Loading.
    /// The waiting screen between a scan and its result: spinner, message, the four steps that tick off
    /// one by one, a progress bar, a rotating tip and Cancel.
    /// </summary>
    public static class LoadingSceneBuilder
    {
        const string AiConfigPath = "Assets/_Project/Scripts/Scanning/GeminiApiConfig_Default.asset";

        [MenuItem("GlowUp/Build Scene/11 Loading", false, 121)]
        public static void Build()
        {
            A.AppScreen s = A.Begin("LoadingScene", null, false, "LoadingCard");
            Transform card = s.card;

            TextMeshProUGUI logo = A.Label(card, "TitleText", "Glow Up", Kit.TitleSize, ThemeRole.Gold, FontRole.Title, TextAlignmentOptions.Center);
            A.TopStrip(logo.transform, 110, 136);

            // everything else sits in a block in the middle of the screen, whatever the screen height
            GameObject block = A.Node(card, "LoadingBlock");
            A.At(block.transform, A.MC, new Vector2(0, -20), new Vector2(920, 960));

            // ---- spinner ----
            GameObject spinnerHolder = A.Node(block.transform, "Spinner");
            A.At(spinnerHolder.transform, A.TC, Vector2.zero, new Vector2(220, 220));
            Image track = A.Icon(spinnerHolder.transform, "SpinnerTrack", IconFactory.SpinnerTrack, 220, ThemeRole.SurfaceAlt);
            Image arc = A.Icon(spinnerHolder.transform, "SpinnerArc", IconFactory.SpinnerArc, 220, ThemeRole.Accent);
            A.Icon(spinnerHolder.transform, "SpinnerIcon", IconFactory.Sparkle, 84, ThemeRole.Heading);

            // ---- message ----
            TextMeshProUGUI message = A.Label(block.transform, "MessageText", "Scanning and analysing your product", 44, ThemeRole.MainText, FontRole.Heading,
                                              TextAlignmentOptions.Center, true);
            A.TopStrip(message.transform, 246, 124, 30, 30);
            A.ShrinkWrapped(message, 32);
            TextMeshProUGUI wait = A.Label(block.transform, "WaitText", "Please wait, this only takes a moment", 31, ThemeRole.SecondaryText, FontRole.Body,
                                           TextAlignmentOptions.Center);
            A.TopStrip(wait.transform, 372, 50, 30, 30);

            // ---- steps ----
            string[] stepTexts = { "Reading the ingredient list", "Checking our ingredient database", "Comparing with your skin profile", "Preparing your result" };
            const float rowHeight = 66;
            Image stepsPanel = A.Panel(block.transform, "StepsPanel", ThemeRole.SurfaceAlt, Kit.PanelAlpha, 48f);
            A.TopStrip(stepsPanel.transform, 448, 28 + stepTexts.Length * rowHeight + 28, 60, 60);
            var steps = new RequirementIndicatorItem[stepTexts.Length];
            for (int i = 0; i < stepTexts.Length; i++)
            {
                GameObject row = A.Node(stepsPanel.transform, "Step" + (i + 1), typeof(RequirementIndicatorItem));
                A.TopStrip(row.transform, 28 + i * rowHeight, rowHeight, 40, 30);
                Image check = A.RawIcon(row.transform, "CheckIcon", IconFactory.Check, 40, new Color32(150, 140, 132, 255));
                A.At(check.transform, A.ML, new Vector2(4, 0), new Vector2(40, 40));
                TextMeshProUGUI label = A.RawLabel(row.transform, "StepLabel", stepTexts[i], 33, new Color32(150, 140, 132, 255), FontRole.Label);
                A.Fill(label.transform, 66, 0, 0, 0);
                A.Shrink(label, 26);
                steps[i] = row.GetComponent<RequirementIndicatorItem>();
                A.Set(steps[i], "checkIcon", check); A.Set(steps[i], "label", label); A.Set(steps[i], "requirementText", stepTexts[i]);
            }
            float afterSteps = 448 + 28 + stepTexts.Length * rowHeight + 28;

            // ---- progress bar: a round track that clips a plain fill ----
            Image progressTrack = A.Panel(block.transform, "ProgressTrack", ThemeRole.SurfaceAlt, 1f, 20f);
            A.TopStrip(progressTrack.transform, afterSteps + 34, 20, 60, 60);
            progressTrack.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            Image fill = A.Icon(progressTrack.transform, "ProgressFill", IconFactory.Square, 20, ThemeRole.Accent);
            fill.preserveAspect = false; fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left; fill.fillAmount = 0.35f;        // the app starts it at 0
            A.Fill(fill.transform);

            TextMeshProUGUI tip = A.Label(block.transform, "TipText", "Tip: patch-test any new product on a small area first.", 30, ThemeRole.SecondaryText, FontRole.Body,
                                          TextAlignmentOptions.Center, true);
            A.TopStrip(tip.transform, afterSteps + 78, 96, 60, 60);

            Button cancel = A.PillButton(card, "CancelButton", "Cancel", 38, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.At(cancel.transform, A.BC, new Vector2(0, 60), new Vector2(380, 112));

            // ---- controller ----
            var controller = A.Controller<LoadingSceneController>("LoadingSceneController");
            A.Set(controller, "messageText", message); A.Set(controller, "tipText", tip);
            A.Set(controller, "progressFill", fill); A.Set(controller, "spinner", arc.rectTransform);
            Kit.SetArray(controller, "steps", steps);
            A.Set(controller, "cancelButton", cancel);
            var aiConfig = AssetDatabase.LoadAssetAtPath<GeminiApiConfig>(AiConfigPath);
            if (aiConfig != null) A.Set(controller, "aiConfig", aiConfig);
            else Debug.LogWarning("[GlowUp] " + AiConfigPath + " was not found. Drag your Gemini Api Config onto LoadingSceneController > Ai Config, or the AI second opinion stays off.");

            A.Finish(s);
            Debug.Log("[GlowUp] LoadingScene built. The steps tick off as the analysis moves on; texts and timings are on the LoadingSceneController object.");
        }
    }
}
