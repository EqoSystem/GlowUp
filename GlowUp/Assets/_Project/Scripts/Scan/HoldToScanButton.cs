using System;
using GlowUp.Common;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.Scan
{
    /// <summary>
    /// The big round button of the scan screen: the scan runs WHILE IT IS HELD, and pauses when it is let go.
    ///
    /// For someone who cannot keep a finger down, a short tap LOCKS it (the scan keeps running) and the next tap lets go
    /// again. "Tap To Lock" switches that off. It must sit on an object with a Graphic that takes touches.
    ///
    /// While a finger is on the button, dragging the page sideways to another screen is switched off: a thumb that
    /// drifts during a long hold must not leave the scanner.
    /// </summary>
    public class HoldToScanButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("What shrinks a little while the button is held (usually this object). Optional.")]
        [SerializeField] private RectTransform scaleTarget;
        [Tooltip("The size of the button while it is held (1 = no change)")]
        [Range(0.8f, 1f)] [SerializeField] private float heldScale = 0.94f;
        [Tooltip("On = a short tap keeps the scan running without holding; the next tap stops it")]
        [SerializeField] private bool tapToLock = true;
        [Tooltip("A press shorter than this many seconds is a tap")]
        [SerializeField] private float tapSeconds = 0.3f;

        bool pressed, locked, usable = true;
        float pressedAt;

        /// <summary>True while the scan should run: the finger is down, or the button was locked with a tap.</summary>
        public bool IsHeld => usable && (pressed || locked);
        /// <summary>True when the button was locked with a tap (nobody is holding it).</summary>
        public bool IsLocked => locked;
        /// <summary>Raised when the button goes down (also when a tap locks it).</summary>
        public event Action Pressed;
        /// <summary>Raised when the scan should pause: the finger went up (and the button is not locked), or a tap unlocked it.</summary>
        public event Action Released;

        /// <summary>Off = the button takes no touches and counts as let go (while the scan is being handed over).</summary>
        public bool Usable
        {
            get => usable;
            set { usable = value; if (!value) LetGo(); }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!usable) return;
            if (locked)
            {
                // a touch on a locked button unlocks it; nothing more happens with this press
                locked = false; pressed = false;
                Released?.Invoke();
                return;
            }
            pressed = true; pressedAt = Time.unscaledTime;
            SwipeSceneNavigator.Suppressed = true;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressed) return;
            pressed = false;
            SwipeSceneNavigator.Suppressed = false;
            if (tapToLock && usable && Time.unscaledTime - pressedAt < tapSeconds) { locked = true; return; }     // a tap: keep going
            Released?.Invoke();
        }

        /// <summary>Lets go of the button from code (the scan finished, the screen is left).</summary>
        public void LetGo()
        {
            bool was = pressed || locked;
            if (pressed) SwipeSceneNavigator.Suppressed = false;
            pressed = false; locked = false;
            if (was) Released?.Invoke();
        }

        void OnDisable() { LetGo(); }
        void OnApplicationPause(bool paused) { if (paused) LetGo(); }
        void OnApplicationFocus(bool focused) { if (!focused) LetGo(); }

        void Update()
        {
            if (scaleTarget == null) return;
            float wanted = IsHeld ? heldScale : 1f;
            float now = scaleTarget.localScale.x;
            if (!Mathf.Approximately(now, wanted)) scaleTarget.localScale = Vector3.one * Mathf.MoveTowards(now, wanted, Time.unscaledDeltaTime * 1.2f);
        }
    }
}
