using System.Text;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// AR DEBUG OVERLAY: what live AR Try-On is doing, on the screen, for checking a change on a real phone in minutes:
    ///   frames per second, average / 95th-percentile / worst frame time, the pipeline (Full or Light, and a trial of Full),
    ///   the face tracking state (Tracking, Held, Lost), mesh smoothing, and the shade shown ("12A -> #B5463F" when the
    ///   product's shade code is known). A "Next test shade" button cycles through ArTestShades (offline, no database needed).
    ///
    /// ONLY in the Unity Editor and in development builds (Build Settings > Development Build, or GlowUp > Build Test APK):
    /// a release build never shows it. There it starts hidden unless the "Debug Overlay" tick on ArFaceTrackingController or
    /// PlayerPrefs int "GlowUp.ArDebug" = 1 is on; holding THREE fingers on the screen for one second shows or hides it.
    /// It costs one text redraw twice a second and no allocation per frame while hidden.
    /// </summary>
    public class ArDebugOverlay : MonoBehaviour
    {
        public const string FlagKey = "GlowUp.ArDebug";

        [Tooltip("Seconds between two updates of the numbers")]
        [Min(0.1f)] [SerializeField] private float refreshSeconds = 0.5f;
        [Tooltip("Seconds three fingers must stay on the screen to show or hide the overlay")]
        [Min(0.2f)] [SerializeField] private float holdSecondsToToggle = 1f;

        /// <summary>The shade shown now, as the AR screen describes it (set by ArTryOnFlowController).</summary>
        public static string ShadeNote = "";

        /// <summary>True where the overlay may exist at all: the Editor and development builds.</summary>
        public static bool Allowed => Application.isEditor || Debug.isDebugBuild;

        /// <summary>True when the overlay starts visible (the controller's tick or the PlayerPrefs flag), in an allowed build.</summary>
        public static bool FlagOn(bool controllerTick) => Allowed && (controllerTick || PlayerPrefs.GetInt(FlagKey, 0) == 1);

        ArAdaptiveQuality quality;
        ArLightAdapter lightAdapter;
        ArFaceTrackingController tracker;
        ArFaceMakeup makeup;
        readonly StringBuilder text = new StringBuilder(256);
        string shown = "";
        GUIStyle style, button;
        float since, sum, worst, fingersFor;
        int frames, testShade = -1;
        bool visible, toggledThisHold;

        /// <summary>Starts counting. Called when live tracking starts.</summary>
        public void Begin(ArAdaptiveQuality qualityController, ArLightAdapter light, ArFaceTrackingController trackingController, ArFaceMakeup faceMakeup, bool startVisible)
        {
            if (!Allowed) { enabled = false; return; }
            quality = qualityController; lightAdapter = light; tracker = trackingController; makeup = faceMakeup;
            since = sum = worst = 0f; frames = 0; shown = "";
            visible = startVisible;
            enabled = true;
        }

        /// <summary>Stops counting and hides the numbers.</summary>
        public void End() { enabled = false; shown = ""; }

        void Update()
        {
            WatchFingers();
            if (!visible) return;
            float dt = Time.unscaledDeltaTime;
            since += dt; sum += dt; frames++;
            if (dt > worst) worst = dt;
            if (since < refreshSeconds) return;

            float average = sum / Mathf.Max(1, frames);
            text.Length = 0;
            text.Append((1f / Mathf.Max(0.0001f, average)).ToString("0")).Append(" fps  ")
                .Append((average * 1000f).ToString("0.0")).Append(" ms  worst ").Append((worst * 1000f).ToString("0")).Append(" ms");
            if (quality != null)
            {
                text.Append("\n").Append(quality.Tier.ToString()).Append(" pipeline").Append(quality.InTrial ? " (trial)" : "")
                    .Append("  p95 ").Append(quality.LastWindow.p95Ms.ToString("0")).Append(" ms");
            }
            if (tracker != null) text.Append("\nFace: ").Append(tracker.TrackingStateText);
            if (quality != null) text.Append("  smoothing ").Append(quality.MeshSmoothing ? "on" : "off").Append("  gloss ").Append(makeup != null && makeup.UseGloss ? "moving" : "fixed");
            if (lightAdapter != null && lightAdapter.isActiveAndEnabled) text.Append("  light ").Append(lightAdapter.Brightness.ToString("0.00"));
            text.Append("\nShade: ").Append(string.IsNullOrEmpty(ShadeNote) ? "-" : ShadeNote);
            if (makeup != null && makeup.Choices != null) text.Append("  ").Append(makeup.Choices.Area).Append(", ").Append(makeup.Choices.FinishName);
            shown = text.ToString();
            since = sum = worst = 0f; frames = 0;
        }

        /// <summary>Three fingers held for a second: show or hide.</summary>
        void WatchFingers()
        {
            if (Input.touchCount >= 3)
            {
                fingersFor += Time.unscaledDeltaTime;
                if (fingersFor >= holdSecondsToToggle && !toggledThisHold) { visible = !visible; toggledThisHold = true; shown = ""; since = refreshSeconds; }
            }
            else { fingersFor = 0f; toggledThisHold = false; }
        }

        void OnGUI()
        {
            if (!visible || shown.Length == 0) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height / 60f)), fontStyle = FontStyle.Bold };
                style.normal.textColor = Color.yellow;
                button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height / 60f)) };
            }
            float top = Screen.safeArea.y + Screen.height * 0.01f;
            GUI.Label(new Rect(Screen.width * 0.03f, top, Screen.width * 0.94f, Screen.height * 0.16f), shown, style);
            if (makeup != null && makeup.Choices != null
                && GUI.Button(new Rect(Screen.width * 0.03f, top + Screen.height * 0.165f, Screen.width * 0.45f, Screen.height * 0.045f), "Next test shade", button))
                NextTestShade();
        }

        void NextTestShade()
        {
            testShade = (testShade + 1) % ArTestShades.All.Length;
            ArTestShades.Shade s = ArTestShades.All[testShade];
            FallbackMakeupOverlay choices = makeup.Choices;
            choices.SetArea(s.area);
            choices.SetFinish(s.finish);
            choices.SetShade(GlowUp.Scan.ShadeAdvisor.Parse(s.hex, Color.gray));
            choices.SetIntensity(s.coverage);
            ShadeNote = "test " + s.code + " " + s.name + " -> " + s.hex + " (" + s.undertone + ")";
            since = refreshSeconds;
        }
    }
}
