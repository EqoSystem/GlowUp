using GlowUp.Common;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Tests
{
    /// <summary>
    /// The busy state of a pill button (icon + label side by side in one row): the spinner takes the icon's place as a member of the
    /// row, left of the words, so it never lies on the words (17 Nov 2026: "Identifying..." had the spinner over its first letters).
    /// </summary>
    public class ButtonBusyTests
    {
        GameObject canvas;

        [TearDown] public void TearDown() { if (canvas != null) Object.DestroyImmediate(canvas); }

        static Rect WorldRect(RectTransform t)
        {
            var c = new Vector3[4]; t.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        [Test]
        public void TheSpinnerOfAPillButtonNeverCoversItsWords()
        {
            canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(canvas.transform, false);
            ((RectTransform)buttonObject.transform).sizeDelta = new Vector2(500, 120);
            var rowObject = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowObject.transform.SetParent(buttonObject.transform, false);
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.anchorMin = Vector2.zero; rowRect.anchorMax = Vector2.one; rowRect.offsetMin = rowRect.offsetMax = Vector2.zero;
            var row = rowObject.GetComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter; row.spacing = 14;
            row.childControlWidth = row.childControlHeight = true; row.childForceExpandWidth = row.childForceExpandHeight = false;

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            icon.transform.SetParent(rowObject.transform, false);
            icon.GetComponent<LayoutElement>().preferredWidth = icon.GetComponent<LayoutElement>().preferredHeight = 40;
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(rowObject.transform, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "Take photo"; label.fontSize = 34; label.alignment = TextAlignmentOptions.Center;

            Button button = buttonObject.GetComponent<Button>();
            ButtonBusy.Set(button, label, true, "Identifying...", "Identifying…");
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
            Canvas.ForceUpdateCanvases();

            Transform spinner = rowObject.transform.Find("BusySpinner");
            Assert.IsNotNull(spinner, "the spinner is a member of the row");
            Assert.IsTrue(spinner.gameObject.activeSelf, "the spinner shows");
            Assert.IsFalse(icon.activeSelf, "the camera icon makes room for the spinner");
            Assert.Less(spinner.GetSiblingIndex(), labelObject.transform.GetSiblingIndex(), "the spinner stands left of the words");
            Rect s = WorldRect((RectTransform)spinner), l = WorldRect((RectTransform)labelObject.transform);
            Assert.LessOrEqual(s.xMax, l.xMin + 0.01f, "the spinner does not overlap the words' box");
            Assert.IsFalse(button.interactable, "a busy button cannot be pressed");

            ButtonBusy.Set(button, label, false, "Take photo");
            Assert.IsTrue(icon.activeSelf, "the icon is back when the button is idle again");
            Assert.IsFalse(spinner.gameObject.activeSelf, "the spinner is gone");
            Assert.AreEqual("Take photo", label.text);
            Assert.IsTrue(button.interactable);
        }
    }
}
