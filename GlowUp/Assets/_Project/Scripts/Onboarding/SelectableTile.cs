using System;
using GlowUp.Common;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// One tile (skin type or skin tone). Tapping selects it inside its TileSelectionGroup and plays
    /// the pixel-sweep + neon outline animation. The value is what gets saved to the profile.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SelectableTile : MonoBehaviour
    {
        [Tooltip("Saved to the profile when this tile is chosen, e.g. Oily or Fair")]
        [SerializeField] private string value;
        [SerializeField] private PixelSweepTileAnimator animator;
        [Tooltip("Scale applied while selected")]
        [SerializeField] private float selectedScale = 1.04f;
        [SerializeField] private float scaleSeconds = 0.15f;

        public string Value => value;
        public bool IsSelected { get; private set; }
        public event Action<SelectableTile> Clicked;

        Vector3 baseScale;
        float k;

        void Awake()
        {
            baseScale = transform.localScale;
            GetComponent<Button>().onClick.AddListener(() => Clicked?.Invoke(this));
        }

        public void SetSelected(bool selected, bool animate = true)
        {
            IsSelected = selected;
            if (animator != null) { if (animate) animator.SetSelected(selected); else animator.SetSelected(selected); }
        }

        void Update()
        {
            float target = IsSelected ? 1f : 0f;
            if (Mathf.Approximately(k, target)) return;
            k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, scaleSeconds));
            transform.localScale = Vector3.Lerp(baseScale, baseScale * selectedScale, k);
        }
    }

    // TileSelectionGroup (the single-choice group these tiles belong to) is in its own file, TileSelectionGroup.cs:
    // Unity can only keep a script attached in a saved scene when its class and its file have the same name.
}
