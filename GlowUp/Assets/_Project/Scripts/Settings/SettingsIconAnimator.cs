using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// The small animation of a Settings tile's icon. Each tile picks one style:
    ///
    ///  - Pop In: the icon grows from nothing with a little bounce (Appearance).
    ///  - Assemble: the icon's two halves fly in and join (Account).
    ///  - Sparkle: the icon glows and small stars twinkle round it (Skin Profile).
    ///  - Shield Then Check: the shield pops in, then the check mark pops in on it (Safety).
    ///  - Bell Swing: the icon swings like a bell that was rung (Notifications).
    ///  - Pulse: the icon beats twice (Sounds).
    ///  - Spin: the icon turns once (Camera).
    ///
    /// It plays when the page appears (after "Start Delay") and whenever the tile is opened.
    /// In the Editor the icon is shown as it rests when the animation is over.
    /// </summary>
    public class SettingsIconAnimator : MonoBehaviour
    {
        public enum Style { PopIn, Assemble, Sparkle, ShieldThenCheck, BellSwing, Pulse, Spin }

        [SerializeField] private Style style = Style.PopIn;

        [Header("References")]
        [Tooltip("The icon (for Assemble: the object that holds the two halves)")]
        [SerializeField] private RectTransform icon;
        [Tooltip("Assemble: the icon's top half and bottom half")]
        [SerializeField] private RectTransform topHalf;
        [SerializeField] private RectTransform bottomHalf;
        [Tooltip("Shield Then Check: the check mark on the shield")]
        [SerializeField] private RectTransform second;
        [Tooltip("Sparkle: the small stars round the icon (invisible while nothing plays)")]
        [SerializeField] private RectTransform[] sparkles;
        [Tooltip("Sparkle: a soft light behind the icon. Optional.")]
        [SerializeField] private Graphic glow;

        [Header("Timing")]
        [Tooltip("Seconds one play takes")]
        [SerializeField] private float seconds = 0.7f;
        [Tooltip("On = it plays once when the page appears")]
        [SerializeField] private bool playOnEnable = true;
        [Tooltip("Seconds before that first play (give each tile a little more, so they play one after the other)")]
        [SerializeField] private float startDelay = 0f;
        [Tooltip("Plays again every this many seconds (0 = only when the page appears and when the tile is tapped)")]
        [SerializeField] private float repeatEvery = 0f;

        [Header("Feel")]
        [Tooltip("How far the icon overshoots its size when it pops (0 = no bounce)")]
        [Range(0f, 3f)] [SerializeField] private float bounce = 1.7f;
        [Tooltip("Assemble: how far apart the halves start")]
        [SerializeField] private float assembleDistance = 46f;
        [Tooltip("Assemble: how many degrees the halves are turned when they start")]
        [SerializeField] private float assembleTwist = 50f;
        [Tooltip("Bell Swing: degrees of the first swing")]
        [SerializeField] private float swingAngle = 28f;
        [Tooltip("Pulse and Sparkle: how much bigger the icon gets (0.18 = 18%)")]
        [Range(0f, 0.6f)] [SerializeField] private float pulseAmount = 0.18f;
        [Range(0f, 1f)] [SerializeField] private float glowAlpha = 0.55f;

        float t = -1f, wait = -1f, idle;
        Vector2 topHome, bottomHome;
        bool homesKnown;

        void OnEnable()
        {
            RememberHomes();
            Rest();
            idle = 0f;
            if (playOnEnable) wait = Mathf.Max(0f, startDelay);
        }

        void RememberHomes()
        {
            if (homesKnown) return;
            homesKnown = true;
            if (topHalf != null) topHome = topHalf.anchoredPosition;
            if (bottomHalf != null) bottomHome = bottomHalf.anchoredPosition;
        }

        /// <summary>Plays the animation once from the start.</summary>
        public void Play()
        {
            if (!isActiveAndEnabled) return;
            RememberHomes();
            wait = -1f; t = 0f; idle = 0f;
            Draw(0f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (wait >= 0f)
            {
                if (wait == 0f || (wait -= dt) <= 0f) { wait = -1f; t = 0f; Draw(0f); }
                else if (style != Style.BellSwing && style != Style.Pulse && style != Style.Spin && style != Style.Sparkle) Draw(0f);   // icons that "arrive" wait unseen
                return;
            }
            if (t < 0f)
            {
                if (repeatEvery > 0f && (idle += dt) >= repeatEvery) Play();
                return;
            }
            t += dt / Mathf.Max(0.05f, seconds);
            if (t >= 1f) { t = -1f; Rest(); return; }
            Draw(t);
        }

        // back-out easing: runs past 1 and settles
        float Pop(float x)
        {
            x = Mathf.Clamp01(x);
            float c = bounce, c3 = c + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c * Mathf.Pow(x - 1f, 2f);
        }

        void Draw(float x)
        {
            switch (style)
            {
                case Style.PopIn:
                    Scale(icon, Pop(x));
                    break;
                case Style.Assemble:
                {
                    float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(x / 0.7f), 3f);
                    Half(topHalf, topHome, 1f, e);
                    Half(bottomHalf, bottomHome, -1f, e);
                    float settle = x > 0.7f ? Mathf.Sin((x - 0.7f) / 0.3f * Mathf.PI) * 0.14f : 0f;      // a small pop when the halves meet
                    Scale(icon, 1f + settle);
                    break;
                }
                case Style.Sparkle:
                {
                    float wave = Mathf.Sin(x * Mathf.PI);
                    Scale(icon, 1f + pulseAmount * wave);
                    if (glow != null) { Color c = glow.color; c.a = glowAlpha * wave; glow.color = c; }
                    if (sparkles != null)
                        for (int i = 0; i < sparkles.Length; i++)
                        {
                            if (sparkles[i] == null) continue;
                            float u = Mathf.Clamp01((x - i * 0.16f) / 0.5f);
                            Scale(sparkles[i], Mathf.Sin(u * Mathf.PI));
                            sparkles[i].localEulerAngles = new Vector3(0f, 0f, 90f * u);
                        }
                    break;
                }
                case Style.ShieldThenCheck:
                    Scale(icon, Pop(x / 0.55f));
                    Scale(second, x < 0.5f ? 0f : Pop((x - 0.5f) / 0.5f));
                    break;
                case Style.BellSwing:
                    if (icon != null) icon.localEulerAngles = new Vector3(0f, 0f, swingAngle * Mathf.Sin(x * Mathf.PI * 4f) * (1f - x));
                    break;
                case Style.Pulse:
                    Scale(icon, 1f + pulseAmount * Mathf.Abs(Mathf.Sin(x * Mathf.PI * 2f)) * (1f - x * 0.5f));
                    break;
                case Style.Spin:
                {
                    float e = 1f - Mathf.Pow(1f - x, 3f);
                    if (icon != null) icon.localEulerAngles = new Vector3(0f, 0f, -360f * e);
                    Scale(icon, 1f + pulseAmount * Mathf.Sin(x * Mathf.PI));
                    break;
                }
            }
        }

        void Half(RectTransform half, Vector2 home, float direction, float e)
        {
            if (half == null) return;
            half.anchoredPosition = home + new Vector2(-direction * assembleDistance * 0.6f, direction * assembleDistance) * (1f - e);
            half.localEulerAngles = new Vector3(0f, 0f, direction * assembleTwist * (1f - e));
            if (half.TryGetComponent(out Graphic g)) { Color c = g.color; c.a = Mathf.Clamp01(e * 1.5f); g.color = c; }
        }

        /// <summary>The icon as it looks when nothing plays.</summary>
        void Rest()
        {
            Scale(icon, 1f);
            if (icon != null) icon.localEulerAngles = Vector3.zero;
            Scale(second, 1f);
            if (homesKnown) { Half(topHalf, topHome, 1f, 1f); Half(bottomHalf, bottomHome, -1f, 1f); }
            if (glow != null) { Color c = glow.color; c.a = 0f; glow.color = c; }
            if (sparkles != null) foreach (RectTransform s in sparkles) Scale(s, 0f);
        }

        static void Scale(RectTransform target, float s) { if (target != null) target.localScale = new Vector3(s, s, 1f); }
    }
}
