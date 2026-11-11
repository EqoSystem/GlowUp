using TMPro;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Keeps a small object (the "i" badge) right after the LAST LETTER of a text (12 November 2026): "Recommended for you (i)". The text's
    /// real width is read from the font that is on it now, every time it changes, in the Editor and on the phone, so the badge never sits on
    /// the letters and never drifts away from them. The object's anchors are left as they are; only its horizontal position is set.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class TextEndFollower : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;
        [Tooltip("Space between the last letter and this object (canvas units)")]
        [SerializeField] private float gap = 16f;
        [Tooltip("A second object that moves by the same amount (the badge's bubble, which must stay under the text's left edge)")]
        [SerializeField] private RectTransform bubble;

        float lastWidth = -1f;

        void OnEnable() { Apply(); }
        void LateUpdate() { Apply(); }

        public void Apply()
        {
            if (text == null) return;
            float width = text.GetPreferredValues(text.text, 100000f, 0f).x;
            if (Mathf.Approximately(width, lastWidth)) return;
            lastWidth = width;
            var rect = (RectTransform)transform;
            Vector2 at = rect.anchoredPosition; at.x = width + gap; rect.anchoredPosition = at;
            if (bubble != null) { Vector2 b = bubble.anchoredPosition; b.x = -(width + gap); bubble.anchoredPosition = b; }
        }
    }
}
