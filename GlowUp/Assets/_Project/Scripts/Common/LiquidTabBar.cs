using GlowUp.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The pink pill of the bottom bar (Recent | Start Scan | Try On). It rests behind the icon of the screen the user
    /// is on: Recent Scans = the left icon, the dashboard = the middle one, AR Try-On = the right one.
    ///
    /// While the user drags the page sideways the pill FOLLOWS the drag: it slides towards the icon of the screen the
    /// drag leads to, gets longer and flatter the faster it moves (like a drop of liquid), and it is half-way exactly
    /// when letting go would change the screen. When the finger lifts it snaps: on to the new icon, or back to its own,
    /// with a small wobble. On the next screen the pill starts where it was left and finishes its way there, so the
    /// movement carries across the change of screen. The icon the pill is under takes the colour of text on pink.
    ///
    /// SwipeSceneNavigator drives it (SetDrag / Commit / Release). Every size, speed and colour is an Inspector field.
    /// </summary>
    public class LiquidTabBar : ThemeAwareBehaviour
    {
        [Header("References")]
        [Tooltip("The pink pill (an Image behind the icons, a child of the bar)")]
        [SerializeField] private RectTransform drop;
        [SerializeField] private Image dropImage;
        [Tooltip("The bar's buttons from left to right")]
        [SerializeField] private RectTransform[] slots;
        [Tooltip("The icon of each button, in the same order (they change colour while the pill is under them). Optional.")]
        [SerializeField] private Graphic[] slotIcons;
        [Tooltip("Which button belongs to the screen this bar is on (0 = the first; -1 = none: the pill is hidden)")]
        [SerializeField] private int current = -1;

        [Header("Shape")]
        [Tooltip("The pill at rest (a circle when both are the same)")]
        [SerializeField] private float restWidth = 108f;
        [SerializeField] private float restHeight = 108f;
        [Tooltip("How much longer the pill can get at full speed (canvas units)")]
        [SerializeField] private float longestStretch = 150f;
        [Tooltip("How narrow the pill may squeeze for a moment when it arrives, as a share of its resting width (1 = it never squeezes). It gets taller as it squeezes, so keep it large enough to stay inside the bar.")]
        [Range(0.6f, 1f)] [SerializeField] private float narrowestShare = 0.86f;
        [Tooltip("How much flatter it gets as it gets longer (1 = it keeps its area like a real drop, 0 = its height never changes)")]
        [Range(0f, 1f)] [SerializeField] private float squash = 0.7f;
        [Tooltip("On = the pill never gets taller than at rest (phase 1D, 11 November 2026: a taller pill touched the bar's edge and the labels)")]
        [SerializeField] private bool neverTallerThanRest = true;
        [Range(0f, 1f)] [SerializeField] private float opacity = 1f;

        [Header("Liquid feel")]
        [Tooltip("While the finger drags: how tightly the pill follows it (higher = tighter)")]
        [SerializeField] private float followStiffness = 520f;
        [SerializeField] private float followDamping = 36f;
        [Tooltip("When the finger lifts: how hard the pill snaps to its icon (higher = quicker)")]
        [SerializeField] private float snapStiffness = 300f;
        [Tooltip("Lower = it swings past the icon and settles (more liquid); higher = it stops dead")]
        [SerializeField] private float snapDamping = 21f;
        [Tooltip("Extra length for every canvas unit per second the pill moves")]
        [SerializeField] private float stretchPerSpeed = 0.075f;
        [Tooltip("How springy the stretch itself is: after a quick move the pill squeezes narrow and tall for a moment, then rounds off")]
        [SerializeField] private float wobbleStiffness = 300f;
        [SerializeField] private float wobbleDamping = 15f;

        [Header("Across a change of screen")]
        [Tooltip("On = on the next screen the pill starts where it was on the last one and flows to its icon. Off = it is simply there.")]
        [SerializeField] private bool flowInFromLastScreen = true;

        [Header("Colour (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color restColor = new Color32(217, 107, 138, 255);
        [Tooltip("An icon that is not under the pill")]
        [SerializeField] private Color iconColor = new Color32(58, 47, 48, 255);
        [Tooltip("An icon while the pill is under it")]
        [SerializeField] private Color iconOnPillColor = new Color32(58, 47, 48, 255);

        // where the pill was when the last screen was left, and the screen that was being opened then (the bars of all
        // screens have the same layout, so the place carries over)
        static float carriedX;
        static string carriedTo;

        float x, velocity, target, stretch, stretchVelocity;
        float[] slotX;
        bool ready, dragging, arriving;
        int redraws;

        /// <summary>The button of this screen (-1 = none).</summary>
        public int Current => current;

        protected override void OnEnable()
        {
            base.OnEnable();
            SceneNavigator.Leaving += RememberPlace;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            SceneNavigator.Leaving -= RememberPlace;
            // the screen is going: the next screen's pill starts exactly where this one is now, so its way to the tapped or
            // swiped icon goes on without a jump (nothing covers the change of screen)
            if (ready && current >= 0 && carriedTo != null) carriedX = x;
        }

        void RememberPlace(string nextScene)
        {
            if (!ready || current < 0) return;
            carriedX = x; carriedTo = nextScene;
        }

        void Start()
        {
            MeasureSlots();
            float home = current >= 0 ? SlotX(current) : 0f;
            x = target = home;
            bool calm = GlowUp.Performance.PerformanceSettings.ReduceEffects;
            // only when the user came STRAIGHT here from a screen with a bar (not by way of the scanner or Settings)
            if (flowInFromLastScreen && !calm && current >= 0 && carriedTo == SceneManager.GetActiveScene().name && Mathf.Abs(carriedX - home) > 1f)
            {
                x = carriedX; arriving = true;          // it waits there until the new screen can be seen, then flows home
            }
            carriedTo = null;
            ready = true;
            Draw();
        }

        void OnRectTransformDimensionsChange()
        {
            if (!ready) return;
            MeasureSlots();
            if (!dragging && current >= 0)
            {
                target = SlotX(current);
                // the bar changed size (the screen was just laid out, or turned): the pill is simply put where it belongs.
                // It must not slide there as if the user had moved it. (On its way in from the last screen it keeps going.)
                if (!arriving) { x = target; velocity = stretch = stretchVelocity = 0f; }
            }
            redraws = 2;
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            restColor = new Color(p.buttonFill.r, p.buttonFill.g, p.buttonFill.b, 1f);
            iconColor = p.mainText; iconOnPillColor = p.buttonText;
            // in the Editor (scene not playing) the pill is shown where it rests, in its resting colour
            if (!Application.isPlaying && dropImage != null) { Color c = restColor; c.a = current >= 0 ? opacity : 0f; dropImage.color = c; }
            redraws = 2;
        }

        void MeasureSlots()
        {
            int count = slots != null ? slots.Length : 0;
            if (slotX == null || slotX.Length != count) slotX = new float[count];
            if (drop == null) return;
            var parent = (RectTransform)drop.parent;
            for (int i = 0; i < count; i++)
                slotX[i] = slots[i] != null ? parent.InverseTransformPoint(slots[i].TransformPoint(slots[i].rect.center)).x : 0f;
        }

        float SlotX(int index)
        {
            if (slotX == null || slotX.Length == 0) return 0f;
            return slotX[Mathf.Clamp(index, 0, slotX.Length - 1)];
        }

        /// <summary>
        /// Called while the user drags towards another screen. `targetSlot` = that screen's button (-1 = there is no
        /// screen on that side), `progress` 0 to 1 = how far the pill is on its way to that button.
        /// </summary>
        public void SetDrag(int targetSlot, float progress)
        {
            if (!ready || current < 0) return;
            dragging = true; arriving = false;
            float from = SlotX(current);
            target = targetSlot >= 0 ? Mathf.Lerp(from, SlotX(targetSlot), Mathf.Clamp01(progress)) : from;
        }

        /// <summary>The drag ended without a change of screen: the pill flows back to its own icon.</summary>
        public void Release()
        {
            if (!ready) return;
            dragging = false;
            if (current >= 0) target = SlotX(current);
        }

        /// <summary>The user is going to another screen: the pill heads for that screen's button.</summary>
        public void Commit(int targetSlot)
        {
            if (!ready) return;
            dragging = false;
            if (targetSlot >= 0 && current >= 0) target = SlotX(targetSlot);
        }

        void Update()
        {
            if (!ready || drop == null || current < 0) return;
            if (arriving)
            {
                if (ScreenTransition.IsBusy) return;       // the new screen is still covered: the pill waits where it was left
                arriving = false;
            }
            bool still = Mathf.Abs(target - x) < 0.05f && Mathf.Abs(velocity) < 0.5f && Mathf.Abs(stretch) < 0.05f && Mathf.Abs(stretchVelocity) < 0.5f;
            if (still && !dragging) { if (redraws > 0) { redraws--; Draw(); } return; }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.033f);
            if (GlowUp.Performance.PerformanceSettings.ReduceEffects)
            {
                // Performance Mode: the pill slides without stretching or wobbling
                x = Mathf.MoveTowards(x, target, Mathf.Max(60f, Mathf.Abs(target - x) * 14f) * dt);
                velocity = stretch = stretchVelocity = 0f;
            }
            else
            {
                Spring(ref x, ref velocity, target, dragging ? followStiffness : snapStiffness, dragging ? followDamping : snapDamping, dt);
                float wanted = Mathf.Min(longestStretch, Mathf.Abs(velocity) * stretchPerSpeed);
                Spring(ref stretch, ref stretchVelocity, wanted, wobbleStiffness, wobbleDamping, dt);
            }
            Draw();
        }

        static void Spring(ref float value, ref float speed, float goal, float stiffness, float damping, float dt)
        {
            speed += ((goal - value) * stiffness - speed * damping) * dt;
            value += speed * dt;
        }

        void Draw()
        {
            if (drop == null) return;
            // longer than at rest while it moves; for a moment narrower (and taller) when it has just arrived
            float width = Mathf.Clamp(restWidth + stretch, restWidth * narrowestShare, restWidth + longestStretch);
            float height = restHeight * Mathf.Lerp(1f, restWidth / Mathf.Max(1f, width), squash);
            if (neverTallerThanRest) height = Mathf.Min(height, restHeight);
            drop.anchoredPosition = new Vector2(x, drop.anchoredPosition.y);
            drop.sizeDelta = current >= 0 ? new Vector2(width, height) : Vector2.zero;
            if (dropImage != null) { Color c = restColor; c.a = current >= 0 ? opacity : 0f; dropImage.color = c; }

            if (slotIcons == null || slotX == null) return;
            float half = width * 0.5f;
            for (int i = 0; i < slotIcons.Length && i < slotX.Length; i++)
            {
                if (slotIcons[i] == null) continue;
                float under = current >= 0 ? 1f - Mathf.Clamp01((Mathf.Abs(x - slotX[i]) - half * 0.35f) / Mathf.Max(1f, half * 0.9f)) : 0f;
                Color c = Color.Lerp(iconColor, iconOnPillColor, under);
                c.a = slotIcons[i].color.a;
                slotIcons[i].color = c;
            }
        }
    }
}
