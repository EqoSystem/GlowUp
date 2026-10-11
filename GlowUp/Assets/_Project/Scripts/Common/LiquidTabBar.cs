using GlowUp.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The "liquid" highlight of the bottom bar. A soft drop sits behind the icon of the screen the user
    /// is on. While the user drags sideways to another screen the drop flows towards that screen's icon:
    /// its front runs ahead, its back follows late, so it stretches and wobbles like liquid, and the
    /// faster the drag the more its colour turns into the "fast" colour. SwipeSceneNavigator drives it.
    /// </summary>
    public class LiquidTabBar : ThemeAwareBehaviour
    {
        [Header("References")]
        [Tooltip("The drop (an Image behind the icons, a child of the bar)")]
        [SerializeField] private RectTransform drop;
        [SerializeField] private Image dropImage;
        [Tooltip("The bar's buttons from left to right")]
        [SerializeField] private RectTransform[] slots;
        [Tooltip("Which button belongs to the screen this bar is on (0 = the first, -1 = none, for example on the dashboard)")]
        [SerializeField] private int current = -1;

        [Header("Shape")]
        [SerializeField] private float restWidth = 168f;
        [SerializeField] private float restHeight = 132f;
        [Tooltip("How much thinner the drop gets when fully stretched (0 = not at all)")]
        [Range(0f, 0.6f)] [SerializeField] private float squash = 0.28f;
        [Range(0f, 1f)] [SerializeField] private float opacity = 0.42f;

        [Header("Liquid feel")]
        [Tooltip("How quickly the front of the drop follows (higher = snappier)")]
        [SerializeField] private float frontStiffness = 420f;
        [SerializeField] private float frontDamping = 24f;
        [Tooltip("How quickly the back follows (lower than the front = more stretch and wobble)")]
        [SerializeField] private float backStiffness = 190f;
        [SerializeField] private float backDamping = 15f;

        [Header("Colour")]
        [Tooltip("A drag this fast (canvas units per second) turns the drop fully into the fast colour")]
        [SerializeField] private float fastSpeed = 2600f;
        [SerializeField] private float colourFollowSpeed = 9f;
        [SerializeField] private Color restColor = new Color32(217, 142, 160, 255);
        [SerializeField] private Color fastColor = new Color32(184, 138, 59, 255);

        float front, back, frontVelocity, backVelocity, frontTarget, backTarget;
        float size, sizeTarget, speedShown, speedTarget;
        bool ready;

        void Start()
        {
            float home = current >= 0 ? SlotX(current) : SlotX(Mathf.Max(0, slots != null ? slots.Length / 2 : 0));
            front = back = frontTarget = backTarget = home;
            size = sizeTarget = current >= 0 ? 1f : 0f;
            ready = true;
            Draw();
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            restColor = new Color(p.accent.r, p.accent.g, p.accent.b, 1f);
            fastColor = new Color(p.gold.r, p.gold.g, p.gold.b, 1f);
            // in the Editor (scene not playing) the drop is shown where it rests, in its resting colour
            if (!Application.isPlaying && dropImage != null) { Color c = restColor; c.a = current >= 0 ? opacity : 0f; dropImage.color = c; }
        }

        float SlotX(int index)
        {
            if (slots == null || slots.Length == 0 || drop == null) return 0f;
            RectTransform slot = slots[Mathf.Clamp(index, 0, slots.Length - 1)];
            if (slot == null) return 0f;
            var parent = (RectTransform)drop.parent;
            return parent.InverseTransformPoint(slot.TransformPoint(slot.rect.center)).x;
        }

        /// <summary>
        /// Called while the user drags towards another screen. `targetSlot` = that screen's button (-1 = it has none),
        /// `progress` 0 to 1 = how far the drag is, `speed` = how fast it moves (canvas units per second).
        /// </summary>
        public void SetDrag(int targetSlot, float progress, float speed)
        {
            if (!ready) return;
            progress = Mathf.Clamp01(progress);
            speedTarget = speed;
            float from = current >= 0 ? SlotX(current) : (targetSlot >= 0 ? SlotX(targetSlot) : front);
            float to = targetSlot >= 0 ? SlotX(targetSlot) : from;
            // the front runs ahead of the finger, the back hangs behind it
            frontTarget = Mathf.Lerp(from, to, Mathf.Clamp01(progress * 1.25f));
            backTarget = Mathf.Lerp(from, to, Mathf.Clamp01(progress * progress * 0.85f));
            if (current < 0) sizeTarget = targetSlot >= 0 ? progress : 0f;         // from a screen without a button the drop grows under the target
            else sizeTarget = targetSlot >= 0 ? 1f : 1f - progress * 0.6f;        // towards a screen without a button it shrinks
        }

        /// <summary>The drag ended without a change of screen: the drop flows back.</summary>
        public void Release()
        {
            if (!ready) return;
            speedTarget = 0f;
            float home = current >= 0 ? SlotX(current) : front;
            frontTarget = backTarget = home;
            sizeTarget = current >= 0 ? 1f : 0f;
        }

        /// <summary>The user is going to another screen: the drop settles on that screen's button.</summary>
        public void Commit(int targetSlot)
        {
            if (!ready) return;
            if (targetSlot < 0) { sizeTarget = 0f; return; }
            frontTarget = backTarget = SlotX(targetSlot);
            sizeTarget = 1f;
        }

        void Update()
        {
            if (!ready || drop == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.033f);
            Spring(ref front, ref frontVelocity, frontTarget, frontStiffness, frontDamping, dt);
            Spring(ref back, ref backVelocity, backTarget, backStiffness, backDamping, dt);
            size = Mathf.MoveTowards(size, sizeTarget, dt * 6f);
            speedShown = Mathf.Lerp(speedShown, speedTarget, 1f - Mathf.Exp(-colourFollowSpeed * dt));
            Draw();
        }

        static void Spring(ref float value, ref float velocity, float target, float stiffness, float damping, float dt)
        {
            velocity += ((target - value) * stiffness - velocity * damping) * dt;
            value += velocity * dt;
        }

        void Draw()
        {
            if (drop == null) return;
            float stretch = Mathf.Abs(front - back);
            float width = (restWidth + stretch) * Mathf.Clamp01(size);
            float height = restHeight * Mathf.Clamp01(size) * (1f - squash * Mathf.Clamp01(stretch / Mathf.Max(1f, restWidth * 2f)));
            drop.anchoredPosition = new Vector2((front + back) * 0.5f, drop.anchoredPosition.y);
            drop.sizeDelta = new Vector2(width, height);
            if (dropImage != null)
            {
                Color c = Color.Lerp(restColor, fastColor, Mathf.Clamp01(speedShown / Mathf.Max(1f, fastSpeed)));
                c.a = opacity * Mathf.Clamp01(size * 1.5f);
                dropImage.color = c;
            }
        }
    }
}
