using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.AR
{
    public class ArDebugOverlay : MonoBehaviour
    {
        public const string FlagKey = "GlowUp.ArDebug";

        [Min(0.1f)] [SerializeField] private float refreshSeconds = 0.25f;
        [Min(0.2f)] [SerializeField] private float holdSecondsToToggle = 1f;

        public static string ShadeNote = "";
        public static bool Allowed => Application.isEditor || Debug.isDebugBuild;
        public static bool FlagOn(bool controllerTick) => Allowed && (controllerTick || PlayerPrefs.GetInt(FlagKey, 0) == 1);

        ArAdaptiveQuality quality;
        ArLightAdapter lightAdapter;
        ArFaceTrackingController tracker;
        ArFaceMakeup makeup;
        ArGuidedFace guided;
        ArMeshSmoother meshSmoother;
        ArFaceHold faceHold;

        readonly StringBuilder text = new StringBuilder(512);
        GameObject canvasObject;
        GameObject ownedEventSystem;
        CanvasGroup panelGroup;
        TextMeshProUGUI metricsText;
        TextMeshProUGUI smoothingLabel;
        TextMeshProUGUI predictionLabel;
        TextMeshProUGUI resolutionLabel;
        TextMeshProUGUI skipLabel;
        TextMeshProUGUI adaptiveLabel;
        Button smoothingButton;
        Button predictionButton;
        Button resolutionButton;
        Button skipButton;
        Button adaptiveButton;
        Button copyButton;
        Button shadeButton;

        float since;
        float sum;
        float worst;
        float fingersFor;
        int frames;
        int testShade = -1;
        bool visible;
        bool toggledThisHold;

        public void Begin(ArAdaptiveQuality qualityController, ArLightAdapter light, ArFaceTrackingController trackingController,
            ArFaceMakeup faceMakeup, ArGuidedFace guidedFace, bool startVisible)
        {
            if (!Allowed) { enabled = false; return; }
            quality = qualityController;
            lightAdapter = light;
            tracker = trackingController;
            makeup = faceMakeup;
            guided = guidedFace;
            meshSmoother = tracker != null ? tracker.DebugMeshSmoother : null;
            faceHold = tracker != null ? tracker.DebugFaceHold : null;
            EnsureCanvas();
            ResetFrameMeasurements();
            SetVisible(startVisible);
            enabled = true;
        }

        public void End()
        {
            enabled = false;
            SetVisible(false);
            if (canvasObject != null) Destroy(canvasObject);
            if (ownedEventSystem != null) Destroy(ownedEventSystem);
            canvasObject = null;
            ownedEventSystem = null;
            panelGroup = null;
            metricsText = null;
            smoothingButton = predictionButton = resolutionButton = skipButton = adaptiveButton = copyButton = shadeButton = null;
            quality = null;
            lightAdapter = null;
            tracker = null;
            makeup = null;
            guided = null;
            meshSmoother = null;
            faceHold = null;
        }

        void OnDestroy()
        {
            if (canvasObject != null) Destroy(canvasObject);
            if (ownedEventSystem != null) Destroy(ownedEventSystem);
        }

        void Update()
        {
            WatchFingers();
            if (!visible) return;

            float dt = Time.unscaledDeltaTime;
            since += dt;
            sum += dt;
            frames++;
            if (dt > worst) worst = dt;
            if (since < refreshSeconds) return;

            RefreshMetrics();
            ResetFrameMeasurements();
        }

        void EnsureCanvas()
        {
            if (canvasObject != null) return;
            if (EventSystem.current == null)
                ownedEventSystem = new GameObject("AR Latency Debug Event System", typeof(EventSystem), typeof(StandaloneInputModule));

            canvasObject = new GameObject("AR Latency Debug Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            RectTransform panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(canvas.transform, false);
            panel.anchorMin = new Vector2(0.025f, 1f);
            panel.anchorMax = new Vector2(0.975f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -16f);
            panel.sizeDelta = new Vector2(0f, 840f);
            panelObject.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.06f, 0.94f);
            panelGroup = panelObject.GetComponent<CanvasGroup>();

            AddText(panel, "Title", "AR latency - development", new Vector2(26f, -20f), new Vector2(980f, 54f), 38f, TextAlignmentOptions.Left);
            metricsText = AddText(panel, "Metrics", "", new Vector2(26f, -82f), new Vector2(980f, 310f), 30f, TextAlignmentOptions.TopLeft);
            smoothingButton = AddButton(panel, "Smoothing", new Vector2(24f, -405f), new Vector2(480f, 68f), () => SetSmoothing(!SmoothingOn), out smoothingLabel);
            predictionButton = AddButton(panel, "Prediction", new Vector2(516f, -405f), new Vector2(480f, 68f), () => SetPrediction(!PredictionOn), out predictionLabel);
            resolutionButton = AddButton(panel, "Resolution", new Vector2(24f, -485f), new Vector2(972f, 68f), CycleResolution, out resolutionLabel);
            skipButton = AddButton(panel, "Frame skip", new Vector2(24f, -565f), new Vector2(972f, 68f), CycleFrameSkip, out skipLabel);
            adaptiveButton = AddButton(panel, "Adaptive quality", new Vector2(24f, -645f), new Vector2(972f, 68f), ToggleAdaptive, out adaptiveLabel);
            copyButton = AddButton(panel, "Copy values", new Vector2(24f, -725f), new Vector2(480f, 68f), CopyValues, out _);
            shadeButton = AddButton(panel, "Next test shade", new Vector2(516f, -725f), new Vector2(480f, 68f), NextTestShade, out _);
            RefreshButtons();
        }

        static TextMeshProUGUI AddText(RectTransform parent, string name, string value, Vector2 position, Vector2 size,
            float fontSize, TextAlignmentOptions alignment)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            TextMeshProUGUI label = child.GetComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = alignment;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            label.text = value;
            return label;
        }

        static Button AddButton(RectTransform parent, string name, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action,
            out TextMeshProUGUI label)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = child.GetComponent<Image>();
            image.color = new Color(0.17f, 0.2f, 0.25f, 1f);
            Button button = child.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            label = AddText(rect, "Label", name, new Vector2(12f, -8f), size - new Vector2(24f, 16f), 28f, TextAlignmentOptions.Center);
            return button;
        }

        bool SmoothingOn => guided != null ? guided.SmoothingEnabled : meshSmoother != null && meshSmoother.SmoothingEnabled;
        bool PredictionOn => guided != null ? guided.PredictionEnabled : faceHold != null && faceHold.PredictionEnabled;

        void SetSmoothing(bool on)
        {
            if (guided != null) guided.SetSmoothingEnabled(on);
            if (meshSmoother != null) meshSmoother.SetSmoothingEnabled(on);
            RefreshButtons();
        }

        void SetPrediction(bool on)
        {
            if (guided != null) guided.SetPredictionEnabled(on);
            if (faceHold != null) faceHold.SetPredictionEnabled(on);
            RefreshButtons();
        }

        void CycleResolution()
        {
            if (guided == null) return;
            guided.CycleResolutionPreset();
            RefreshButtons();
        }

        void CycleFrameSkip()
        {
            if (guided == null) return;
            guided.SetFrameSkip((guided.ManualFrameSkip + 1) % 3);
            RefreshButtons();
        }

        void ToggleAdaptive()
        {
            bool on = guided != null ? !guided.AdaptiveQualityEnabled : quality != null && !quality.AdaptiveEnabled;
            if (guided != null) guided.SetAdaptiveQualityEnabled(on);
            if (quality != null) quality.SetDebugAdaptiveEnabled(on);
            RefreshButtons();
        }

        void RefreshButtons()
        {
            if (smoothingLabel == null) return;
            smoothingLabel.text = "Smoothing: " + (SmoothingOn ? "ON" : "OFF");
            predictionLabel.text = "Prediction: " + (PredictionOn ? "ON" : "OFF");
            resolutionLabel.text = guided != null ? "Inference: " + guided.ResolutionLabel : "Inference resolution: N/A (ARCore)";
            skipLabel.text = guided != null
                ? "Frame skip: " + guided.ManualFrameSkip + " selected, " + guided.FrameSkip + " active"
                : "Frame skip: N/A (ARCore)";
            adaptiveLabel.text = "Adaptive quality: " + ((guided != null ? guided.AdaptiveQualityEnabled : quality != null && quality.AdaptiveEnabled) ? "ON" : "OFF");
            resolutionButton.interactable = guided != null;
            skipButton.interactable = guided != null;
            if (shadeButton != null) shadeButton.interactable = makeup != null && makeup.Choices != null;
        }

        void RefreshMetrics()
        {
            float average = sum / Mathf.Max(1, frames);
            text.Length = 0;
            text.Append((1f / Mathf.Max(0.0001f, average)).ToString("0")).Append(" fps  avg ").Append((average * 1000f).ToString("0.0"))
                .Append(" ms  frame ").Append((Time.unscaledDeltaTime * 1000f).ToString("0.0")).Append(" ms  worst ").Append((worst * 1000f).ToString("0.0")).Append(" ms")
                .Append("  target ").Append(Application.targetFrameRate).Append("  vSync ").Append(QualitySettings.vSyncCount);

            if (guided != null)
            {
                text.Append("\nPath: MediaPipe guided  camera ").Append(guided.CameraWidth).Append("x").Append(guided.CameraHeight)
                    .Append("  input ").Append(guided.InferenceSide).Append("px");
                text.Append("\nCamera ts ").Append(guided.CameraTimestampMs.ToString("0.0")).Append("ms  landmark ts ")
                    .Append(guided.LandmarkTimestampMs.ToString("0.0")).Append("ms  age ").Append(guided.LandmarkAgeMs.ToString("0.0"))
                    .Append("ms  lag ").Append(guided.LagFrames).Append(" frames at ").Append(guided.CameraFpsEstimate.ToString("0")).Append(" fps");
                text.Append("\nInference ").Append(guided.InferenceMilliseconds.ToString("0.0")).Append("ms  mesh update ")
                    .Append(guided.MeshUpdateMilliseconds.ToString("0.00")).Append("ms");
                text.Append("\nSmoothing ").Append(guided.SmoothingEnabled ? "ON" : "OFF").Append("  One Euro minCutoff ")
                    .Append(guided.MinCutoff.ToString("0.00")).Append("  beta ").Append(guided.Beta.ToString("0.00"))
                    .Append("  prediction ").Append(guided.PredictionEnabled ? "ON" : "OFF").Append("  skip ").Append(guided.FrameSkip);
            }
            else if (tracker != null && tracker.IsLive)
            {
                text.Append("\nPath: AR Foundation / ARCore  camera ts ").Append(tracker.CameraTimestampMs.ToString("0.0"))
                    .Append("ms  face-update event ts ").Append(tracker.FaceResultTimestampMs.ToString("0.0")).Append("ms  age ~")
                    .Append(tracker.FaceResultAgeMs.ToString("0.0")).Append("ms  lag ~").Append(tracker.FaceFrameLag).Append(" frames");
                text.Append("\nInference: N/A (native ARCore)  mesh update ").Append(meshSmoother != null ? meshSmoother.LastUpdateMilliseconds.ToString("0.00") : "N/A").Append("ms");
                text.Append("\nSmoothing ").Append(SmoothingOn ? "ON" : "OFF");
                if (meshSmoother != null) text.Append("  One Euro minCutoff ").Append(meshSmoother.MinCutoff.ToString("0.00")).Append("  beta ").Append(meshSmoother.Beta.ToString("0.00"));
                text.Append("  prediction ").Append(PredictionOn ? "ON" : "OFF (held face only)");
            }
            else text.Append("\nPath: waiting for AR tracking mode");

            if (quality != null)
                text.Append("\nQuality ").Append(quality.Tier).Append("  p95 ").Append(quality.LastWindow.p95Ms.ToString("0")).Append("ms  light ").Append(quality.IsLight);
            if (lightAdapter != null && lightAdapter.isActiveAndEnabled) text.Append("  light ").Append(lightAdapter.Brightness.ToString("0.00"));
            if (!string.IsNullOrEmpty(ShadeNote)) text.Append("\nShade: ").Append(ShadeNote);
            if (makeup != null && makeup.Choices != null) text.Append("  ").Append(makeup.Choices.Area).Append(", ").Append(makeup.Choices.FinishName);
            metricsText.text = text.ToString();
            RefreshButtons();
        }

        void CopyValues()
        {
            RefreshMetrics();
            GUIUtility.systemCopyBuffer = text.ToString();
            Debug.Log("[ARLatency] Debug values copied to the clipboard.");
        }

        void SetVisible(bool on)
        {
            visible = on;
            if (panelGroup == null) return;
            panelGroup.alpha = on ? 1f : 0f;
            panelGroup.interactable = on;
            panelGroup.blocksRaycasts = on;
            if (on) ResetFrameMeasurements();
        }

        void ResetFrameMeasurements() { since = sum = worst = 0f; frames = 0; }

        void WatchFingers()
        {
            if (Input.touchCount >= 3)
            {
                fingersFor += Time.unscaledDeltaTime;
                if (fingersFor >= holdSecondsToToggle && !toggledThisHold)
                {
                    SetVisible(!visible);
                    toggledThisHold = true;
                }
            }
            else { fingersFor = 0f; toggledThisHold = false; }
        }

        void NextTestShade()
        {
            if (makeup == null || makeup.Choices == null) return;
            testShade = (testShade + 1) % ArTestShades.All.Length;
            ArTestShades.Shade shade = ArTestShades.All[testShade];
            FallbackMakeupOverlay choices = makeup.Choices;
            choices.SetArea(shade.area);
            choices.SetFinish(shade.finish);
            choices.SetShade(GlowUp.Scan.ShadeAdvisor.Parse(shade.hex, Color.gray));
            choices.SetIntensity(shade.coverage);
            ShadeNote = "test " + shade.code + " " + shade.name + " -> " + shade.hex + " (" + shade.undertone + ")";
        }
    }
}
