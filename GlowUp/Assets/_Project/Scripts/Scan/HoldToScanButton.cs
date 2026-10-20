using System;
using GlowUp.Common;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.Scan
{
    /// <summary>
    /// The big round button of the scan screen: tap once to start scanning, tap again to pause.
    /// It must sit on an object with a Graphic that takes touches.
    /// </summary>
    public class HoldToScanButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("What shrinks a little while scanning (usually this object). Optional.")]
        [SerializeField] private RectTransform scaleTarget;
        [Tooltip("The size of the button while scanning (1 = no change)")]
        [Range(0.8f, 1f)] [SerializeField] private float heldScale = 0.94f;

        bool pressed, locked, usable = true;

        /// <summary>True while the scan should run after the user toggles it on.</summary>
        public bool IsScanning => usable && locked;
        /// <summary>True when scanning is toggled on.</summary>
        public bool IsActive => locked;
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
            if (!usable) return;
            pressed = true;
            SwipeSceneNavigator.Suppressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressed) return;
            pressed = false;
            SwipeSceneNavigator.Suppressed = false;
            if (!usable) return;
            locked = !locked;
            if (locked) ScanStarted?.Invoke(); else ScanPaused?.Invoke();
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
            if (scaleTarget == null) return;
            float wanted = IsScanning ? heldScale : 1f;
            float now = scaleTarget.localScale.x;
            if (!Mathf.Approximately(now, wanted)) scaleTarget.localScale = Vector3.one * Mathf.MoveTowards(now, wanted, Time.unscaledDeltaTime * 1.2f);
        }
    }
}
