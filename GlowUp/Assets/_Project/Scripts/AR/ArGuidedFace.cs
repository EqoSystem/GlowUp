using System;
using GlowUp.Scan;
using UnityEngine;
using UnityEngine.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace GlowUp.AR
{
    /// <summary>
    /// GUIDED TRY-ON THAT FOLLOWS THE FACE, on every phone. Phones without ARCore face tracking used to get a fixed face outline with flat colour
    /// shapes: the user had to fit their face into it, and the colour looked pasted on. Now the phone's own face finder (MediaPipe Face Landmarker,
    /// FaceLandmarkReader) finds the 468 points of the face in the camera picture, and the make-up is drawn on those points, so it sits on the real
    /// lips, cheeks and eyelids and moves with the face, as live tracking does on ARCore phones. The colour TINTS the skin (shader
    /// GlowUp/UiMakeupBlend), so the skin's own light and texture stay visible.
    ///
    /// Every phone and every room is different, so this script adapts:
    ///  - the picture the finder gets and how often follow the time it really takes (GuidedFacePace), starting from the phone's AR pipeline
    ///    (light or full, ArAdaptiveQuality);
    ///  - the points are smoothed (One Euro filter): steady when the face is still, quick when it moves;
    ///  - the skin colour under the make-up is measured on the face itself, the highlight of a finish follows the room's brightness, and a dark
    ///    room or a face that is too far away gets a short hint.
    /// What to show (part, shade, strength, finish) still comes from FallbackMakeupOverlay through ArFaceMakeup, so the tabs and sliders work as
    /// before. When the face finder cannot start (Android older than 7, or a build without the library), the old guide stays.
    ///
    /// It is made and started at run time by ArFaceTrackingController (UseGuidedPreview); the numbers below can be changed on that object in Play
    /// mode or in the scene after it has been added once.
    /// </summary>
    public class ArGuidedFace : MonoBehaviour
    {
        [Header("Following the face")]
        [Tooltip("Size of the mask pictures, in pixels on each side (made once per visit)")]
        [Range(128, 512)] [SerializeField] private int maskSize = 256;
        [Tooltip("How steady the points are while the face is still (lower = steadier, with a little more delay)")]
        [Range(0.2f, 5f)] [SerializeField] private float steadiness = 1.2f;
        [Tooltip("How quickly the points follow a fast movement (higher = quicker)")]
        [Range(0f, 40f)] [SerializeField] private float quickness = 8f;
        [Tooltip("Seconds a lost face stays on screen while it fades out")]
        [Range(0.05f, 1.5f)] [SerializeField] private float fadeSeconds = 0.35f;
        [Tooltip("A face narrower than this share of the view counts as too far away")]
        [Range(0.05f, 0.5f)] [SerializeField] private float smallestFace = 0.18f;

        [Header("The room")]
        [SerializeField] private bool adaptSkinColour = true;
        [Tooltip("How far the assumed skin tone moves towards the measured one (0 = not at all, 1 = fully)")]
        [Range(0f, 1f)] [SerializeField] private float skinStrength = 0.6f;
        [Tooltip("How quickly the measured skin and brightness are followed")]
        [Min(0.1f)] [SerializeField] private float lightFollowSpeed = 3f;
        [Tooltip("The picture's brightness (0 to 1) at which the highlight is as the finish says")]
        [Range(0.1f, 0.9f)] [SerializeField] private float referenceBrightness = 0.45f;
        [Range(0f, 1f)] [SerializeField] private float darkestHighlight = 0.35f;
        [Range(1f, 2f)] [SerializeField] private float brightestHighlight = 1.15f;
        [Tooltip("The picture counts as dark below this brightness (0 to 1)")]
        [Range(0.02f, 0.5f)] [SerializeField] private float darkBelow = 0.2f;
        [Tooltip("The dark-room hint comes after this many seconds")]
        [Min(0f)] [SerializeField] private float darkHintAfter = 2.5f;

        [Header("Pigment (phase 3A, 11 November 2026)")]
        [Tooltip("On = a thin layer of the shade itself lies over the tint. The tint alone can at most double the skin's own colour, so a pale or "
               + "light shade on darker skin stayed dark and looked painted; the pigment carries it. Off in the lighter AR pipeline (one draw less per part).")]
        [SerializeField] private bool pigmentLayer = true;
        [Tooltip("How much of the shade itself covers the face (foundation): 0 = tint only")]
        [Range(0f, 0.6f)] [SerializeField] private float pigmentFace = 0.18f;
        [Tooltip("How much of the shade itself covers cheeks, eyes, lips and brows")]
        [Range(0f, 0.6f)] [SerializeField] private float pigmentParts = 0.22f;
        [Tooltip("Extra pigment for a shade much lighter than the skin under it (the tint cannot lighten that far)")]
        [Range(0f, 0.6f)] [SerializeField] private float pigmentForLighter = 0.3f;

        [Header("Wording")]
        [SerializeField] private string msgLoading = "Getting face finding ready...";
        [SerializeField] private string msgLooking = "Looking for your face. Hold the phone in front of you.";
        [SerializeField] private string msgFollowing = "The colour follows your face. Turn your head a little to see it from the side.";
        [SerializeField] private string msgLost = "Face lost for a moment. Look at the camera.";
        [SerializeField] private string msgTooFar = "Come a little closer to the camera.";
        [SerializeField] private string msgDark = "It is quite dark here. Face a window or a lamp for a steadier try-on.";

        /// <summary>True while the face finder runs and the make-up is drawn on the face (false = the old guide is shown).</summary>
        public bool Following { get; private set; }
        /// <summary>Raised when Following changes.</summary>
        public event Action<bool> FollowingChanged;

        const float LowLagBeta = 24f;
        static readonly int SkinBaseId = Shader.PropertyToID("_SkinBase");
        const int Points = FaceMeshData.PointCount;

        CameraFeedController feed;
        ArFaceMakeup makeup;
        FallbackMakeupOverlay choices;
        ArAdaptiveQuality quality;
        ArOneEuroSettings filterSettings;
        Transform layerParent;
        CanvasGroup[] oldVisuals;
        Action<string> say;

        FaceLandmarkReader reader;
        bool running, prepared, failed;
        bool smoothingEnabled = true, predictionEnabled = true, adaptiveQualityEnabled = true;
        int frameSkip, adaptiveFrameSkip, lastSubmittedFrame, lastObservedFrame, resolutionPreset = -1;
        bool adaptiveReduceHighlights;
        float frameTimeAverage, slowBudgetSeconds, fastBudgetSeconds;
        float readerBusySince = -1f, nextReaderRestartAt;
        int readerRecoveries;
        readonly GuidedFacePace pace = new GuidedFacePace();
        float nextLook;
        float submittedFrameTimestamp, resultFrameTimestamp;
        float lastInferenceMilliseconds, meshUpdateMilliseconds;
        readonly Vector3[] previousFiltered = new Vector3[Points];
        readonly Vector3[] velocity = new Vector3[Points];
        bool havePreviousFiltered;
        int[] pixels;
        int pictureW, pictureH;

        RectTransform root;
        ArGuidedFaceLayer[] layers;
        ArGuidedFaceLayer highlight;
        Texture2D[] colourMasks, highlightMasks;
        Material[] materials;
        Material shineMaterial;
        float shineSharpSent = -1f, shineSparkleSent = -1f;
        readonly Color[] skinSent = new Color[GuidedFaceShapes.LayerCount];
        Vector2[] meshUvs;
        int[] meshTriangles;

        ArGuidedFaceLayer[] pigments;                 // phase 3A: the thin layer of the shade itself over each tint (null in the lighter pipeline)
        readonly Vector3[] raw = new Vector3[Points];
        readonly OneEuroVertexFilter filter = new OneEuroVertexFilter();
        readonly Vector3[] shown = new Vector3[Points];
        Vector3[][] local;
        bool haveFace, everSeen;
        float lastAnswerAt, presence, lostFor, tooSmallFor, darkFor;
        float brightness = 0.5f, gainNow = 1f, skinAmount;
        Color skinNow = Color.white, skinTarget = Color.white;
        string lastSaid = "";

        public bool SmoothingEnabled => smoothingEnabled;
        public bool PredictionEnabled => predictionEnabled;
        public bool AdaptiveQualityEnabled => adaptiveQualityEnabled;
        public int FrameSkip => Mathf.Max(frameSkip, adaptiveFrameSkip);
        public int ManualFrameSkip => frameSkip;
        public int InferenceSide => pace.MaxSide;
        public string ResolutionLabel => resolutionPreset < 0 ? "Auto " + pace.ResolutionLabel :
            (resolutionPreset == 0 ? "Low " : resolutionPreset == 1 ? "Medium " : "High ") + pace.ResolutionLabel;
        public int CameraWidth => feed != null ? feed.PictureSize.x : 0;
        public int CameraHeight => feed != null ? feed.PictureSize.y : 0;
        public int CameraRequestedWidth => feed != null ? feed.RequestedSize.x : 0;
        public int CameraRequestedHeight => feed != null ? feed.RequestedSize.y : 0;
        public float CameraTimestampMs => feed != null ? feed.LastFrameTimestamp * 1000f : 0f;
        public float CameraFpsEstimate => feed != null ? feed.FrameRateEstimate : 0f;
        public float LandmarkTimestampMs => resultFrameTimestamp * 1000f;
        public float LandmarkAgeMs => resultFrameTimestamp > 0f ? Mathf.Max(0f, (Time.realtimeSinceStartup - resultFrameTimestamp) * 1000f) : 0f;
        public int LagFrames => Mathf.RoundToInt(LandmarkAgeMs * CameraFpsEstimate / 1000f);
        public float InferenceMilliseconds => lastInferenceMilliseconds;
        public float MeshUpdateMilliseconds => meshUpdateMilliseconds;
        public int ReaderRecoveries => readerRecoveries;
        public float MinCutoff => filterSettings != null ? filterSettings.minCutoff : steadiness;
        public float Beta => Mathf.Max(LowLagBeta, filterSettings != null ? filterSettings.beta : quickness);

        public void SetSmoothingEnabled(bool value) { smoothingEnabled = value; filter.Reset(); havePreviousFiltered = false; }
        public void SetPredictionEnabled(bool value) { predictionEnabled = value; }
        public void SetFrameSkip(int value) { frameSkip = Mathf.Clamp(value, 0, 2); }
        public void SetAdaptiveQualityEnabled(bool value)
        {
            adaptiveQualityEnabled = value;
            pace.SetAdaptiveEnabled(value);
            if (!value)
            {
                adaptiveFrameSkip = 0;
                adaptiveReduceHighlights = false;
                slowBudgetSeconds = fastBudgetSeconds = 0f;
            }
        }
        public void CycleResolutionPreset()
        {
            resolutionPreset = (resolutionPreset + 1) % 3;
            pace.SetManualIndex(resolutionPreset);
        }

        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// Starts following the face in the guided preview. `feed` = the guided camera; `parent` = where the layers go (inside the before/after
        /// clip); `hide` = the old guide's outline and shapes, hidden while the face is followed; `sayLine` = the status line.
        /// </summary>
        public void Begin(CameraFeedController feedCamera, ArFaceMakeup targetMakeup, ArAdaptiveQuality pipeline, Transform parent,
            GameObject[] hide, Action<string> sayLine, ArOneEuroSettings oneEuroSettings)
        {
            End();
            feed = feedCamera; makeup = targetMakeup; quality = pipeline; layerParent = parent; say = sayLine; filterSettings = oneEuroSettings;
            choices = makeup != null ? makeup.Choices : null;
            if (feed == null || makeup == null || choices == null || layerParent == null) { Debug.Log("[GuidedFace] not started: an object is missing."); return; }
            oldVisuals = new CanvasGroup[hide != null ? hide.Length : 0];
            for (int i = 0; i < oldVisuals.Length; i++)
            {
                if (hide[i] == null) continue;
                if (!hide[i].TryGetComponent(out CanvasGroup group)) group = hide[i].AddComponent<CanvasGroup>();
                oldVisuals[i] = group;
            }
            pace.Begin(quality != null && quality.IsLight);
            pace.SetAdaptiveEnabled(adaptiveQualityEnabled);
            if (resolutionPreset >= 0) pace.SetManualIndex(resolutionPreset);
            running = true; prepared = false; failed = false; haveFace = false; everSeen = false;
            presence = 0f; lostFor = 0f; tooSmallFor = 0f; darkFor = 0f; nextLook = 0f; lastSaid = "";
            lastSubmittedFrame = 0; lastObservedFrame = 0; submittedFrameTimestamp = resultFrameTimestamp = 0f;
            lastInferenceMilliseconds = meshUpdateMilliseconds = 0f; havePreviousFiltered = false;
            frameTimeAverage = 0f; slowBudgetSeconds = fastBudgetSeconds = 0f; adaptiveFrameSkip = 0; adaptiveReduceHighlights = false;
            readerBusySince = -1f; nextReaderRestartAt = 0f; readerRecoveries = 0;
            gainNow = 1f; skinAmount = 0f;
            _ = makeup.Layers;                     // ArFaceMakeup only works out the colours once its layers exist
            makeup.UseGloss = false;
            reader = FaceLandmarkReader.Begin(out string problem);
            if (reader == null) Fail(problem);
            else Say(msgLoading);
        }

        /// <summary>Stops following: the finder is closed, the layers and their pictures are removed and the old guide comes back.</summary>
        public void End()
        {
            bool was = Following;
            running = false;
            if (reader != null) { reader.Close(); reader = null; }
            if (root != null) { Destroy(root.gameObject); root = null; }
            layers = null; highlight = null; local = null; pigments = null;
            Release(colourMasks); Release(highlightMasks); colourMasks = highlightMasks = null;
            if (materials != null) foreach (Material m in materials) if (m != null) Destroy(m);
            materials = null;
            if (shineMaterial != null) Destroy(shineMaterial);
            shineMaterial = null;
            ShowOldGuide(true);
            if (makeup != null) { makeup.Presence = 1f; makeup.SkinAdapt = 0f; makeup.ShineGain = 1f; if (prepared) makeup.SetBrowMask(null); }
            prepared = false;
            submittedFrameTimestamp = resultFrameTimestamp = 0f;
            lastInferenceMilliseconds = meshUpdateMilliseconds = 0f;
            filterSettings = null;
            SetFollowing(false);
            if (was) Debug.Log("[GuidedFace] stopped.");
        }

        void OnDisable() { End(); }

        // ---------------------------------------------------------------------------------------
        void Update()
        {
            if (!running || failed || reader == null) return;
            switch (reader.State)
            {
                case FaceLandmarkReader.Status.Starting: return;
                case FaceLandmarkReader.Status.Failed: Fail(reader.Problem); return;
            }
            if (!prepared) Prepare();

            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            frameTimeAverage = frameTimeAverage <= 0f ? dt : Mathf.Lerp(frameTimeAverage, dt, 0.08f);
            AdaptToFrameTime(dt);
            if (reader.TryTake(out float[] answer))
            {
                readerBusySince = -1f;
                lastInferenceMilliseconds = reader.LastMilliseconds;
                resultFrameTimestamp = submittedFrameTimestamp;
                pace.Report(reader.LastMilliseconds);
                if (GuidedFaceShapes.IsFace(answer)) OnFace(answer, now);
                else OnNoFace();
            }
            bool readerBusy = reader.Busy;
            if (readerBusy)
            {
                if (readerBusySince < 0f) readerBusySince = now;
                else if (now - readerBusySince >= 2f && now >= nextReaderRestartAt)
                {
                    RecoverStuckReader(now);
                    return;
                }
            }
            else readerBusySince = -1f;
            bool newCameraFrame = feed != null && feed.FrameSequence > lastObservedFrame;
            if (newCameraFrame) lastObservedFrame = feed.FrameSequence;
            bool skipFrame = feed != null && feed.FrameSequence - lastSubmittedFrame <= FrameSkip;
            if (feed != null && feed.IsPlaying && newCameraFrame && !skipFrame && now >= nextLook && !readerBusy && feed.Grab()
                && feed.UprightArgb(null, pace.MaxSide, ref pixels, out pictureW, out pictureH) && reader.Look(pixels, pictureW, pictureH))
            {
                nextLook = now + pace.MinInterval;
                lastSubmittedFrame = feed.FrameSequence;
                submittedFrameTimestamp = feed.LastFrameTimestamp;
                MeasureRoom();
            }

            if (haveFace && LandmarkAgeMs > 120f)
            {
                haveFace = false;
                presence = 0f;
                makeup.Presence = 0f;
            }

            // Prediction advances the filtered result from its source camera frame to the currently displayed frame.
            if (haveFace)
            {
                Vector3[] target = filter.Values;
                // age of the camera frame the points came from + about one display frame; the One Euro filter's own lag is covered by a higher beta
                float predictionTime = predictionEnabled ? Mathf.Clamp(LandmarkAgeMs * 0.001f + 0.016f, 0f, 0.16f) : 0f;
                for (int i = 0; i < Points; i++)
                {
                    Vector3 delta = velocity[i] * predictionTime;
                    if (delta.sqrMagnitude > 0.0036f) delta = delta.normalized * 0.06f;
                    shown[i] = target[i] + delta;
                }
            }
            else lostFor += dt;
            bool showFace = everSeen && (haveFace || lostFor < fadeSeconds);
            presence = Mathf.MoveTowards(presence, haveFace ? 1f : 0f, dt / Mathf.Max(0.05f, fadeSeconds));
            if (!showFace) presence = 0f;
            makeup.Presence = presence;

            FollowRoom(dt);
            if (presence > 0.001f) DrawPoints();
            Colour();
            Speak();
        }

        // ---------------------------------------------------------------------------------------
        void Prepare()
        {
            prepared = true;
            float[] uvs = FaceMeshData.Uvs;
            meshUvs = new Vector2[Points];
            for (int i = 0; i < Points; i++) meshUvs[i] = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
            meshTriangles = GuidedFaceShapes.TrianglesWithoutHoles(FaceMeshData.Triangles);

            colourMasks = new Texture2D[GuidedFaceShapes.LayerCount];
            highlightMasks = new Texture2D[GuidedFaceShapes.LayerCount];
            for (int layer = 0; layer < GuidedFaceShapes.LayerCount; layer++)
            {
                colourMasks[layer] = FaceMaskPainter.ToAlphaTexture(GuidedFaceShapes.ColourMask(layer, maskSize, uvs), maskSize, "Guided face mask " + layer);
                if (layer != GuidedFaceShapes.Brows)
                    highlightMasks[layer] = FaceMaskPainter.ToAlphaTexture(GuidedFaceShapes.HighlightMask(layer, maskSize, uvs), maskSize, "Guided face highlight " + layer);
            }
            // the brow layer of ArFaceMakeup only shows once it has a mask: this one is the guided face's own
            makeup.SetBrowMask(colourMasks[GuidedFaceShapes.Brows]);

            var rootObject = new GameObject("GuidedFace (follows the face)", typeof(RectTransform));
            root = rootObject.GetComponent<RectTransform>();
            root.SetParent(layerParent, false);
            Stretch(root);
            root.SetAsLastSibling();

            Material template = choices.ShapeMaterial;
            Shader blend = template != null && template.shader != null && template.shader.name == "GlowUp/UiMakeupBlend" ? template.shader : Shader.Find("GlowUp/UiMakeupBlend");
            string[] names = { "Face", "Cheeks", "Eyes", "Lips", "Brows" };
            layers = new ArGuidedFaceLayer[GuidedFaceShapes.LayerCount];
            materials = new Material[GuidedFaceShapes.LayerCount];
            local = new Vector3[GuidedFaceShapes.LayerCount + 1][];
            bool withPigment = pigmentLayer && !(quality != null && quality.IsLight);
            pigments = withPigment ? new ArGuidedFaceLayer[GuidedFaceShapes.LayerCount] : null;
            for (int layer = 0; layer < GuidedFaceShapes.LayerCount; layer++)
            {
                materials[layer] = blend != null ? new Material(blend) { name = "Guided face " + names[layer] } : null;
                layers[layer] = MakeLayer(names[layer], colourMasks[layer], materials[layer]);
                // the pigment lies right over its tint, with the same feathered mask (the default UI material: an ordinary see-through colour)
                if (withPigment) pigments[layer] = MakeLayer(names[layer] + " Pigment", colourMasks[layer], null);
                local[layer] = new Vector3[Points];
            }
            // the highlight ADDS light through its own shader (a grey film of white paint made the face look like a ghost); the finish sets how sharp it is
            Shader shine = Shader.Find("GlowUp/UiShine");
            shineMaterial = shine != null ? new Material(shine) { name = "Guided face shine" } : null;
            shineSharpSent = shineSparkleSent = -1f;
            highlight = MakeLayer("Highlight", null, shineMaterial);
            local[GuidedFaceShapes.LayerCount] = new Vector3[Points];
            ShowOldGuide(false);
            SetFollowing(true);
            Say(msgLooking);
            Debug.Log("[GuidedFace] following the face (pipeline " + (quality != null && quality.IsLight ? "light" : "full") + ", pictures of " + pace.MaxSide + " px).");
        }

        ArGuidedFaceLayer MakeLayer(string name, Texture mask, Material material)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(ArGuidedFaceLayer));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            Stretch(rect);
            var layer = go.GetComponent<ArGuidedFaceLayer>();
            if (material != null) layer.material = material;
            layer.color = new Color(1f, 1f, 1f, 0f);
            layer.Setup(mask, meshUvs, meshTriangles);
            layer.enabled = false;
            return layer;
        }

        void OnFace(float[] answer, float now)
        {
            // too far away: a tiny face is followed badly, so the user is asked to come closer
            float width = Mathf.Abs(answer[GuidedFaceShapes.FaceSideOne * 3] - answer[GuidedFaceShapes.FaceSideTwo * 3]);
            if (width < smallestFace) { tooSmallFor += Mathf.Max(0.01f, now - lastAnswerAt); lastAnswerAt = now; OnNoFace(); return; }
            tooSmallFor = 0f;

            for (int i = 0; i < Points; i++) raw[i] = new Vector3(answer[i * 3], answer[i * 3 + 1], 0f);
            filter.minCutoff = filterSettings != null ? filterSettings.minCutoff : steadiness;
            // a floor on beta: with the older saved value (8) the mesh trailed the face when the head tilted or turned
            filter.beta = Mathf.Max(LowLagBeta, filterSettings != null ? filterSettings.beta : quickness);
            filter.derivativeCutoff = filterSettings != null ? filterSettings.derivativeCutoff : 1f;
            float dt = haveFace && lastAnswerAt > 0f ? Mathf.Clamp(now - lastAnswerAt, 0.005f, 0.5f) : 1f / 30f;
            if (!haveFace)
            {
                filter.Reset();
                havePreviousFiltered = false;
            }
            if (!smoothingEnabled) filter.Reset();
            filter.Step(raw, Points, dt);
            if (havePreviousFiltered)
            {
                for (int i = 0; i < Points; i++)
                    velocity[i] = (filter.Values[i] - previousFiltered[i]) / dt;
            }
            else Array.Clear(velocity, 0, velocity.Length);
            Array.Copy(filter.Values, previousFiltered, Points);
            havePreviousFiltered = true;
            if (!haveFace && presence <= 0.001f) Array.Copy(filter.Values, shown, Points);      // a face that comes back starts where it is, not where it was
            haveFace = true; everSeen = true; lostFor = 0f;
            lastAnswerAt = now;
        }

        void OnNoFace()
        {
            haveFace = false;
        }

        /// <summary>The smoothed points, in each layer's own coordinates (the view may be shown like a mirror).</summary>
        void DrawPoints()
        {
            RectTransform view = feed.Viewport;
            if (view == null || layers == null) return;
            long start = Stopwatch.GetTimestamp();
            Rect rect = view.rect;
            bool mirrored = feed.ShowsMirrored;
            for (int l = 0; l <= GuidedFaceShapes.LayerCount; l++)
            {
                ArGuidedFaceLayer layer = l < GuidedFaceShapes.LayerCount ? layers[l] : highlight;
                if (layer == null || !layer.enabled) continue;
                Matrix4x4 toLayer = layer.rectTransform.worldToLocalMatrix * view.localToWorldMatrix;
                Vector3[] points = local[l];
                for (int i = 0; i < Points; i++)
                {
                    Vector2 p = GuidedFaceShapes.ToView(shown[i].x, shown[i].y, rect, mirrored);
                    points[i] = toLayer.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f));
                }
                layer.SetPoints(points);
                if (pigments != null && l < GuidedFaceShapes.LayerCount && pigments[l] != null && pigments[l].enabled) pigments[l].SetPoints(points);
            }
            meshUpdateMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000f / Stopwatch.Frequency;
        }

        /// <summary>
        /// The thin layer of the shade itself (phase 3A): the shade's colour, with a share of the layer's strength. A shade much lighter than the
        /// skin under it gets more, because the tint under it can at most double the skin's colour (GuidedPigment.Share).
        /// </summary>
        Color PigmentColour(int layer, Color shade, Color skin)
        {
            float share = GuidedPigment.Share(shade, skin, layer == GuidedFaceShapes.Face ? pigmentFace : pigmentParts, pigmentForLighter);
            return new Color(shade.r, shade.g, shade.b, Mathf.Clamp01(shade.a * share));
        }

        /// <summary>Each layer takes the colour ArFaceMakeup works out (part, shade, strength, finish, presence); the highlight the finish's shine.</summary>
        void Colour()
        {
            if (layers == null) return;
            for (int l = 0; l < layers.Length; l++)
            {
                Color c = makeup.LayerColour(l);
                bool on = c.a > 0.004f && presence > 0.001f;
                ArGuidedFaceLayer pigment = pigments != null ? pigments[l] : null;
                if (pigment != null && pigment.enabled != on) pigment.enabled = on;
                if (layers[l].enabled != on) { layers[l].enabled = on; if (on) { skinSent[l] = Color.clear; DrawPoints(); } }     // switched on: a fresh stencil copy needs the skin colour again
                if (!on) continue;
                layers[l].color = c;
                // the camera view has a Mask, so the UI draws a stencil COPY of the material: the skin colour goes to that copy too
                Color skin = makeup.SkinUnderFor(l);
                if (pigment != null) pigment.color = PigmentColour(l, c, skin);
                if (materials[l] != null && !Same(skinSent[l], skin))
                {
                    skinSent[l] = skin;
                    materials[l].SetColor(SkinBaseId, skin);
                    Material drawn = layers[l].materialForRendering;
                    if (drawn != null && drawn != materials[l]) drawn.SetColor(SkinBaseId, skin);
                }
            }
            int active = makeup.ShownLayer;
            float strength = 0f;
            if (active >= 0 && active < GuidedFaceShapes.LayerCount && active != GuidedFaceShapes.Brows && highlightMasks[active] != null)
            {
                float twinkle = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * choices.TwinkleSpeed);
                strength = adaptiveReduceHighlights ? 0f :
                    Mathf.Clamp01(makeup.LayerColour(active).a * 1.2f) * (choices.FinishShine + choices.FinishGlitter * 0.6f * twinkle) * gainNow;
                highlight.SetMask(highlightMasks[active]);
            }

            bool shineOn = strength > 0.004f && presence > 0.001f;
            if (highlight.enabled != shineOn) { highlight.enabled = shineOn; if (shineOn) DrawPoints(); }
            if (shineOn)
            {
                Color tint = makeup.HighlightTint;
                highlight.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(strength * 0.5f));
                ShapeShine();
            }
        }

        /// <summary>Each finish has its own sheen: satin wide and soft, glossy a tight bright spot, shimmer glitter, metallic tinted and firm.</summary>
        void ShapeShine()
        {
            if (shineMaterial == null) return;
            float sharp, sparkle = 0f;
            switch (choices.FinishName)
            {
                case "satin": sharp = 0.7f; break;
                case "glossy": sharp = 2.6f; break;
                case "shimmer": sharp = 1.3f; sparkle = Mathf.Clamp01(choices.FinishGlitter); break;
                case "metallic": sharp = 1.1f; sparkle = Mathf.Clamp01(choices.FinishGlitter * 0.5f); break;
                default: sharp = 1f; break;
            }
            if (Mathf.Approximately(sharp, shineSharpSent) && Mathf.Approximately(sparkle, shineSparkleSent)) return;
            shineSharpSent = sharp; shineSparkleSent = sparkle;
            shineMaterial.SetFloat("_Sharpness", sharp); shineMaterial.SetFloat("_Sparkle", sparkle);
            Material drawn = highlight.materialForRendering;        // the camera view's Mask makes the UI draw a stencil copy
            if (drawn != null && drawn != shineMaterial) { drawn.SetFloat("_Sharpness", sharp); drawn.SetFloat("_Sparkle", sparkle); }
        }

        void AdaptToFrameTime(float dt)
        {
            if (!adaptiveQualityEnabled) return;
            if (frameTimeAverage > 0.04f)
            {
                slowBudgetSeconds += dt;
                fastBudgetSeconds = 0f;
                if (slowBudgetSeconds >= 0.75f)
                {
                    adaptiveReduceHighlights = true;
                    adaptiveFrameSkip = Mathf.Min(2, adaptiveFrameSkip + 1);
                    slowBudgetSeconds = 0f;
                }
            }
            else if (frameTimeAverage < 0.03f)
            {
                fastBudgetSeconds += dt;
                slowBudgetSeconds = 0f;
                if (fastBudgetSeconds >= 3f)
                {
                    adaptiveFrameSkip = Mathf.Max(0, adaptiveFrameSkip - 1);
                    if (adaptiveFrameSkip == 0) adaptiveReduceHighlights = false;
                    fastBudgetSeconds = 0f;
                }
            }
            else slowBudgetSeconds = fastBudgetSeconds = 0f;
        }

        void RecoverStuckReader(float now)
        {
            if (readerRecoveries >= 2)
            {
                Fail("face finding stopped responding after two restart attempts");
                return;
            }

            readerRecoveries++;
            reader.Close();
            reader = FaceLandmarkReader.Begin(out string problem);
            if (reader == null)
            {
                Fail(problem);
                return;
            }

            readerBusySince = -1f;
            nextReaderRestartAt = now + 5f;
            nextLook = now + 0.25f;
            submittedFrameTimestamp = resultFrameTimestamp = 0f;
            haveFace = false;
            presence = 0f;
            makeup.Presence = 0f;
            Debug.LogWarning("[GuidedFace] restarted a face inference that was busy for over 2 seconds (attempt " + readerRecoveries + ").");
        }

        // ---------------------------------------------------------------------------------------
        //  The room: brightness and skin colour, measured on the picture the finder was just given
        // ---------------------------------------------------------------------------------------
        void MeasureRoom()
        {
            if (pixels == null || pictureW <= 0 || pictureH <= 0) return;
            double sum = 0; int count = 0;
            int step = Mathf.Max(1, (pictureW * pictureH) / 2048);
            for (int i = 0; i < pictureW * pictureH; i += step) { int p = pixels[i]; sum += Luma((p >> 16) & 255, (p >> 8) & 255, p & 255); count++; }
            brightness = count > 0 ? (float)(sum / count) / 255f : 0.5f;
            if (brightness < darkBelow) darkFor += Mathf.Max(Time.unscaledDeltaTime, pace.MinInterval);
            else if (brightness > darkBelow + 0.05f) darkFor = 0f;

            if (!haveFace) return;
            // the skin, on the face itself (forehead, cheeks, chin), at the brightness the layers assume (its hue, not its brightness)
            double r = 0, g = 0, b = 0; int n = 0;
            Vector3[] at = filter.Values;
            foreach (int point in GuidedFaceShapes.SkinPoints)
            {
                int cx = Mathf.RoundToInt(at[point].x * (pictureW - 1)), cy = Mathf.RoundToInt(at[point].y * (pictureH - 1));
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= pictureW || y >= pictureH) continue;
                        int p = pixels[y * pictureW + x];
                        r += (p >> 16) & 255; g += (p >> 8) & 255; b += p & 255; n++;
                    }
            }
            if (n == 0) return;
            var sample = new Color((float)(r / n) / 255f, (float)(g / n) / 255f, (float)(b / n) / 255f, 1f);
            float have = Mathf.Max(0.02f, Luma(sample.r, sample.g, sample.b));
            float k = makeup.AssumedSkinBrightness / have;
            skinTarget = new Color(Mathf.Clamp01(sample.r * k), Mathf.Clamp01(sample.g * k), Mathf.Clamp01(sample.b * k), 1f);
        }

        void FollowRoom(float dt)
        {
            float t = 1f - Mathf.Exp(-lightFollowSpeed * dt);
            skinAmount = Mathf.Lerp(skinAmount, adaptSkinColour && haveFace ? skinStrength : 0f, t);
            skinNow = Color.Lerp(skinNow, skinTarget, t);
            makeup.SkinSample = skinNow;
            makeup.SkinAdapt = skinAmount;
            float gainTarget = Mathf.Clamp(brightness / Mathf.Max(0.05f, referenceBrightness), darkestHighlight, brightestHighlight);
            gainNow = Mathf.Lerp(gainNow, gainTarget, t);
            makeup.ShineGain = gainNow;
        }

        void Speak()
        {
            if (!Following) return;
            if (darkFor >= darkHintAfter) Say(msgDark);
            else if (tooSmallFor > 0.6f) Say(msgTooFar);
            else if (haveFace) Say(msgFollowing);
            else if (everSeen && lostFor > 0.6f) Say(msgLost);
            else if (!everSeen) Say(msgLooking);
        }

        // ---------------------------------------------------------------------------------------
        void Fail(string problem)
        {
            failed = true;
            Debug.Log("[GuidedFace] the face cannot be followed on this phone, the guide stays: " + problem);
            if (reader != null) { reader.Close(); reader = null; }
            ShowOldGuide(true);
            Say(problem);
            SetFollowing(false);
        }

        void ShowOldGuide(bool show)
        {
            if (oldVisuals == null) return;
            foreach (CanvasGroup group in oldVisuals) if (group != null) group.alpha = show ? 1f : 0f;
        }

        void SetFollowing(bool on)
        {
            if (Following == on) return;
            Following = on;
            FollowingChanged?.Invoke(on);
        }

        void Say(string text)
        {
            if (string.IsNullOrEmpty(text) || text == lastSaid) return;
            lastSaid = text;
            say?.Invoke(text);
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        }

        static void Release(Texture2D[] textures)
        {
            if (textures == null) return;
            foreach (Texture2D t in textures) if (t != null) Destroy(t);
        }

        static float Luma(float r, float g, float b) => 0.299f * r + 0.587f * g + 0.114f * b;

        static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;
    }
}
