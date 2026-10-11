using System;
using System.Collections;
using UnityEngine;
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
        [Tooltip("After a tap (or Refocus) the camera goes back to focusing by itself after this many seconds")]
        [SerializeField] private float refocusHoldSeconds = 2.5f;

        public bool IsPlaying => cam != null && cam.isPlaying && cam.width > 32;
        public bool IsFrontCamera { get; private set; }
        /// <summary>Raised once: true + "" when the camera is running, false + reason when it cannot start.</summary>
        public event Action<bool, string> Started;

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
            if (cam != null) { if (cam.isPlaying) cam.Stop(); Destroy(cam); cam = null; }
            if (display != null) display.texture = null;
            lastAngle = -1; steadyPrevious = null; refocusUntil = 0f;
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
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, Input.mousePosition, null, out Vector2 tapped))
                FocusAt(tapped);
            ShowFocusRing();
        }

        float ringT = -1f;

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
            Refocus(ToCameraPoint(seen));
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
        bool canRefocus;
        float refocusUntil;

        /// <summary>
        /// Makes the camera focus again on the middle of the picture (where the scan frame and the face are),
        /// then lets it carry on focusing by itself. Does nothing on a camera with a fixed focus.
        /// </summary>
        public void Refocus() { Refocus(new Vector2(0.5f, 0.5f)); }

        /// <summary>Makes the camera focus on a spot of its picture (0 to 1 across and up), then lets it focus by itself again.</summary>
        public void Refocus(Vector2 cameraPoint)
        {
            if (!autoFocus || !canRefocus || cam == null || !cam.isPlaying) return;
            try
            {
                cam.autoFocusPoint = cameraPoint;
                refocusUntil = Time.unscaledTime + Mathf.Max(0.5f, refocusHoldSeconds);
            }
            catch (Exception e) { Debug.LogWarning("Camera: refocus is not available: " + e.Message); canRefocus = false; }
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
