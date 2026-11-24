using GlowUp.Common;
using TMPro;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// The one way the staff screens say something: the app's top toast (FormalToast), aligned and kept clear of buttons by
    /// its own placement rules. No message text boxes inside the screens, and nothing is written to the device log.
    /// </summary>
    public static class StaffToast
    {
        public static void Show(Component on, FormalToast.Kind kind, string message)
        {
            if (on == null || string.IsNullOrWhiteSpace(message)) return;
            // A staff controller sits at the scene root, outside any Canvas: GetComponentInParent found nothing and every staff toast was dropped
            // (no message after a wrong password, none from Find missing details). The scene's own canvas is used then.
            Canvas canvas = on.GetComponentInParent<Canvas>();
            if (canvas == null) canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null) return;
            TMP_Text font = canvas.rootCanvas.GetComponentInChildren<TMP_Text>(true);
            FormalToast toast = FormalToast.OnScreen(canvas.transform, font);
            if (toast != null) toast.Show(kind, message);
        }

        public static void Info(Component on, string message) => Show(on, FormalToast.Kind.Info, message);
        public static void Warn(Component on, string message) => Show(on, FormalToast.Kind.Warning, message);
        public static void Error(Component on, string message) => Show(on, FormalToast.Kind.Error, message);
        public static void Ok(Component on, string message) => Show(on, FormalToast.Kind.Success, message);
    }
}
