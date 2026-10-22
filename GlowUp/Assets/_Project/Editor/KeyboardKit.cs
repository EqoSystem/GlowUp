using GlowUp.Common;
using TMPro;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// The keyboard part of the shared last pass of every scene builder (ThemeKit.FinishScene): a scene with a text field gets
    /// ONE root object "Keyboard Avoider" (KeyboardAvoider), so every form behaves the same when the phone's keyboard comes up.
    /// Its numbers are kept across rebuilds (it is a root object, like the scenes' controllers).
    /// </summary>
    public static class KeyboardKit
    {
        public static void Ensure()
        {
            bool hasField = false;
            foreach (TMP_InputField field in Object.FindObjectsOfType<TMP_InputField>(true))
                if (field.gameObject.scene.IsValid()) { hasField = true; break; }
            if (!hasField) return;

            // One avoider per scene, however many times the builders run: keep the first one found, remove the others.
            KeyboardAvoider keep = null;
            foreach (KeyboardAvoider found in Object.FindObjectsOfType<KeyboardAvoider>(true))
            {
                if (!found.gameObject.scene.IsValid()) continue;
                if (keep == null) { keep = found; continue; }
                Object.DestroyImmediate(found);              // the component only; its object may hold other things
            }
            if (keep == null) SceneBuilderCore.Ensure<KeyboardAvoider>(SceneBuilderCore.EnsureRoot("Keyboard Avoider"));
        }

        /// <summary>
        /// Lists the badges and check rows that must stay visible with `field` while it is typed in. Only parts inside the field's
        /// own container are kept (its siblings and what is under them); the field, its container and its parents are dropped.
        /// </summary>
        public static void KeepVisible(TMP_InputField field, params Component[] parts)
        {
            if (field == null) return;
            Transform container = field.transform.parent;
            var list = new System.Collections.Generic.List<RectTransform>();
            foreach (Component part in parts)
            {
                if (part == null || !(part.transform is RectTransform rect)) continue;
                if (container == null || rect == field.transform || rect == container) continue;
                if (!rect.IsChildOf(container) || field.transform.IsChildOf(rect)) continue;     // outside the container, or a parent of the field
                if (!list.Contains(rect)) list.Add(rect);
            }
            SceneBuilderCore.Ensure<KeyboardKeepVisible>(field.gameObject).Set(list.ToArray());
            UnityEditor.EditorUtility.SetDirty(field.gameObject);
        }
    }
}
