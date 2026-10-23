using System.Text;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// AR DEBUG OVERLAY: frames per second and frame time on the screen while live AR Try-On runs, for checking a change on a real phone.
    /// OFF unless the debug flag is on: the "Debug Overlay" tick on ArFaceTrackingController, or PlayerPrefs int "GlowUp.ArDebug" = 1
    /// (so a tester can switch it on without a new build). It costs one text redraw twice a second and no allocation per frame.
    /// </summary>
    public class ArDebugOverlay : MonoBehaviour
    {
        public const string FlagKey = "GlowUp.ArDebug";

        [Tooltip("Seconds between two updates of the numbers")]
        [Min(0.1f)] [SerializeField] private float refreshSeconds = 0.5f;

        ArAdaptiveQuality quality;
        ArLightAdapter light;
        readonly StringBuilder text = new StringBuilder(128);
        string shown = "";
        GUIStyle style;
        float since, sum, worst;
        int frames;

        /// <summary>True when the debug flag is on (the controller's tick or the PlayerPrefs flag).</summary>
        public static bool FlagOn(bool controllerTick) => controllerTick || PlayerPrefs.GetInt(FlagKey, 0) == 1;

        /// <summary>Starts counting. Called when live tracking starts.</summary>
        public void Begin(ArAdaptiveQuality qualityController, ArLightAdapter lightAdapter)
        {
            quality = qualityController; light = lightAdapter;
            since = sum = worst = 0f; frames = 0; shown = "";
            enabled = true;
        }

        /// <summary>Stops counting and hides the numbers.</summary>
        public void End() { enabled = false; shown = ""; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            since += dt; sum += dt; frames++;
            if (dt > worst) worst = dt;
            if (since < refreshSeconds) return;

            float average = sum / Mathf.Max(1, frames);
            text.Length = 0;
            text.Append((1f / Mathf.Max(0.0001f, average)).ToString("0")).Append(" fps  ")
                .Append((average * 1000f).ToString("0.0")).Append(" ms  worst ").Append((worst * 1000f).ToString("0")).Append(" ms");
            if (quality != null) text.Append("\n").Append(quality.Tier.ToString()).Append(" pipeline");
            if (light != null) text.Append("  light ").Append(light.Brightness.ToString("0.00"));
            shown = text.ToString();
            since = sum = worst = 0f; frames = 0;
        }

        void OnGUI()
        {
            if (shown.Length == 0) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height / 55f)), fontStyle = FontStyle.Bold };
                style.normal.textColor = Color.yellow;
            }
            GUI.Label(new Rect(Screen.width * 0.03f, Screen.safeArea.y + Screen.height * 0.01f, Screen.width * 0.94f, Screen.height * 0.12f), shown, style);
        }
    }
}
