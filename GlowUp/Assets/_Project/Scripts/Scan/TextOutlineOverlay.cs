using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// Draws a thin outline round every line of text the scanner has just read, right on the camera
    /// picture, so the user sees what the app is looking at and can move the product until the
    /// outlines sit on the words. The outlines fade away by themselves.
    ///
    /// This object must cover exactly the area that gets read (the scan guide's Frame). "Outline
    /// Template" is the pattern outline: restyle that one object; it is copied for each line.
    /// Outlines only appear on a phone (the Editor's text reader cannot say where the text is).
    /// </summary>
    public class TextOutlineOverlay : MonoBehaviour
    {
        [Tooltip("The pattern outline (a child of this object). It is hidden when the app runs and copied once per line of text.")]
        [SerializeField] private Image outlineTemplate;
        [Tooltip("The most outlines shown at once")]
        [SerializeField] private int maximum = 24;
        [Tooltip("Extra space round each line of text (canvas units)")]
        [SerializeField] private float padding = 6f;
        [Tooltip("Seconds the outlines stay fully visible, and seconds they take to fade")]
        [SerializeField] private float holdSeconds = 1.1f;
        [SerializeField] private float fadeSeconds = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float alpha = 0.95f;

        readonly List<Image> pool = new List<Image>();
        int shown;
        float age = -1f;

        void Awake() { if (outlineTemplate != null) outlineTemplate.gameObject.SetActive(false); }

        /// <summary>Shows outlines at these places: x, y, width, height from 0 to 1, measured from this object's bottom-left corner.</summary>
        public void Show(Rect[] boxes)
        {
            if (outlineTemplate == null || boxes == null) { Clear(); return; }
            Rect area = ((RectTransform)transform).rect;
            shown = Mathf.Min(boxes.Length, Mathf.Max(0, maximum));
            for (int i = 0; i < shown; i++)
            {
                if (i >= pool.Count)
                {
                    Image copy = Instantiate(outlineTemplate, transform);
                    copy.gameObject.name = "TextOutline" + (i + 1);
                    copy.raycastTarget = false;
                    pool.Add(copy);
                }
                RectTransform rt = pool[i].rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
                rt.anchoredPosition = new Vector2(boxes[i].x * area.width - padding, boxes[i].y * area.height - padding);
                rt.sizeDelta = new Vector2(boxes[i].width * area.width + padding * 2f, boxes[i].height * area.height + padding * 2f);
                pool[i].gameObject.SetActive(true);
            }
            for (int i = shown; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
            age = shown > 0 ? 0f : -1f;
            Paint(alpha);
        }

        public void Clear()
        {
            foreach (Image outline in pool) if (outline != null) outline.gameObject.SetActive(false);
            shown = 0; age = -1f;
        }

        void Update()
        {
            if (age < 0f) return;
            age += Time.unscaledDeltaTime;
            if (age <= holdSeconds) return;
            float k = (age - holdSeconds) / Mathf.Max(0.01f, fadeSeconds);
            if (k >= 1f) { Clear(); return; }
            Paint(alpha * (1f - k));
        }

        void Paint(float a)
        {
            for (int i = 0; i < shown && i < pool.Count; i++)
            {
                Color c = pool[i].color; c.a = a; pool[i].color = c;
            }
        }
    }
}
