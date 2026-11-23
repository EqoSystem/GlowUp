using GlowUp.Theme;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// The pink look of the staff scanner screens (the owner's mock-up of 23 Nov 2026): a soft pink page, white cards, pink actions and
    /// pink details instead of the app's cream and gold. It re-colours the LIGHT palette only while a staff screen is open (several staff
    /// screens may overlap for a moment while a scene changes, so it counts them); dark mode and every customer screen keep the app's own
    /// colours. Every staff object keeps its normal theme role: only the palette behind the roles changes.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class StaffSkin : MonoBehaviour
    {
        static int users;
        static ThemeProfile.Palette source, made;

        void Awake() { users++; ThemeManager.Skin = Make; }
        void Start() { ThemeManager.SkinChanged(); }

        void OnDestroy()
        {
            users = Mathf.Max(0, users - 1);
            if (users == 0) { ThemeManager.Skin = null; ThemeManager.SkinChanged(); }
        }

        static ThemeProfile.Palette Make(ThemeProfile.Palette p)
        {
            if (ReferenceEquals(p, source) && made != null) return made;
            var q = JsonUtility.FromJson<ThemeProfile.Palette>(JsonUtility.ToJson(p));
            q.background = new Color32(253, 239, 242, 255);        // soft pink page
            q.surface = new Color32(255, 250, 251, 255);           // the card
            q.surfaceAlt = new Color32(252, 228, 234, 255);        // pink rows and chips
            q.heading = new Color32(58, 42, 48, 255);
            q.mainText = new Color32(58, 42, 48, 255);
            q.secondaryText = new Color32(130, 106, 114, 255);
            q.buttonFill = new Color32(228, 98, 138, 255);         // the pink of the actions
            q.buttonText = new Color32(255, 255, 255, 255);
            q.inputBackground = new Color32(255, 255, 255, 255);
            q.inputText = new Color32(58, 42, 48, 255);
            q.inputPlaceholder = new Color32(176, 152, 160, 255);
            q.accent = new Color32(228, 98, 138, 255);
            q.accentText = new Color32(184, 56, 104, 255);         // pink that reads as small text on white
            q.gold = new Color32(222, 86, 128, 255);               // the logo and the thin frame are pink here
            q.border = new Color32(240, 201, 211, 255);
            source = p; made = q;
            return q;
        }
    }
}
