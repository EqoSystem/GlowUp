using System;
using GlowUp.Common;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.Scan
{
    /// <summary>
    /// The big round "Start Scan" button of the scan screen: tap once to start scanning, tap again to pause.
    /// It must sit on an object with a Graphic that takes touches.
    ///
    /// Locking (everything below is an Inspector field of this object):
    ///  - Debounce: two taps closer together than "Debounce Seconds" count as ONE tap, so a double tap cannot start and
    ///    stop the scan in the same moment.
    ///  - Taps Ignored: the scan screen switches this on while it is busy (the camera is starting, or the analysis is
    ///    running). The button then takes no taps and looks dimmed, but the scan keeps its state.
    ///  - Usable: the scan screen switches this off while a scan is handed over; the button counts as let go.
    /// </summary>
    public class HoldToScanButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Tapping")]
        [Tooltip("Taps closer together than this many seconds count as one tap (0 = no debouncing)")]
        [Min(0f)] [SerializeField] private float debounceSeconds = 0.35f;

        [Header("Looks while scanning")]
        [Tooltip("What shrinks a little while scanning (usually this object). Optional.")]
        [SerializeField] private RectTransform scaleTarget;
        [Tooltip("The size of the button while scanning (1 = no change)")]
        [Range(0.8f, 1f)] [SerializeField] private float heldScale = 0.94f;
        [Tooltip("How fast the button grows and shrinks (bigger = quicker)")]
        [Min(0.1f)] [SerializeField] private float scaleSpeed = 1.2f;

        [Header("Looks while it is locked (busy)")]
        [Tooltip("The object whose see-through level shows the lock (a Canvas Group on the button, made by the scene builder). Optional.")]
        [SerializeField] private CanvasGroup dimGroup;
        [Tooltip("How see-through the button is while it takes no taps (1 = not at all)")]
        [Range(0.3f, 1f)] [SerializeField] private float lockedAlpha = 0.65f;
        [Tooltip("How fast it dims and comes back (bigger = quicker)")]
        [Min(0.5f)] [SerializeField] private float dimSpeed = 8f;

        bool pressed, locked, usable = true, tapsIgnored;
        float lastTap = -10f;

        /// <summary>True while the scan should run after the user toggles it on.</summary>
        public bool IsScanning => usable && locked;
        /// <summary>True when scanning is toggled on.</summary>
        public bool IsActive => locked;
        /// <summary>True while the button takes no taps because the screen is busy (the scan itself is not changed).</summary>
        public bool TapsIgnored { get => tapsIgnored; set => tapsIgnored = value; }
        /// <summary>Raised when a tap starts or resumes scanning.</summary>
        public event Action ScanStarted;
        /// <summary>Raised when a tap pauses scanning.</summary>
        public event Action ScanPaused;

        /// <summary>Off = the button takes no touches and counts as let go (while the scan is being handed over).</summary>
        public bool Usable
        {
            get => usable;
            set { usable = value; if (!value) LetGo(); }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!usable || tapsIgnored) return;
            pressed = true;
            SwipeSceneNavigator.Suppressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressed) return;
            pressed = false;
            SwipeSceneNavigator.Suppressed = false;
            if (!usable || tapsIgnored) return;
            if (debounceSeconds > 0f && Time.unscaledTime - lastTap < debounceSeconds) return;       // the second tap of a double tap
            lastTap = Time.unscaledTime;
            locked = !locked;
            if (locked) ScanStarted?.Invoke(); else ScanPaused?.Invoke();
        }

        /// <summary>A tap from code (the volume button, phase 2A): the same rules as a finger's tap. True when it was taken.</summary>
        public bool Toggle()
        {
            if (!usable || tapsIgnored || pressed) return false;
            if (debounceSeconds > 0f && Time.unscaledTime - lastTap < debounceSeconds) return false;
            lastTap = Time.unscaledTime;
            locked = !locked;
            if (locked) ScanStarted?.Invoke(); else ScanPaused?.Invoke();
            return true;
        }

        /// <summary>Stops scanning and clears any touch state from code (the scan finished, the screen is left).</summary>
        public void LetGo()
        {
            bool was = pressed || locked;
            if (pressed) SwipeSceneNavigator.Suppressed = false;
            pressed = false; locked = false;
            if (was) ScanPaused?.Invoke();
        }

        void OnDisable() { LetGo(); }
        void OnApplicationPause(bool paused) { if (paused) LetGo(); }
        void OnApplicationFocus(bool focused) { if (!focused) LetGo(); }

        void Update()
        {
            if (scaleTarget != null)
            {
                float wanted = IsScanning ? heldScale : 1f;
                float now = scaleTarget.localScale.x;
                if (!Mathf.Approximately(now, wanted)) scaleTarget.localScale = Vector3.one * Mathf.MoveTowards(now, wanted, Time.unscaledDeltaTime * scaleSpeed);
            }
            if (dimGroup != null)
            {
                float alpha = tapsIgnored || !usable ? lockedAlpha : 1f;
                if (!Mathf.Approximately(dimGroup.alpha, alpha)) dimGroup.alpha = Mathf.MoveTowards(dimGroup.alpha, alpha, Time.unscaledDeltaTime * dimSpeed);
            }
        }
    }
}
