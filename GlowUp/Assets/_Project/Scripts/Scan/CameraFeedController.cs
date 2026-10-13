using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlowUp.Scan
{
    /// <summary>
    /// Shows the phone camera (back or front) inside a viewport and keeps it the right way up on
    /// every device: it handles the sensor's rotation, upside-down sensors, the selfie mirror, and
    /// fills the viewport without stretching. It can also hand back an upright, cropped picture of
    /// whatever is inside a given frame - that is what the text reader uses.
    /// The correction switches are for the rare phone whose camera reports its orientation wrongly.
    /// </summary>
    public class CameraFeedController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The RawImage that shows the camera")]
        [SerializeField] private RawImage display;
        [Tooltip("The area the camera picture has to fill")]
        [SerializeField] private RectTransform viewport;
        [Tooltip("Parent of the RawImage; it is flipped left-right for the selfie mirror")]
        [SerializeField] private RectTransform mirrorRoot;

        [Header("Camera")]
        [Tooltip("Off = back camera (scanning). On = front camera (AR Try-On).")]
        [SerializeField] private bool useFrontCamera = false;
        [SerializeField] private int requestedWidth = 1280;
        [SerializeField] private int requestedHeight = 720;
        [SerializeField] private int requestedFps = 30;

        [Tooltip("The phone's question about the camera comes when the screen has been on show this long. Asked in the very first moment, it stood over a screen that was not drawn yet.")]
        [SerializeField] private float askAfterSeconds = 0.35f;

        [Header("Corrections (only if a phone shows the picture wrongly)")]
        [Tooltip("Show the front camera like a mirror")]
        [SerializeField] private bool mirrorFrontCamera = true;
        [Tooltip("Extra clockwise turn in degrees: 0, 90, 180 or 270")]
        [SerializeField] private int extraRotationDegrees = 0;
        [SerializeField] private bool flipVertical = false;

        [Header("Stabiliser (steadies the picture against a shaking hand)")]
        [Tooltip("On = the picture on screen is moved against the phone's small shakes, so text stays readable while it is held by hand. It uses the phone's motion sensor; a phone without one simply shows the picture as it is.")]
        [SerializeField] private bool stabilise = true;
        [Tooltip("How much the picture is enlarged to have room to move (1.08 = 8%). The edges that are cut off are that room.")]
        [Range(1f, 1.3f)] [SerializeField] private float stabiliserZoom = 1.08f;
        [Tooltip("How strongly a shake is countered (1 = fully, 0 = not at all)")]
        [Range(0f, 1.5f)] [SerializeField] private float stabiliserStrength = 1f;
        [Tooltip("How quickly the picture drifts back to the middle (higher = follows a deliberate move of the phone sooner)")]
        [SerializeField] private float stabiliserReturnSpeed = 3.5f;
        [Tooltip("The camera's field of view from top to bottom, in degrees (how far the picture moves for a given turn of the phone)")]
        [Range(30f, 100f)] [SerializeField] private float stabiliserFieldOfView = 62f;
        [Tooltip("Swap the direction left-right / up-down if the picture moves WITH the shake on some phone")]
        [SerializeField] private bool stabiliserFlipX = false;
        [SerializeField] private bool stabiliserFlipY = false;

        [Header("Focus")]
        [Tooltip("The camera keeps the picture sharp by itself. When a phone has several cameras on one side, the main one that can focus is chosen (not the ultra-wide, zoom or depth camera).")]
        [SerializeField] private bool autoFocus = true;
        [Tooltip("Tapping the camera picture makes the camera focus on the spot that was tapped")]
        [SerializeField] private bool tapToFocus = true;
        [Tooltip("The circle that shows where the camera is focusing (a child of the viewport, hidden until a tap). Optional.")]
        [SerializeField] private RectTransform focusRing;
        [Tooltip("Seconds the circle stays after a tap")]
        [SerializeField] private float focusRingSeconds = 1.1f;
        [Tooltip("The circle starts this much bigger and closes in on the spot")]
        [SerializeField] private float focusRingStartScale = 1.6f;
        [Tooltip("After a tap (or Refocus) the camera goes back to focusing by itself after this many seconds. Not while the focus is locked.")]
        [SerializeField] private float refocusHoldSeconds = 2.5f;
        [Tooltip("FOCUS LOCK. On = a tap on the picture focuses on that spot and KEEPS the focus there until it is released (ReleaseFocus, the padlock button of the scan screen). Off = after a tap the camera goes back to focusing by itself.")]
        [SerializeField] private bool tapLocksFocus = false;

        public bool IsPlaying => cam != null && cam.isPlaying && cam.width > 32;
        /// <summary>The size of the picture the camera really gives, in its own pixels (it may differ from the size that was asked for). 0 x 0 while it is off.</summary>
        public Vector2Int PictureSize => IsPlaying ? new Vector2Int(cam.width, cam.height) : Vector2Int.zero;
        public bool IsFrontCamera { get; private set; }
        /// <summary>Raised once: true + "" when the camera is running, false + reason when it cannot start.</summary>
        public event Action<bool, string> Started;
        /// <summary>Raised when the camera was running and is closed (its flashlight goes out with it).</summary>
        public event Action Stopped;

        WebCamTexture cam;
        Color32[] buffer;
        int lastW, lastH, lastAngle = -1;
        bool lastMirrored;
        float[] steadyPrevious;

        // ---------------------------------------------------------------------------------------
        public void StartCamera()
        {
            if (cam != null) return;
            StartCoroutine(StartRoutine());
        }

        /// <summary>
        /// For a screen that has already asked for the camera permission itself and was refused a moment ago:
        /// tells the screen so (the same message as a refusal here) without asking the user a second time.
        /// </summary>
        public void ReportPermissionRefused()
        {
            Started?.Invoke(false, "Camera permission is needed. You can allow it in your phone's settings.");
        }

        IEnumerator StartRoutine()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                // first the screen is drawn, then the phone asks
                float shown = 0f;
                while (shown < askAfterSeconds || GlowUp.Common.ScreenTransition.IsBusy) { if (!GlowUp.Common.ScreenTransition.IsBusy) shown += Time.unscaledDeltaTime; yield return null; }

                bool answered = false, granted = false;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => { granted = true; answered = true; };
                callbacks.PermissionDenied += _ => { answered = true; };
                callbacks.PermissionDeniedAndDontAskAgain += _ => { answered = true; };
                Permission.RequestUserPermission(Permission.Camera, callbacks);
                float waited = 0f;
                while (!answered && waited < 60f) { waited += Time.unscaledDeltaTime; yield return null; }
                if (!granted) { Started?.Invoke(false, "Camera permission is needed. You can allow it in your phone's settings."); yield break; }
            }
#else
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
                if (!Application.HasUserAuthorization(UserAuthorization.WebCam)) { Started?.Invoke(false, "Camera permission is needed."); yield break; }
            }
#endif
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0) { Started?.Invoke(false, "No camera was found on this device."); yield break; }

            // the best camera on the wanted side: the main (wide) one, and one that can focus
            int chosen = 0, bestScore = int.MinValue; bool found = false;
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].isFrontFacing != useFrontCamera) continue;
                int score = 0;
                if (autoFocus)
                {
                    switch (devices[i].kind)
                    {
                        case WebCamKind.WideAngle: score += 4; break;
                        case WebCamKind.ColorAndDepth: score += 1; break;
                        case WebCamKind.UltraWideAngle: score -= 2; break;
                        case WebCamKind.Telephoto: score -= 3; break;
                    }
                    if (devices[i].isAutoFocusPointSupported) score += 3;
                }
                if (score > bestScore) { bestScore = score; chosen = i; found = true; }      // the first one wins a tie
            }
            IsFrontCamera = found ? useFrontCamera : devices[chosen].isFrontFacing;
            canRefocus = devices[chosen].isAutoFocusPointSupported;

            cam = new WebCamTexture(devices[chosen].name, requestedWidth, requestedHeight, requestedFps);
            cam.Play();

            // the texture reports 16x16 until the first real frame arrives
            float t = 0f;
            while (cam != null && cam.width <= 32 && t < 8f) { t += Time.unscaledDeltaTime; yield return null; }
            if (cam == null) yield break;
            if (cam.width <= 32) { StopCamera(); Started?.Invoke(false, "The camera did not start. Close other apps that use it and try again."); yield break; }

            if (display != null) { display.texture = cam; display.enabled = true; display.color = Color.white; }
            Layout(true);
            Started?.Invoke(true, "");
        }

        public void StopCamera()
        {
            StopAllCoroutines();
            bool wasOpen = cam != null;
            if (cam != null) { if (cam.isPlaying) cam.Stop(); Destroy(cam); cam = null; }
            if (display != null) display.texture = null;
            lastAngle = -1; steadyPrevious = null; refocusUntil = 0f;
            // a camera that is opened again focuses by itself again: the lock ends with the camera
            if (FocusLocked) { FocusLocked = false; FocusLockChanged?.Invoke(false, false); }
            if (wasOpen)
            {
                GlowUp.Common.Flashlight.CameraStopped();
                Stopped?.Invoke();
            }
        }

        void OnDisable() { StopCamera(); }

        void Update()
        {
            if (!IsPlaying) return;
            Layout(false);
            Stabilise();

            // back to focusing by itself after a tap / Refocus
            if (refocusUntil > 0f && Time.unscaledTime >= refocusUntil)
            {
                refocusUntil = 0f;
                try { cam.autoFocusPoint = null; } catch (Exception) { }
            }
            if (tapToFocus && autoFocus && viewport != null && Input.GetMouseButtonDown(0)
                && RectTransformUtility.RectangleContainsScreenPoint(viewport, Input.mousePosition, null)
                && OnThePictureItself(Input.mousePosition)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, Input.mousePosition, null, out Vector2 tapped))
                FocusAt(tapped);
            ShowFocusRing();
        }

        float ringT = -1f;
        static readonly List<RaycastResult> tapHits = new List<RaycastResult>();

        /// <summary>
        /// True when a tap at this place of the screen lands on the camera picture itself, not on a button or a message
        /// that lies on it (the flashlight, the padlock, a question) and not on a pop-up over the screen. Such a tap must
        /// not move the focus to the corner where the button happens to be.
        /// </summary>
        bool OnThePictureItself(Vector2 screenPoint)
        {
            EventSystem events = EventSystem.current;
            if (events == null) return true;
            tapHits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = screenPoint }, tapHits);
            if (tapHits.Count == 0) return true;
            // what the finger touches first
            Transform top = tapHits[0].gameObject.transform;
            if (top == viewport || (display != null && top == display.transform)) return true;
            if (top.IsChildOf(viewport)) return false;                       // a button or a message on the picture
            // Something outside the camera box. It is the picture when that thing lies BEHIND the box (the page shows
            // through, the box itself does not catch taps), and not when it lies OVER it (a pop-up, a change of screen).
            Graphic box = viewport.GetComponent<Graphic>(), hit = top.GetComponent<Graphic>();
            if (box == null || hit == null || box.canvas == null || hit.canvas == null) return viewport.IsChildOf(top);
            Canvas boxRoot = box.canvas.rootCanvas, hitRoot = hit.canvas.rootCanvas;
            if (boxRoot != hitRoot) return hitRoot.sortingOrder < boxRoot.sortingOrder;
            return hit.depth < box.depth;
        }

        /// <summary>Focuses on a spot of the viewport (its own coordinates) and shows the circle there.</summary>
        void FocusAt(Vector2 local)
        {
            Rect area = viewport.rect;
            // the spot as a share of the picture the user sees (0,0 = bottom left)
            var seen = new Vector2(Mathf.InverseLerp(area.xMin, area.xMax, local.x), Mathf.InverseLerp(area.yMin, area.yMax, local.y));
            if (focusRing != null)
            {
                focusRing.gameObject.SetActive(true);
                focusRing.anchorMin = focusRing.anchorMax = focusRing.pivot = new Vector2(0.5f, 0.5f);
                focusRing.anchoredPosition = local - area.center;
                ringT = 0f;
            }
            if (tapLocksFocus) LockFocus(ToCameraPoint(seen), true);
            else Refocus(ToCameraPoint(seen));
        }

        /// <summary>A spot of the picture as the user sees it -> the same spot in the camera's own picture (which may lie on its side or be mirrored).</summary>
        Vector2 ToCameraPoint(Vector2 seen)
        {
            Vector2 p = seen - new Vector2(0.5f, 0.5f);
            if (IsFrontCamera && mirrorFrontCamera) p.x = -p.x;
            float a = Angle * Mathf.Deg2Rad;                         // the picture was turned by -Angle to stand upright: turn the spot back
            float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            p = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
            if (Mirrored) p.y = -p.y;
            return new Vector2(Mathf.Clamp01(p.x + 0.5f), Mathf.Clamp01(p.y + 0.5f));
        }

        void ShowFocusRing()
        {
            if (focusRing == null || ringT < 0f) return;
            ringT += Time.unscaledDeltaTime / Mathf.Max(0.1f, focusRingSeconds);
            float closing = Mathf.Clamp01(ringT * 3f);               // closes in during the first third, then holds, then goes
            focusRing.localScale = Vector3.one * Mathf.Lerp(Mathf.Max(1f, focusRingStartScale), 1f, 1f - (1f - closing) * (1f - closing));
            if (focusRing.TryGetComponent(out Graphic graphic)) { Color c = graphic.color; c.a = ringT < 0.75f ? 1f : Mathf.Clamp01((1f - ringT) / 0.25f); graphic.color = c; }
            if (ringT >= 1f) { ringT = -1f; focusRing.gameObject.SetActive(false); }
        }

        // ---------------------------------------------------------------------------------------
        //  Stabiliser: the picture is shown a little enlarged and moved against the phone's quick turns
        // ---------------------------------------------------------------------------------------
        Vector2 heldAngle;          // how far the phone has turned away from where the picture rests (radians), fading back to zero
        bool gyroAsked;

        void Stabilise()
        {
            if (display == null || viewport == null) return;
            var rt = display.rectTransform;
            if (!stabilise || !SystemInfo.supportsGyroscope || GlowUp.Performance.PerformanceSettings.ReduceEffects)
            {
                if (rt.localScale != Vector3.one) { rt.localScale = Vector3.one; rt.anchoredPosition = Vector2.zero; }
                return;
            }
            if (!gyroAsked) { gyroAsked = true; Input.gyro.enabled = true; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Vector3 turning = Input.gyro.rotationRateUnbiased;                     // radians per second, round the phone's own axes
            // turning round the upright axis moves the picture sideways, turning round the sideways axis moves it up and down
            heldAngle += new Vector2(turning.y, turning.x) * dt;
            heldAngle *= Mathf.Exp(-Mathf.Max(0.1f, stabiliserReturnSpeed) * dt);  // a deliberate move is followed after a moment
            Vector2 view = viewport.rect.size;
            float pixelsPerRadian = view.y * 0.5f / Mathf.Tan(stabiliserFieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector2 room = view * (Mathf.Max(1f, stabiliserZoom) - 1f) * 0.5f;      // how far the enlarged picture can move before its edge shows
            float x = -heldAngle.x * pixelsPerRadian * stabiliserStrength * (stabiliserFlipX ? -1f : 1f) * (IsFrontCamera && mirrorFrontCamera ? -1f : 1f);
            float y = -heldAngle.y * pixelsPerRadian * stabiliserStrength * (stabiliserFlipY ? -1f : 1f);
            rt.localScale = Vector3.one * Mathf.Max(1f, stabiliserZoom);
            rt.anchoredPosition = new Vector2(Mathf.Clamp(x, -room.x, room.x), Mathf.Clamp(y, -room.y, room.y));
        }

        // ---------------------------------------------------------------------------------------
        //  Focus
        // ---------------------------------------------------------------------------------------
        bool canRefocus, nudgeRight;
        float refocusUntil;

        /// <summary>
        /// Makes the camera focus again on the middle of the picture (where the scan frame and the face are),
        /// then lets it carry on focusing by itself. Does nothing on a camera with a fixed focus.
        /// While the focus is locked it stays locked: the camera focuses once more and holds the new focus.
        /// </summary>
        public void Refocus() { Refocus(new Vector2(0.5f, 0.5f)); }

        /// <summary>Makes the camera focus on a spot of its picture (0 to 1 across and up), then lets it focus by itself again (or, while locked, holds it).</summary>
        public void Refocus(Vector2 cameraPoint)
        {
            if (!autoFocus || !canRefocus || cam == null || !cam.isPlaying) return;
            if (!FocusOnce(cameraPoint)) return;
            refocusUntil = FocusLocked ? 0f : Time.unscaledTime + Mathf.Max(0.5f, refocusHoldSeconds);
        }

        // The phone's camera, told a spot, focuses ONCE on it and then keeps the lens where it is; told "no spot" it goes
        // back to looking for focus all the time. (Read in Unity 2022.3's own Android camera code: a spot = focus mode
        // "auto" with one trigger, no spot = "continuous picture".) So: hold = leave the spot set, release = clear it.
        bool FocusOnce(Vector2 cameraPoint)
        {
            try
            {
                // never exactly the spot of the time before, so "the same spot again" is never taken for nothing to do
                nudgeRight = !nudgeRight;
                float x = Mathf.Clamp(cameraPoint.x + (nudgeRight ? 0.004f : -0.004f), 0.01f, 0.99f);
                cam.autoFocusPoint = new Vector2(x, Mathf.Clamp(cameraPoint.y, 0.01f, 0.99f));
                return true;
            }
            catch (Exception e) { Debug.LogWarning("Camera: refocus is not available: " + e.Message); canRefocus = false; return false; }
        }

        // ---------------------------------------------------------------------------------------
        //  Focus lock
        // ---------------------------------------------------------------------------------------
        /// <summary>True when this camera can be told where to focus, and so can hold its focus. False for a camera with a fixed focus and for a computer's webcam.</summary>
        public bool CanLockFocus => autoFocus && canRefocus && cam != null && cam.isPlaying;
        /// <summary>True while the focus is held: the camera has focused once and does not look for focus by itself.</summary>
        public bool FocusLocked { get; private set; }
        /// <summary>The focus lock went on or off. First value: locked now. Second: the user did it by tapping the picture.</summary>
        public event Action<bool, bool> FocusLockChanged;

        /// <summary>
        /// FOCUS LOCK: the camera focuses once on the middle of the picture and HOLDS that focus, instead of looking for
        /// focus all the time (which makes small print go sharp and soft again and again). It stays held until
        /// ReleaseFocus, or until the camera is closed. Returns false when this camera cannot do it.
        /// </summary>
        public bool LockFocus() { return LockFocus(new Vector2(0.5f, 0.5f), false); }

        /// <summary>The same, on a spot of the scan frame or any other part of the viewport (the middle of `area`).</summary>
        public bool LockFocus(RectTransform area)
        {
            if (area == null || viewport == null) return LockFocus();
            Rect inside = RectInViewport(area); Vector2 view = viewport.rect.size;
            if (view.x < 1f || view.y < 1f) return LockFocus();
            return LockFocus(ToCameraPoint(new Vector2(Mathf.Clamp01(inside.center.x / view.x), Mathf.Clamp01(inside.center.y / view.y))), false);
        }

        bool LockFocus(Vector2 cameraPoint, bool byTap)
        {
            if (!CanLockFocus || !FocusOnce(cameraPoint)) return false;
            refocusUntil = 0f;                               // the camera does not get its own focusing back
            bool wasLocked = FocusLocked;
            FocusLocked = true;
            if (!wasLocked || byTap) FocusLockChanged?.Invoke(true, byTap);
            return true;
        }

        /// <summary>Ends the focus lock: the camera looks for focus by itself again.</summary>
        public void ReleaseFocus()
        {
            refocusUntil = 0f;
            if (cam != null) { try { cam.autoFocusPoint = null; } catch (Exception) { } }
            if (!FocusLocked) return;
            FocusLocked = false;
            FocusLockChanged?.Invoke(false, false);
        }

        // ---------------------------------------------------------------------------------------
        //  Display
        // ---------------------------------------------------------------------------------------
        int Angle => (((cam != null ? cam.videoRotationAngle : 0) + extraRotationDegrees) % 360 + 360) % 360;
        bool Mirrored => (cam != null && cam.videoVerticallyMirrored) ^ flipVertical;

        void Layout(bool force)
        {
            if (display == null || viewport == null || cam == null) return;
            int angle = Angle; bool mirrored = Mirrored;
            Vector2 view = viewport.rect.size;
            if (!force && cam.width == lastW && cam.height == lastH && angle == lastAngle && mirrored == lastMirrored
                && Mathf.Approximately(view.x, lastView.x) && Mathf.Approximately(view.y, lastView.y)) return;
            lastW = cam.width; lastH = cam.height; lastAngle = angle; lastMirrored = mirrored; lastView = view;

            // size of the picture once it is upright
            bool sideways = angle == 90 || angle == 270;
            float uprightW = sideways ? cam.height : cam.width, uprightH = sideways ? cam.width : cam.height;
            float scale = Mathf.Max(view.x / Mathf.Max(1f, uprightW), view.y / Mathf.Max(1f, uprightH));    // fill, never stretch

            var rt = display.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(cam.width * scale, cam.height * scale);
            rt.localEulerAngles = new Vector3(0, 0, -angle);
            rt.localScale = Vector3.one;
            display.uvRect = mirrored ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
            if (mirrorRoot != null) mirrorRoot.localScale = new Vector3(IsFrontCamera && mirrorFrontCamera ? -1f : 1f, 1f, 1f);
        }
        Vector2 lastView;

        // ---------------------------------------------------------------------------------------
        //  Pictures for the text reader
        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// An upright picture of what is inside `frame` (a RectTransform inside the viewport, e.g. the scan guide).
        /// The longer side is at most `maxSide` pixels. Returns null when the camera is not running.
        /// </summary>
        public Texture2D CaptureUpright(RectTransform frame, int maxSide = 1100)
        {
            if (!IsPlaying || viewport == null) return null;
            int w = cam.width, h = cam.height;
            if (buffer == null || buffer.Length != w * h) buffer = new Color32[w * h];
            cam.GetPixels32(buffer);

            int angle = Angle; bool mirrored = Mirrored;
            bool sideways = angle == 90 || angle == 270;
            int upW = sideways ? h : w, upH = sideways ? w : h;

            // which part of the upright picture is inside the frame?
            Vector2 view = viewport.rect.size;
            float scale = Mathf.Max(view.x / upW, view.y / upH);
            Rect f = frame != null ? RectInViewport(frame) : new Rect(0, 0, view.x, view.y);
            float offX = (view.x - upW * scale) * 0.5f, offY = (view.y - upH * scale) * 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((f.xMin - offX) / scale), 0, upW - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((f.yMin - offY) / scale), 0, upH - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((f.xMax - offX) / scale), x0 + 1, upW);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((f.yMax - offY) / scale), y0 + 1, upH);
            int cropW = x1 - x0, cropH = y1 - y0;

            float shrink = Mathf.Max(1f, Mathf.Max(cropW, cropH) / (float)Mathf.Max(64, maxSide));
            int outW = Mathf.Max(8, Mathf.RoundToInt(cropW / shrink)), outH = Mathf.Max(8, Mathf.RoundToInt(cropH / shrink));
            var pixels = new Color32[outW * outH];
            for (int oy = 0; oy < outH; oy++)
            {
                int uy = y0 + Mathf.Min(cropH - 1, (int)(oy * shrink));
                for (int ox = 0; ox < outW; ox++)
                {
                    int ux = x0 + Mathf.Min(cropW - 1, (int)(ox * shrink));
                    // upright pixel (ux, uy) -> pixel in the camera's own picture, undoing the rotation
                    int sx, sy;
                    switch (angle)
                    {
                        case 90: sx = w - 1 - uy; sy = ux; break;
                        case 180: sx = w - 1 - ux; sy = h - 1 - uy; break;
                        case 270: sx = uy; sy = h - 1 - ux; break;
                        default: sx = ux; sy = uy; break;
                    }
                    if (mirrored) sy = h - 1 - sy;
                    pixels[oy * outW + ox] = buffer[Mathf.Clamp(sy, 0, h - 1) * w + Mathf.Clamp(sx, 0, w - 1)];
                }
            }
            var tex = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>The frame's rectangle measured from the viewport's bottom-left corner.</summary>
        Rect RectInViewport(RectTransform frame)
        {
            var corners = new Vector3[4];
            frame.GetWorldCorners(corners);
            Vector2 min = viewport.InverseTransformPoint(corners[0]), max = viewport.InverseTransformPoint(corners[2]);
            Rect v = viewport.rect;
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x) - v.xMin, Mathf.Min(min.y, max.y) - v.yMin,
                                   Mathf.Max(min.x, max.x) - v.xMin, Mathf.Max(min.y, max.y) - v.yMin);
        }

        // ---------------------------------------------------------------------------------------
        //  Pictures for hold-to-scan: several a second, as plain numbers (no texture, no JPG)
        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// Takes the camera's newest picture into memory. The three methods below (Brightness and movement, GreyPictures,
        /// UprightArgb) all look at this one picture, so a look at the camera costs one copy. Returns false when the camera
        /// is not running or has no new picture since the last call.
        /// </summary>
        public bool Grab()
        {
            if (!IsPlaying || !cam.didUpdateThisFrame) return false;
            MeasureMotion();
            return true;
        }

        /// <summary>How much the picture changed between the last two looks (see MeasureMotion).</summary>
        public float LastMotion { get; private set; } = 1f;

        // the part of the upright picture that lies inside a frame of the viewport
        struct Crop { public int w, h, angle, x0, y0, cropW, cropH; public bool mirrored; }

        bool CropOf(RectTransform frame, out Crop c)
        {
            c = default;
            if (cam == null || viewport == null || buffer == null) return false;
            c.w = cam.width; c.h = cam.height;
            if (buffer.Length != c.w * c.h) return false;
            c.angle = Angle; c.mirrored = Mirrored;
            bool sideways = c.angle == 90 || c.angle == 270;
            int upW = sideways ? c.h : c.w, upH = sideways ? c.w : c.h;
            Vector2 view = viewport.rect.size;
            float scale = Mathf.Max(view.x / upW, view.y / upH);
            Rect f = frame != null ? RectInViewport(frame) : new Rect(0, 0, view.x, view.y);
            float offX = (view.x - upW * scale) * 0.5f, offY = (view.y - upH * scale) * 0.5f;
            c.x0 = Mathf.Clamp(Mathf.FloorToInt((f.xMin - offX) / scale), 0, upW - 1);
            c.y0 = Mathf.Clamp(Mathf.FloorToInt((f.yMin - offY) / scale), 0, upH - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((f.xMax - offX) / scale), c.x0 + 1, upW);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((f.yMax - offY) / scale), c.y0 + 1, upH);
            c.cropW = x1 - c.x0; c.cropH = y1 - c.y0;
            return c.cropW > 0 && c.cropH > 0;
        }

        /// <summary>How many of the camera's own pixels lie inside `frame`, across and down (what the text reader has to work with). Call Grab() first.</summary>
        public bool FramePixels(RectTransform frame, out int width, out int height)
        {
            bool ok = CropOf(frame, out Crop c);
            width = ok ? c.cropW : 0; height = ok ? c.cropH : 0;
            return ok;
        }

        /// <summary>The place in the camera's own picture of the upright pixel (ux, uy), uy counted from the BOTTOM (the same sums as CaptureUpright).</summary>
        static int SourceIndex(in Crop c, int ux, int uy)
        {
            int sx, sy;
            switch (c.angle)
            {
                case 90: sx = c.w - 1 - uy; sy = ux; break;
                case 180: sx = c.w - 1 - ux; sy = c.h - 1 - uy; break;
                case 270: sx = uy; sy = c.h - 1 - ux; break;
                default: sx = ux; sy = uy; break;
            }
            if (c.mirrored) sy = c.h - 1 - sy;
            if (sx < 0) sx = 0; else if (sx >= c.w) sx = c.w - 1;
            if (sy < 0) sy = 0; else if (sy >= c.h) sy = c.h - 1;
            return sy * c.w + sx;
        }

        /// <summary>
        /// The upright picture of what is inside `frame`, as one ARGB number per pixel, row by row FROM THE TOP (the way
        /// the phone's text reader wants it). `pixels` is made (or made bigger) when needed and reused. The longer side is
        /// at most `maxSide`. Call Grab() first. Returns false when there is no picture.
        /// </summary>
        public bool UprightArgb(RectTransform frame, int maxSide, ref int[] pixels, out int width, out int height)
        {
            width = height = 0;
            if (!CropOf(frame, out Crop c)) return false;
            float shrink = Mathf.Max(1f, Mathf.Max(c.cropW, c.cropH) / (float)Mathf.Max(64, maxSide));
            int outW = Mathf.Max(8, Mathf.RoundToInt(c.cropW / shrink)), outH = Mathf.Max(8, Mathf.RoundToInt(c.cropH / shrink));
            if (pixels == null || pixels.Length < outW * outH) pixels = new int[outW * outH];
            var columns = new int[outW];
            for (int ox = 0; ox < outW; ox++) columns[ox] = c.x0 + Mathf.Min(c.cropW - 1, (int)(ox * shrink));
            for (int row = 0; row < outH; row++)
            {
                int uy = c.y0 + Mathf.Min(c.cropH - 1, (int)((outH - 1 - row) * shrink));        // row 0 is the top
                int at = row * outW;
                for (int ox = 0; ox < outW; ox++)
                {
                    Color32 p = buffer[SourceIndex(c, columns[ox], uy)];
                    pixels[at + ox] = unchecked((int)0xFF000000) | (p.r << 16) | (p.g << 8) | p.b;
                }
            }
            width = outW; height = outH;
            return true;
        }

        /// <summary>
        /// The same for ONE PART of the frame, and it may be ENLARGED: this is what the text tracer of hold-to-scan uses
        /// to give the text reader a close look at the block of small print it is following.
        /// `part` = the part of the frame, 0 to 1 from the frame's TOP-LEFT corner (0,0,1,1 = the whole frame).
        /// `enlarge` = how many times bigger than the camera's own picture it should be (1 = as the camera took it). An
        /// enlarged picture is smoothed between the camera's pixels, never blocky. The longer side stays at most `maxSide`,
        /// so `scale` says what was really done (above 1 = enlarged, below 1 = made smaller). Call Grab() first.
        /// </summary>
        public bool UprightArgb(RectTransform frame, Rect part, float enlarge, int maxSide, ref int[] pixels, out int width, out int height, out float scale)
        {
            width = height = 0; scale = 1f;
            if (!CropOf(frame, out Crop c)) return false;
            // the part, in the upright picture's own pixels (counted from the BOTTOM, like the crop)
            float left = Mathf.Clamp01(part.xMin), right = Mathf.Clamp01(part.xMax), top = Mathf.Clamp01(part.yMin), bottom = Mathf.Clamp01(part.yMax);
            if (right - left < 0.02f || bottom - top < 0.02f) { left = 0f; right = 1f; top = 0f; bottom = 1f; }
            float partX = c.x0 + left * c.cropW, partY = c.y0 + (1f - bottom) * c.cropH;
            float partW = Mathf.Max(8f, (right - left) * c.cropW), partH = Mathf.Max(8f, (bottom - top) * c.cropH);

            scale = Mathf.Min(Mathf.Max(0.05f, enlarge), Mathf.Max(64, maxSide) / Mathf.Max(partW, partH));
            int outW = Mathf.Max(8, Mathf.RoundToInt(partW * scale)), outH = Mathf.Max(8, Mathf.RoundToInt(partH * scale));
            if (pixels == null || pixels.Length < outW * outH) pixels = new int[outW * outH];
            int lastX = c.x0 + c.cropW - 1, lastY = c.y0 + c.cropH - 1;

            if (scale <= 1.001f)
            {
                // as the camera took it, or smaller: the nearest pixel is enough
                for (int row = 0; row < outH; row++)
                {
                    int uy = Mathf.Clamp((int)(partY + (outH - 1 - row + 0.5f) / scale), c.y0, lastY);      // row 0 is the top
                    int at = row * outW;
                    for (int ox = 0; ox < outW; ox++)
                    {
                        Color32 p = buffer[SourceIndex(c, Mathf.Clamp((int)(partX + (ox + 0.5f) / scale), c.x0, lastX), uy)];
                        pixels[at + ox] = unchecked((int)0xFF000000) | (p.r << 16) | (p.g << 8) | p.b;
                    }
                }
            }
            else
            {
                // enlarged: every new pixel is mixed from the four camera pixels round it
                if (enlargeColumn == null || enlargeColumn.Length < outW) { enlargeColumn = new int[outW]; enlargeShare = new int[outW]; }
                for (int ox = 0; ox < outW; ox++)
                {
                    float sx = partX + (ox + 0.5f) / scale - 0.5f;
                    int whole = Mathf.FloorToInt(sx);
                    enlargeColumn[ox] = whole; enlargeShare[ox] = (int)((sx - whole) * 256f);
                }
                for (int row = 0; row < outH; row++)
                {
                    float sy = partY + (outH - 1 - row + 0.5f) / scale - 0.5f;
                    int y0 = Mathf.FloorToInt(sy), fy = (int)((sy - y0) * 256f);
                    int yA = Mathf.Clamp(y0, c.y0, lastY), yB = Mathf.Clamp(y0 + 1, c.y0, lastY);
                    int at = row * outW;
                    for (int ox = 0; ox < outW; ox++)
                    {
                        int xA = Mathf.Clamp(enlargeColumn[ox], c.x0, lastX), xB = Mathf.Clamp(enlargeColumn[ox] + 1, c.x0, lastX), fx = enlargeShare[ox];
                        Color32 a = buffer[SourceIndex(c, xA, yA)], b = buffer[SourceIndex(c, xB, yA)];
                        Color32 d = buffer[SourceIndex(c, xA, yB)], e = buffer[SourceIndex(c, xB, yB)];
                        int r = ((a.r * (256 - fx) + b.r * fx) * (256 - fy) + (d.r * (256 - fx) + e.r * fx) * fy) >> 16;
                        int g = ((a.g * (256 - fx) + b.g * fx) * (256 - fy) + (d.g * (256 - fx) + e.g * fx) * fy) >> 16;
                        int bl = ((a.b * (256 - fx) + b.b * fx) * (256 - fy) + (d.b * (256 - fx) + e.b * fx) * fy) >> 16;
                        pixels[at + ox] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
                    }
                }
            }
            width = outW; height = outH;
            return true;
        }
        int[] enlargeColumn, enlargeShare;

        /// <summary>
        /// Two small grey pictures of what is inside `frame`, for the quality gate (FrameQuality): `overview` = the whole
        /// frame made small (overviewWidth x overviewHeight), `detail` = a square patch from the middle of the frame at
        /// the camera's own sharpness (detailSize x detailSize). One number 0 to 255 per pixel. Call Grab() first.
        /// </summary>
        public bool GreyPictures(RectTransform frame, ref byte[] overview, int overviewWidth, int overviewHeight, ref byte[] detail, int detailSize)
        {
            if (!CropOf(frame, out Crop c)) return false;
            if (overview == null || overview.Length != overviewWidth * overviewHeight) overview = new byte[overviewWidth * overviewHeight];
            for (int y = 0; y < overviewHeight; y++)
            {
                int uy = c.y0 + Mathf.Min(c.cropH - 1, (int)((y + 0.5f) / overviewHeight * c.cropH));
                for (int x = 0; x < overviewWidth; x++)
                {
                    Color32 p = buffer[SourceIndex(c, c.x0 + Mathf.Min(c.cropW - 1, (int)((x + 0.5f) / overviewWidth * c.cropW)), uy)];
                    overview[y * overviewWidth + x] = (byte)((p.r * 77 + p.g * 150 + p.b * 29) >> 8);
                }
            }
            int size = Mathf.Min(detailSize, Mathf.Min(c.cropW, c.cropH));
            if (detail == null || detail.Length != detailSize * detailSize) detail = new byte[detailSize * detailSize];
            int startX = c.x0 + (c.cropW - size) / 2, startY = c.y0 + (c.cropH - size) / 2;
            for (int y = 0; y < detailSize; y++)
                for (int x = 0; x < detailSize; x++)
                {
                    Color32 p = buffer[SourceIndex(c, startX + Mathf.Min(size - 1, x), startY + Mathf.Min(size - 1, y))];
                    detail[y * detailSize + x] = (byte)((p.r * 77 + p.g * 150 + p.b * 29) >> 8);
                }
            return true;
        }

        /// <summary>
        /// How much the picture changed since the last call: 0 = perfectly still, higher = moving.
        /// Used to wait until the user holds the phone steady before reading text.
        /// </summary>
        public float MeasureMotion()
        {
            if (!IsPlaying) return 1f;
            int w = cam.width, h = cam.height;
            if (buffer == null || buffer.Length != w * h) buffer = new Color32[w * h];
            cam.GetPixels32(buffer);
            const int gx = 16, gy = 12;
            var current = new float[gx * gy];
            for (int y = 0; y < gy; y++)
                for (int x = 0; x < gx; x++)
                {
                    Color32 c = buffer[(int)((y + 0.5f) / gy * h) * w + (int)((x + 0.5f) / gx * w)];
                    current[y * gx + x] = (c.r + c.g + c.b) / 765f;
                }
            float motion = 1f;
            if (steadyPrevious != null && steadyPrevious.Length == current.Length)
            {
                float sum = 0f;
                for (int i = 0; i < current.Length; i++) sum += Mathf.Abs(current[i] - steadyPrevious[i]);
                motion = sum / current.Length;
            }
            steadyPrevious = current;
            LastMotion = motion;
            return motion;
        }

        /// <summary>The average brightness of the picture, 0 (black) to 1 (white). Very low = the lens is covered.</summary>
        public float MeasureBrightness()
        {
            if (steadyPrevious == null || steadyPrevious.Length == 0) return 0.5f;
            float sum = 0f; foreach (float v in steadyPrevious) sum += v;
            return sum / steadyPrevious.Length;
        }

        /// <summary>
        /// The strongest real colour in the last captured frame (ignores white, grey and black), as hex, or "".
        /// A rough guess at the colour of the product the user is holding.
        /// </summary>
        public string DominantColourHex()
        {
            if (buffer == null || buffer.Length == 0) return "";
            const int bins = 24;
            var weight = new float[bins]; var r = new float[bins]; var g = new float[bins]; var b = new float[bins];
            int step = Mathf.Max(1, buffer.Length / 6000);
            for (int i = 0; i < buffer.Length; i += step)
            {
                Color c = buffer[i];
                Color.RGBToHSV(c, out float hue, out float sat, out float val);
                if (sat < 0.30f || val < 0.20f) continue;
                int bin = Mathf.Min(bins - 1, (int)(hue * bins));
                float wgt = sat * val;
                weight[bin] += wgt; r[bin] += c.r * wgt; g[bin] += c.g * wgt; b[bin] += c.b * wgt;
            }
            int best = 0; float total = 0f;
            for (int i = 0; i < bins; i++) { total += weight[i]; if (weight[i] > weight[best]) best = i; }
            if (weight[best] <= 0f || weight[best] < total * 0.18f) return "";      // no clear colour
            var colour = new Color(r[best] / weight[best], g[best] / weight[best], b[best] / weight[best]);
            return "#" + ColorUtility.ToHtmlStringRGB(colour);
        }
    }
}
