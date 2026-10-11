using GlowUp.Theme;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Preview Theme > Light / Dark. Recolours the open scene without entering Play mode,
    /// including the idle look of animated components (Continue button, checklist rows, borders ...).
    /// </summary>
    public static class ThemePreview
    {
        [MenuItem("GlowUp/Preview Theme/Light Mode")] static void Light() => Apply(false);
        [MenuItem("GlowUp/Preview Theme/Dark Mode")] static void Dark() => Apply(true);

        public static void Apply(bool dark)
        {
            var profile = SceneBuilderCore.EnsureThemeProfile();
            var p = profile.Get(dark);
            foreach (var g in Object.FindObjectsOfType<ThemedGraphic>(true))
            {
                g.ApplyPalette(p);
                var gr = g.GetComponent<UnityEngine.UI.Graphic>();
                if (gr != null) EditorUtility.SetDirty(gr);
            }
            foreach (var t in Object.FindObjectsOfType<ThemeAwareBehaviour>(true))
            {
                t.PreviewPalette(p);
                EditorUtility.SetDirty(t);
                foreach (var gr in t.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) EditorUtility.SetDirty(gr);
            }
            foreach (var f in Object.FindObjectsOfType<ThemedFont>(true)) f.Apply(profile);
            if (Camera.main != null) Camera.main.backgroundColor = p.background;
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }
    }
}
