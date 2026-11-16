using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// A CHECK THAT FILLS (New13 item 7, 10 November 2026): when a card is chosen, a circle grows from 60 % to full size while it fills
    /// with the brand colour, then the tick is drawn from left to right (230 ms, ease-out, once). Un-choosing reverses it in 150 ms.
    /// With reduced effects it simply shows the filled state. Made at run time.
    /// </summary>
    public class CheckFillIndicator : MonoBehaviour
    {
        public const float OnSeconds = 0.23f, OffSeconds = 0.15f;

        Image disc, tick;
        bool on;
        float k;            // 0 = empty, 1 = full (disc filled and tick drawn)

        public bool IsOn => on;

        /// <summary>A `size`-unit indicator in the top-right corner of `parent`, `inset` units in from its edges.</summary>
        public static CheckFillIndicator AddTo(RectTransform parent, float size, float inset, Color fill, Color tickColour)
        {
            RectTransform rt = RuntimeUiKit.Rect("ChosenCheck", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-inset, -inset);
            var check = rt.gameObject.AddComponent<CheckFillIndicator>();
            check.disc = RuntimeUiKit.Icon(rt, "Fill", RuntimeIcons.Circle, fill, size);
            check.tick = RuntimeUiKit.Icon(rt, "Tick", RuntimeIcons.Tick, tickColour, size * 0.72f);
            check.tick.type = Image.Type.Filled; check.tick.fillMethod = Image.FillMethod.Horizontal; check.tick.fillOrigin = (int)Image.OriginHorizontal.Left;
            check.Paint();
            return check;
        }

        public void SetOn(bool value, bool animate)
        {
            on = value;
            transform.SetAsLastSibling();
            if (!animate || GlowUp.Performance.PerformanceSettings.ReduceEffects || !isActiveAndEnabled) k = on ? 1f : 0f;
            Paint();
        }

        void Update()
        {
            float target = on ? 1f : 0f;
            if (Mathf.Approximately(k, target)) return;
            k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime / (on ? OnSeconds : OffSeconds));
            Paint();
        }

        /// <summary>The look at progress `k`: the disc during the first 60 %, the tick during the last 55 % (they overlap a little).</summary>
        void Paint()
        {
            if (disc == null || tick == null) return;
            float d = Ease(Mathf.Clamp01(k / 0.6f));
            float t = Ease(Mathf.Clamp01((k - 0.45f) / 0.55f));
            disc.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, d);
            Color c = disc.color; c.a = d; disc.color = c;
            tick.fillAmount = t;
            bool visible = k > 0.001f;
            if (disc.enabled != visible) { disc.enabled = visible; tick.enabled = visible; }
        }

        static float Ease(float x) => 1f - (1f - x) * (1f - x);
    }
}
