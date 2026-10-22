using System;
using System.Collections;
using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Performance;
using GlowUp.Scan;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlowUp.AR
{
    /// <summary>
    /// Live face tracking for AR Try-On (Google ARCore through Unity's AR Foundation).
    ///
    /// When the screen opens it checks, in this order: is live tracking switched on, did the user allow
    /// the camera, does this phone support AR face tracking, does the AR camera really start. If every
    /// answer is yes, the front camera picture with the colour ON the user's own face (it follows the
    /// face as they move) is shown in the camera box. If any answer is no, the guided preview is used
    /// instead (the face outline the user lines up with), exactly as before. The user never sees an
    /// error for this: phones without AR support simply get the guided preview.
    ///
    /// The AR Session and XR Origin objects stay INACTIVE in the Hierarchy; this script switches them on.
    /// </summary>
    public class ArFaceTrackingController : MonoBehaviour
    {
        [Header("Live tracking objects (inactive in the Hierarchy until the phone has been checked)")]
        [SerializeField] private GameObject sessionObject;
        [SerializeField] private GameObject originObject;
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private ARFaceManager faceManager;
        [Tooltip("The colour layers that go on the tracked face")]
        [SerializeField] private ArFaceMakeup makeup;

        [Header("Adaptive quality and the helpers (each is its own object in the Hierarchy; all optional)")]
        [Tooltip("AR Quality Controller: decides the full or the light pipeline and holds the numbers of both")]
        [SerializeField] private ArAdaptiveQuality quality;
        [Tooltip("AR Light Adapter: makes the make-up follow the room (skin colour, highlight) and notices a dark room")]
        [SerializeField] private ArLightAdapter lightAdapter;
        [Tooltip("AR Face Hold: keeps a lost face on screen for a moment, predicts its movement and fades it out")]
        [SerializeField] private ArFaceHold faceHold;
        [Tooltip("AR Mouth Mask: keeps the make-up off the teeth and tongue when the mouth opens")]
        [SerializeField] private ArMouthMask mouthMask;

        [Header("Where the picture shows")]
        [SerializeField] private RawImage display;
        [SerializeField] private RectTransform viewport;
        [Tooltip("The guided preview flips this object for its selfie mirror; live tracking sets it straight")]
        [SerializeField] private RectTransform mirrorRoot;
        [Tooltip("The face outline and colour shapes of the guided preview. Hidden while live tracking runs.")]
        [SerializeField] private GameObject faceGuide;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Before and after")]
        [Tooltip("Shows the camera picture WITHOUT the colour, for the left side of the before/after split. Inactive until the split is on.")]
        [SerializeField] private RawImage beforeDisplay;
        [Tooltip("Tick this only if the 'before' side shows upside down on the phone")]
        [SerializeField] private bool flipBeforePicture = false;

        [Header("Guided preview (used whenever live tracking is not possible)")]
        [SerializeField] private CameraFeedController guidedCamera;

        [Header("Behaviour")]
        [Tooltip("Off = always use the guided preview")]
        [SerializeField] private bool useLiveTracking = true;
        [Tooltip("Off = phones in Performance Mode (Settings page) get the guided preview, which is lighter")]
        [SerializeField] private bool useInPerformanceMode = true;
        [Tooltip("If the phone supports AR but lacks Google Play Services for AR, offer to install it. Off = use the guided preview instead.")]
        [SerializeField] private bool offerToInstallArServices = true;
        [Tooltip("If the AR camera has not shown a picture after this many seconds, the guided preview is used")]
        [SerializeField] private float startTimeoutSeconds = 12f;
        [Tooltip("The live picture is drawn at most this many pixels high (lower = lighter for the phone)")]
        [SerializeField] private int maximumPictureHeight = 1280;
        [Tooltip("On = the camera is asked for its sharpest picture mode. Many phones start face tracking with a small 640 x 480 picture, which looks blocky.")]
        [SerializeField] private bool sharpestCameraMode = true;
        [Tooltip("The longest side of the camera picture that is asked for, in pixels (bigger is sharper but heavier for the phone)")]
        [SerializeField] private int longestCameraSide = 1920;
        [Tooltip("Camera modes slower than this many pictures per second are not chosen")]
        [SerializeField] private int slowestCameraRate = 30;
        [Tooltip("The status line says the face is lost after it has been out of view this long")]
        [SerializeField] private float faceLostAfterSeconds = 0.5f;
        [Tooltip("Ask the phone to keep the face sharp by itself (auto focus). Phones whose selfie camera has a fixed focus ignore it.")]
        [SerializeField] private bool autoFocus = true;
        [Tooltip("If live tracking ever closes the app while starting, it is switched off on that phone (guided preview instead) until the app is updated")]
        [SerializeField] private bool rememberCrashes = true;
        [Tooltip("Live tracking counts as working once it has run this many seconds")]
        [SerializeField] private float stableAfterSeconds = 4f;
        [Tooltip("The phone's question about the camera comes when the screen has been on show this long. Asked in the very first moment, it stood over a screen that was not drawn yet.")]
        [SerializeField] private float askAfterSeconds = 0.4f;

        [Header("Wording")]
        [SerializeField] private string msgChecking = "Getting the camera ready...";
        [SerializeField] private string msgInstalling = "Live try-on needs Google Play Services for AR. Follow the steps on screen.";
        [SerializeField] private string msgStarting = "Starting live try-on...";
        [SerializeField] private string msgLookingForFace = "Looking for your face. Hold the phone in front of you.";
        [SerializeField] private string msgTracking = "Live try-on. Turn your head to see the colour from the side.";
        [SerializeField] private string msgSwitchedOff = "Live try-on closed the app last time, so the guided preview is used on this phone.";
        [Tooltip("Said while the face is lost for a moment and the make-up is held on screen")]
        [SerializeField] private string msgFaceHeld = "Face lost for a moment. Turn back towards the camera.";

        /// <summary>True while the tracked face is being shown (false = guided preview, or not started).</summary>
        public bool IsLive { get; private set; }
        /// <summary>Raised once per start: true = live tracking, false = guided preview.</summary>
        public event Action<bool> ModeDecided;

        const string GuardName = "ar-live-tracking";
        bool guarding;
        float liveSince;

        RenderTexture picture, beforePicture;
        ARCameraBackground background;
        bool compare;
        Coroutine routine;
        int frames;
        bool subscribed, hadFace, saidAnything;
        bool usingLight, usingHold, usingMouth;     // which helpers were started for this live session
        int dressedLayer = int.MinValue;            // the colour layer the faces are dressed in now (light pipeline: only the shown one)
        string lastSaid = "";
        float lastSeen = -100f;
        Vector2 lastView;
        readonly List<Camera> otherCameras = new List<Camera>();    // the scene's own cameras: they must not draw the tracked face a second time
        readonly List<int> otherCameraMasks = new List<int>();

        // ---------------------------------------------------------------------------------------
        /// <summary>Called by the AR Try-On screen when it opens.</summary>
        public void Begin()
        {
            if (routine != null || IsLive) return;
            routine = StartCoroutine(Run());
        }

        /// <summary>Called when the user leaves the screen: the AR camera is released.</summary>
        public void StopTracking()
        {
            if (routine != null) { StopCoroutine(routine); routine = null; }
            EndGuard();
            StopLive();
        }

        void OnDisable() { StopTracking(); }

        /// <summary>The before/after split was switched on or off: while it is on, the plain camera picture is kept too.</summary>
        public void SetCompare(bool on)
        {
            compare = on;
            if (!on) ReleaseBefore();
        }

        /// <summary>Copies the camera picture, without the tracked face's colour, into the "before" picture.</summary>
        void DrawBefore()
        {
            if (beforeDisplay == null || picture == null || arCamera == null || display == null) return;
            if (background == null) background = arCamera.GetComponent<ARCameraBackground>();
            if (background == null || background.material == null) return;
            if (beforePicture == null)
            {
                beforePicture = new RenderTexture(picture.width, picture.height, 0) { name = "AR Try-On picture (before)" };
                beforePicture.Create();
                beforeDisplay.texture = beforePicture;
                beforeDisplay.color = Color.white;
            }
            Graphics.Blit(null, beforePicture, background.material);
            RectTransform from = display.rectTransform, to = beforeDisplay.rectTransform;
            to.anchorMin = to.anchorMax = to.pivot = new Vector2(0.5f, 0.5f);
            to.anchoredPosition = Vector2.zero; to.sizeDelta = from.sizeDelta;
            beforeDisplay.uvRect = flipBeforePicture ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
            if (!beforeDisplay.gameObject.activeSelf) beforeDisplay.gameObject.SetActive(true);
        }

        void ReleaseBefore()
        {
            if (beforeDisplay != null) { beforeDisplay.texture = null; beforeDisplay.gameObject.SetActive(false); }
            if (beforePicture != null) { beforePicture.Release(); Destroy(beforePicture); beforePicture = null; }
        }

        // leaving the app for a moment (a call, the home button, a permission question) is not a crash
        void OnApplicationPause(bool paused)
        {
            if (!guarding) return;
            if (paused) FeatureCrashGuard.Suspend(GuardName); else FeatureCrashGuard.Begin(GuardName);
        }

        void EndGuard()
        {
            if (!guarding) return;
            guarding = false;
            FeatureCrashGuard.Suspend(GuardName);
        }

        /// <summary>
        /// Google's AR service closes an app that was built as 32-bit only when it runs on a 64-bit phone
        /// (almost every phone today). Such a copy of the app must not start live tracking at all.
        /// </summary>
        static bool Is32BitAppOn64BitPhone()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (IntPtr.Size != 4) return false;
            try
            {
                using (var build = new AndroidJavaClass("android.os.Build"))
                {
                    string[] wide = build.GetStatic<string[]>("SUPPORTED_64_BIT_ABIS");
                    return wide != null && wide.Length > 0;
                }
            }
            catch (Exception e) { Debug.LogWarning("AR Try-On: could not read the phone's processor type: " + e.Message); return true; }
#else
            return false;
#endif
        }

        bool CanTryLive()
        {
            if (!useLiveTracking) return false;
            if (!useInPerformanceMode && PerformanceSettings.Mode == GraphicsMode.Performance) return false;
            if (quality != null && quality.PreferGuidedPreview) return false;
            return sessionObject != null && originObject != null && arCamera != null && cameraManager != null && faceManager != null
                && display != null && viewport != null && makeup != null && makeup.Ready;
        }

        IEnumerator Run()
        {
            if (quality != null) quality.BeginSession();         // which pipeline this phone gets (and the frame-rate cap of the light one)
            if (!CanTryLive()) { UseGuidedPreview("live tracking is switched off, or its objects are not set on this script"); yield break; }
            if (Is32BitAppOn64BitPhone())
            {
                UseGuidedPreview("this copy of the app is 32-bit and the phone is 64-bit; Google's AR service closes such apps (build with ARM64 ticked: GlowUp > Build Android APK does it)");
                yield break;
            }
            if (rememberCrashes && FeatureCrashGuard.IsBlocked(GuardName))
            {
                UseGuidedPreview("live tracking closed the app the last time it was started on this phone", msgSwitchedOff);
                yield break;
            }
            Say(msgChecking);

            // 1. the camera permission: one clear question before anything opens the camera
            bool allowed = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                // first the screen is drawn (with its card that says why the camera is needed), then the phone asks
                float shown = 0f;
                while (shown < askAfterSeconds || ScreenTransition.IsBusy) { if (!ScreenTransition.IsBusy) shown += Time.unscaledDeltaTime; yield return null; }

                bool answered = false, granted = false;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => { granted = true; answered = true; };
                callbacks.PermissionDenied += _ => { answered = true; };
                callbacks.PermissionDeniedAndDontAskAgain += _ => { answered = true; };
                Permission.RequestUserPermission(Permission.Camera, callbacks);
                float asked = 0f;
                while (!answered && asked < 60f) { asked += Time.unscaledDeltaTime; yield return null; }
                allowed = granted;
            }
#endif
            if (!allowed)
            {
                // the user has just answered "no": say so on the screen, do not ask a second time
                routine = null;
                if (quality != null) quality.EndSession();
                Debug.Log("[GlowUp] AR Try-On: the camera permission was not given.");
                if (faceGuide != null) faceGuide.SetActive(true);
                if (guidedCamera != null) guidedCamera.ReportPermissionRefused();
                ModeDecided?.Invoke(false);
                yield break;
            }

            // from here on the phone's AR service is used: if the app dies before live tracking is running, the next start knows
            if (rememberCrashes) { FeatureCrashGuard.Begin(GuardName); guarding = true; }

            // 2. can this phone track faces?
            if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
                yield return ARSession.CheckAvailability();
            if (ARSession.state == ARSessionState.NeedsInstall && offerToInstallArServices)
            {
                Say(msgInstalling);
                yield return ARSession.Install();
            }
            if (ARSession.state < ARSessionState.Ready) { UseGuidedPreview("this phone has no AR face tracking available (" + ARSession.state + ")"); yield break; }

            // 3. start the AR camera
            Say(msgStarting);
            bool prepared = false;
            try { PreparePicture(); Subscribe(); prepared = true; }
            catch (Exception e) { Debug.LogWarning("AR Try-On: live tracking could not be prepared: " + e.Message); }
            if (!prepared) { StopLive(); UseGuidedPreview("live tracking could not be prepared"); yield break; }

            frames = 0;
            cameraManager.autoFocusRequested = autoFocus;
            sessionObject.SetActive(true);
            originObject.SetActive(true);

            float waited = 0f;
            while (frames < 3 && waited < startTimeoutSeconds && ARSession.state >= ARSessionState.Ready) { waited += Time.unscaledDeltaTime; yield return null; }
            bool frontCamera = cameraManager.currentFacingDirection == CameraFacingDirection.User;
            bool canTrackFaces = faceManager.subsystem != null;
            if (frames < 3 || !frontCamera || !canTrackFaces)
            {
                string why = frames < 3 ? "the AR camera did not start in time" : !frontCamera ? "the phone did not give AR the front camera" : "the phone has no face tracking";
                StopLive();
                yield return null; yield return null;           // give the phone a moment to hand the camera back
                UseGuidedPreview(why);
                yield break;
            }

            // 4. live
            IsLive = true; hadFace = false; lastSeen = -100f; liveSince = Time.unscaledTime;
            StartHelpers();
            HideFaceFromOtherCameras();
            if (faceGuide != null) faceGuide.SetActive(false);
            display.color = Color.white;
            Say(msgLookingForFace);
            routine = null;
            ModeDecided?.Invoke(true);
        }

        // ---------------------------------------------------------------------------------------
        void UseGuidedPreview(string reason, string noteForTheUser = null)
        {
            routine = null;
            IsLive = false;
            EndGuard();
            if (quality != null) quality.EndSession();
            Debug.Log("[GlowUp] AR Try-On uses the guided preview: " + reason + ".");
            if (faceGuide != null) faceGuide.SetActive(true);
            if (guidedCamera != null) guidedCamera.StartCamera();
            ModeDecided?.Invoke(false);
            if (!string.IsNullOrEmpty(noteForTheUser) && isActiveAndEnabled) StartCoroutine(ShowNote(noteForTheUser));
        }

        // shown once the guided camera is running (its own start-up message comes first)
        IEnumerator ShowNote(string note)
        {
            float waited = 0f;
            while (guidedCamera != null && !guidedCamera.IsPlaying && waited < 10f) { waited += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(0.4f);
            if (!IsLive) Say(note);
        }

        void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            faceManager.facesChanged += OnFacesChanged;
            cameraManager.frameReceived += OnFrame;
        }

        void StopLive()
        {
            StopHelpers();
            if (subscribed)
            {
                subscribed = false;
                if (faceManager != null) faceManager.facesChanged -= OnFacesChanged;
                if (cameraManager != null) cameraManager.frameReceived -= OnFrame;
            }
            if (originObject != null) originObject.SetActive(false);
            if (sessionObject != null) sessionObject.SetActive(false);
            if (arCamera != null) arCamera.targetTexture = null;
            ReleaseBefore();
            for (int i = 0; i < otherCameras.Count; i++) if (otherCameras[i] != null) otherCameras[i].cullingMask = otherCameraMasks[i];
            otherCameras.Clear(); otherCameraMasks.Clear();
            if (picture != null)
            {
                if (display != null && display.texture == picture) { display.texture = null; display.color = new Color(0f, 0f, 0f, 0f); }
                picture.Release();
                Destroy(picture);
                picture = null;
            }
            IsLive = false;
        }

        void OnFrame(ARCameraFrameEventArgs args)
        {
            frames++;
            if (frames == 3 && sharpestCameraMode && !cameraModeChosen) ChooseCameraMode();
        }

        bool cameraModeChosen;

        /// <summary>
        /// Picks the camera's sharpest picture mode that is still smooth. Done once, after the first pictures have
        /// arrived (the list of modes is only known then). A phone that refuses simply keeps the mode it had.
        /// </summary>
        void ChooseCameraMode()
        {
            cameraModeChosen = true;
            try
            {
                using (var modes = cameraManager.GetConfigurations(Unity.Collections.Allocator.Temp))
                {
                    if (!modes.IsCreated || modes.Length == 0) return;
                    var current = cameraManager.currentConfiguration;
                    int best = -1; long bestPixels = current.HasValue ? (long)current.Value.width * current.Value.height : 0;
                    for (int i = 0; i < modes.Length; i++)
                    {
                        var mode = modes[i];
                        if (Mathf.Max(mode.width, mode.height) > Mathf.Max(640, longestCameraSide)) continue;
                        if (mode.framerate.HasValue && mode.framerate.Value < slowestCameraRate) continue;
                        long pixels = (long)mode.width * mode.height;
                        if (pixels > bestPixels) { bestPixels = pixels; best = i; }
                    }
                    if (best >= 0) cameraManager.currentConfiguration = modes[best];
                }
            }
            catch (System.Exception e) { Debug.LogWarning("AR Try-On: the camera kept its own picture mode: " + e.Message); }
        }

        /// <summary>
        /// The tracked face is a real 3D object, so every camera in the scene would draw it. Only the AR camera
        /// should: the scene's other cameras (the screen is a canvas, they have nothing else to draw) are told to
        /// draw nothing while live tracking runs, and get their setting back when it stops.
        /// </summary>
        void HideFaceFromOtherCameras()
        {
            foreach (Camera other in Camera.allCameras)
            {
                if (other == null || other == arCamera || otherCameras.Contains(other)) continue;
                otherCameras.Add(other); otherCameraMasks.Add(other.cullingMask);
                other.cullingMask = 0;
            }
        }

        // ---------------------------------------------------------------------------------------
        //  The picture: the AR camera draws into a texture with the screen's shape, and that texture
        //  fills the camera box (cut at the sides or top and bottom, never stretched).
        // ---------------------------------------------------------------------------------------
        void PreparePicture()
        {
            int pictureCap = quality != null ? quality.PictureHeight : maximumPictureHeight;
            int height = Mathf.Clamp(Screen.height, 320, Mathf.Max(320, pictureCap));
            int width = Mathf.Max(180, Mathf.RoundToInt(height * (Screen.width / (float)Mathf.Max(1, Screen.height))));
            picture = new RenderTexture(width, height, 24) { name = "AR Try-On picture", antiAliasing = 1 };
            picture.Create();
            arCamera.targetTexture = picture;

            display.texture = picture;
            display.uvRect = new Rect(0f, 0f, 1f, 1f);
            display.color = new Color(1f, 1f, 1f, 0f);          // shown once the first pictures have arrived
            display.enabled = true;
            if (mirrorRoot != null) mirrorRoot.localScale = Vector3.one;    // the AR picture is already a mirror image
            LayoutPicture(true);
        }

        void LayoutPicture(bool force)
        {
            if (picture == null || display == null || viewport == null) return;
            Vector2 view = viewport.rect.size;
            if (!force && Mathf.Approximately(view.x, lastView.x) && Mathf.Approximately(view.y, lastView.y)) return;
            lastView = view;
            float scale = Mathf.Max(view.x / picture.width, view.y / picture.height);
            RectTransform rt = display.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(picture.width * scale, picture.height * scale);
            rt.localEulerAngles = Vector3.zero;
            rt.localScale = Vector3.one;
        }

        // ---------------------------------------------------------------------------------------
        //  Faces
        // ---------------------------------------------------------------------------------------
        void OnFacesChanged(ARFacesChangedEventArgs args)
        {
            if (usingHold && faceHold != null) faceHold.Observe(args);
            if (args.added == null) return;
            foreach (ARFace face in args.added) Dress(face);
        }

        // ---------------------------------------------------------------------------------------
        //  The helpers of the full pipeline (light adaptation, face hold, mouth mask) and the light pipeline's savings
        // ---------------------------------------------------------------------------------------
        void StartHelpers()
        {
            usingLight = lightAdapter != null && (quality == null || quality.LightAdaptation);
            usingHold = faceHold != null && (quality == null || quality.HoldLostFace);
            usingMouth = mouthMask != null && (quality == null || quality.MouthMask);
            if (usingLight) lightAdapter.BeginSession(arCamera, makeup);
            if (usingHold) faceHold.BeginSession(faceManager, makeup);
            if (usingMouth) mouthMask.BeginSession(makeup);
            if (quality != null) quality.TierChanged += OnTierChanged;
            dressedLayer = int.MinValue;
        }

        void StopHelpers()
        {
            if (quality != null) { quality.TierChanged -= OnTierChanged; quality.EndSession(); }
            if (usingLight && lightAdapter != null) lightAdapter.EndSession();
            if (usingHold && faceHold != null) faceHold.EndSession();
            if (usingMouth && mouthMask != null) mouthMask.EndSession();
            usingLight = usingHold = usingMouth = false;
            if (makeup != null) makeup.Presence = 1f;
        }

        /// <summary>The frame-rate check moved this phone to the light pipeline while AR Try-On was open.</summary>
        void OnTierChanged()
        {
            if (!IsLive || quality == null) return;
            if (usingLight && !quality.LightAdaptation) { lightAdapter.EndSession(); usingLight = false; }
            if (usingMouth && !quality.MouthMask) { mouthMask.EndSession(); usingMouth = false; }
            if (picture != null)
            {
                RenderTexture old = picture;
                PreparePicture();                    // a smaller picture
                old.Release(); Destroy(old);
                if (compare) ReleaseBefore();        // the before picture is drawn again at the new size
            }
            dressedLayer = int.MinValue;             // every face is dressed again
        }

        /// <summary>In the light pipeline only the colour layer being tried is drawn: when another part is chosen, the faces get that layer.</summary>
        void RedressWhenLayerChanged()
        {
            if (quality == null || !quality.DrawOnlyShownLayer) return;
            if (makeup.ShownLayer == dressedLayer) return;
            dressedLayer = makeup.ShownLayer;
            foreach (ARFace f in faceManager.trackables) Dress(f);
        }

        /// <summary>Gives a newly found face its mesh and the four colour layers.</summary>
        void Dress(ARFace face)
        {
            if (face == null || makeup == null) return;
            GameObject go = face.gameObject;
            if (!go.TryGetComponent<MeshFilter>(out _)) go.AddComponent<MeshFilter>();
            if (!go.TryGetComponent(out MeshRenderer faceRenderer)) faceRenderer = go.AddComponent<MeshRenderer>();

            var layers = new List<Material>();
            foreach (Material layer in makeup.LayersFor(quality != null && quality.DrawOnlyShownLayer)) if (layer != null) layers.Add(layer);
            faceRenderer.sharedMaterials = layers.ToArray();    // one mesh, drawn once per layer
            faceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            faceRenderer.receiveShadows = false;
            faceRenderer.lightProbeUsage = LightProbeUsage.Off;
            faceRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            if (!go.TryGetComponent<ARFaceMeshVisualizer>(out _)) go.AddComponent<ARFaceMeshVisualizer>();   // keeps the mesh on the face
        }

        void Update()
        {
            if (!IsLive) return;
            LayoutPicture(false);
            if (compare) DrawBefore();
            if (guarding && Time.unscaledTime - liveSince >= stableAfterSeconds) { guarding = false; FeatureCrashGuard.Passed(GuardName); }

            // the AR camera was taken away (for example by a phone call): carry on with the guided preview
            if (ARSession.state < ARSessionState.Ready)
            {
                StopLive();
                UseGuidedPreview("the AR session stopped");
                return;
            }

            bool seen = false;
            ARFace tracked = null;
            foreach (ARFace face in faceManager.trackables)
                if (face != null && face.trackingState == TrackingState.Tracking) { seen = true; tracked = face; break; }
            if (seen) lastSeen = Time.unscaledTime;
            bool hasFace = Time.unscaledTime - lastSeen <= Mathf.Max(0f, faceLostAfterSeconds);

            // the helpers: light and mouth follow a tracked face; a lost face is held for a moment and fades
            if (usingLight) lightAdapter.FaceVisible = seen;
            if (usingMouth) { if (tracked != null) mouthMask.Step(tracked); else mouthMask.Lost(); }
            bool holding = false;
            if (usingHold) { faceHold.Step(); holding = faceHold.IsHolding; }
            RedressWhenLayerChanged();

            string say = hasFace ? msgTracking
                : holding ? msgFaceHeld
                : usingLight && lightAdapter.LowLight ? lightAdapter.LowLightMessage
                : msgLookingForFace;
            if (say != lastSaid || !saidAnything) { lastSaid = say; hadFace = hasFace; Say(say); }
        }

        void Say(string message)
        {
            saidAnything = true;
            if (statusText != null) statusText.text = message;
        }
    }
}
