using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Onboarding;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Builds the scrollable checklist block used by Steps 2 and 4:
    /// [panel > ScrollView > Viewport > Content > Option_* rows] + [add-your-own input + Add button].
    /// The list stretches between `topY` and the add row, so it fits any phone height.
    /// Existing objects (e.g. Step 4's AllergenContainerCard, UserAllergenInputField, AddButton) are reused.
    /// The option rows are generated objects: they are rebuilt on every run, keeping whatever wording they had.
    /// </summary>
    public static class ChecklistBuilder
    {
        // distances from the panel bottom (the Continue button + message line take the first FooterTop units)
        const float AddRowY = FooterTop + 10, AddRowH = 120, ListBottom = AddRowY + AddRowH + 14;
        const float RowHeight = 100, BoxSize = 60;

        /// <summary>Name of the "none of these" row object. Its label text can be reworded freely in the Hierarchy.</summary>
        public const string NoneRowName = "Option_None";

        /// <param name="noneOption">Wording of an extra first row that means "none of these" (it unticks every other row). Null = no such row.</param>
        public static ChecklistPanel Build(Transform card, string name, float topY, string[] defaultOptions, string addPlaceholder,
            Transform existingInput, Transform existingAdd, Transform existingCard, TextMeshProUGUI sharedFeedback, string noneOption = null)
        {
            // ---- backdrop panel ----
            var root = EnsurePanel(card, name, ThemeRole.SurfaceAlt, PanelAlpha, existingCard);
            TopBottom(root, card, topY, ListBottom);

            // ---- scroll view ----
            var scrollRt = MakeScroll(root, name + "_Scroll", out var content);
            Stretch(scrollRt, 16, 16, 16, 16);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 28, 16, 16); layout.spacing = 10;      // side padding leaves room for the rows' glow

            // ---- option rows: keep the current wording, rebuild the objects from the current template ----
            var labels = new List<string>();
            var stale = new List<GameObject>();
            string noneText = noneOption;                     // the wording typed on an existing None row is kept
            foreach (Transform child in content)
            {
                if (child.name == "OptionRowTemplate") { stale.Add(child.gameObject); continue; }
                if (!child.name.StartsWith("Option_")) continue;
                var l = child.GetComponentInChildren<TextMeshProUGUI>(true);
                bool hasText = l != null && !string.IsNullOrWhiteSpace(l.text);
                if (child.name == NoneRowName) { if (hasText && noneOption != null) noneText = l.text; }
                else if (hasText && !(noneOption != null && string.Equals(l.text.Trim(), noneOption, System.StringComparison.OrdinalIgnoreCase))) labels.Add(l.text);
                stale.Add(child.gameObject);
            }
            foreach (var s in stale) Object.DestroyImmediate(s);
            if (labels.Count == 0) { labels.AddRange(defaultOptions); labels.Add("Apply All"); }

            Toggle template = BuildRowTemplate(content);
            Toggle noneRow = null;
            if (noneOption != null)
            {
                // first in the list: the quick answer for a user who has nothing to tick
                var row = Object.Instantiate(template.gameObject, content);
                row.name = NoneRowName; row.SetActive(true);
                var l = row.GetComponentInChildren<TextMeshProUGUI>(true); if (l != null) l.text = noneText;
                noneRow = row.GetComponent<Toggle>();
            }
            foreach (var text in labels)
            {
                var row = Object.Instantiate(template.gameObject, content);
                row.name = "Option_" + text; row.SetActive(true);
                var l = row.GetComponentInChildren<TextMeshProUGUI>(true); if (l != null) l.text = text;
            }

            // ---- add-your-own row: anchored to the panel bottom, stretched across ----
            TMP_InputField input = existingInput != null ? existingInput.GetComponent<TMP_InputField>() : null;
            if (input == null) input = SceneBuilderCore.MakeInput(card, name + "_AddInput", addPlaceholder, new Vector2(0.5f, 0), Vector2.zero, new Vector2(600, AddRowH));
            if (input.transform.parent != card) input.transform.SetParent(card, false);
            var irt = (RectTransform)input.transform;
            irt.anchorMin = new Vector2(0, 0); irt.anchorMax = new Vector2(1, 0); irt.pivot = new Vector2(0.5f, 0);
            irt.offsetMin = new Vector2(PadX, AddRowY); irt.offsetMax = new Vector2(-(PadX + 232), AddRowY + AddRowH);
            StyleInput(input, addPlaceholder, ThemeRole.InputBackground);
            var ib = SceneBuilderCore.Ensure<InputFieldBehaviour>(input.gameObject);
            SceneBuilderCore.SetField(ib, "characterLimit", 40);

            Button add = existingAdd != null ? SceneBuilderCore.Ensure<Button>(existingAdd.gameObject)
                                             : SceneBuilderCore.MakeButton(card, name + "_AddButton", "Add", ButtonSize, new Vector2(1, 0), Vector2.zero, new Vector2(216, AddRowH), Color.white);
            ClearClicks(add);
            if (add.transform.parent != card) add.transform.SetParent(card, false);
            var art = (RectTransform)add.transform;
            art.anchorMin = art.anchorMax = new Vector2(1, 0); art.pivot = new Vector2(1, 0);
            art.anchoredPosition = new Vector2(-PadX, AddRowY); art.sizeDelta = new Vector2(216, AddRowH);
            var addImg = SceneBuilderCore.Ensure<Image>(add.gameObject); StyleRounded(addImg); SetAlpha(addImg, 1f); add.targetGraphic = addImg;
            SceneBuilderCore.Themed(add.gameObject, ThemeRole.ButtonFill);
            foreach (var l in add.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                SetText(l, "Add", 42, TextAlignmentOptions.Center); l.raycastTarget = false;
                Stretch(l.transform, 0, 0, 0, 0);
                SceneBuilderCore.Themed(l.gameObject, ThemeRole.ButtonText);
            }

            // messages ("Added ...", "Already in the list") share the scene's message line above Continue
            var oldFeedback = card.Find(name + "_FeedbackText");
            if (oldFeedback != null) Object.DestroyImmediate(oldFeedback.gameObject);

            // ---- component ----
            var panel = SceneBuilderCore.Ensure<ChecklistPanel>(root.gameObject);
            SceneBuilderCore.SetField(panel, "listContent", content);
            SceneBuilderCore.SetField(panel, "rowTemplate", template);
            SetReference(panel, "noneRow", noneRow);
            SceneBuilderCore.SetField(panel, "customInput", input);
            SceneBuilderCore.SetField(panel, "addButton", add);
            SceneBuilderCore.SetField(panel, "feedbackText", sharedFeedback);
            SetStrings(panel, "options", defaultOptions);
            return panel;
        }

        /// <summary>Sets an object slot, also to "None" (SceneBuilderCore.SetField cannot clear a slot).</summary>
        static void SetReference(Object target, string field, Object value)
        {
            var so = new UnityEditor.SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[SceneBuilder] {target.GetType().Name} has no field '{field}'"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// OptionRowTemplate [round row + Toggle + ChecklistRowAnimator] > GlowOutline, Background (round box) > Checkmark, Label.
        /// It stays inactive; rows are copies of it.
        /// </summary>
        static Toggle BuildRowTemplate(RectTransform content)
        {
            var go = new GameObject("OptionRowTemplate", typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement), typeof(ChecklistRowAnimator));
            go.transform.SetParent(content, false);
            go.transform.SetAsFirstSibling();
            var row = go.GetComponent<Image>(); StyleRounded(row);
            var le = go.GetComponent<LayoutElement>(); le.preferredHeight = RowHeight; le.minHeight = RowHeight; le.flexibleHeight = 0;

            // neon outline that glows while the row is ticked
            var glowGo = new GameObject("GlowOutline", typeof(RectTransform), typeof(Image));
            glowGo.transform.SetParent(go.transform, false);
            var glow = glowGo.GetComponent<Image>(); glow.sprite = IconFactory.NeonOutline; glow.type = Image.Type.Sliced; glow.raycastTarget = false; glow.color = Color.clear;
            float halo = IconFactory.NeonOutlineMargin * (RowHeight * 0.5f) / PillCorners.SpriteRadius;
            Stretch(glowGo.transform, -halo, -halo, -halo, -halo);
            var gpc = glowGo.AddComponent<PillCorners>();
            SceneBuilderCore.SetField(gpc, "spriteMargin", IconFactory.NeonOutlineMargin);

            // round check box
            var box = new GameObject("Background", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(go.transform, false);
            var brt = (RectTransform)box.transform; brt.anchorMin = brt.anchorMax = new Vector2(0, 0.5f); brt.pivot = new Vector2(0, 0.5f);
            brt.anchoredPosition = new Vector2(22, 0); brt.sizeDelta = new Vector2(BoxSize, BoxSize);
            var bimg = box.GetComponent<Image>(); StyleRounded(bimg); bimg.raycastTarget = false;

            var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(box.transform, false);
            var cimg = check.GetComponent<Image>(); cimg.sprite = IconFactory.Check; cimg.type = Image.Type.Simple; cimg.preserveAspect = true; cimg.raycastTarget = false;
            var crt = (RectTransform)check.transform; crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.pivot = new Vector2(0.5f, 0.5f);
            crt.offsetMin = new Vector2(12, 12); crt.offsetMax = new Vector2(-12, -12);
            crt.localScale = Vector3.zero;                    // unticked: the animator snaps it in when the row is ticked

            var label = NewText(go.transform, "Label", "Option");
            SetText(label, null, OptionSize, TextAlignmentOptions.MidlineLeft);
            label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = true; label.fontSizeMin = 28; label.fontSizeMax = OptionSize;
            var lrt = label.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(22 + BoxSize + 22, 0); lrt.offsetMax = new Vector2(-24, 0);
            SceneBuilderCore.Themed(label.gameObject, ThemeRole.MainText);

            var tog = go.GetComponent<Toggle>();
            tog.targetGraphic = row; tog.transition = Selectable.Transition.None;
            tog.graphic = null;                               // the animator drives the checkmark (scale snap), not Toggle's fade
            tog.isOn = false;

            var anim = go.GetComponent<ChecklistRowAnimator>();
            SceneBuilderCore.SetField(anim, "rowBackground", row);
            SceneBuilderCore.SetField(anim, "checkBoxBackground", bimg);
            // New13 item 3: an unticked box has a visible border (ChecklistRowAnimator keeps its colour in step with the theme)
            var boxBorder = SceneBuilderCore.Ensure<Outline>(bimg.gameObject);
            boxBorder.effectDistance = new Vector2(2.5f, -2.5f); boxBorder.effectColor = new Color32(110, 98, 101, 255);
            SceneBuilderCore.SetField(anim, "checkmark", crt);
            SceneBuilderCore.SetField(anim, "glowOutline", glow);
            go.SetActive(false);        // template stays inactive; the rows are copies of it
            return tog;
        }
    }
}
