using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// "Skeleton" loading for SHAPES: the placeholder shapes under this object (the rows of a list that is loading, the
    /// outline of the next screen) look alive while the user waits. Put it on the parent of the placeholder shapes; every
    /// Image under it takes part. The shapes themselves are ordinary Images: move, resize or recolour them in the Scene view.
    ///
    /// With "Shine" set (the scene builders set it) the shapes are drawn the way the admin website draws them: a blush
    /// base with a rose-to-gold shine travelling across the screen, the same as the bars of TextSkeleton. Without it the
    /// shapes brighten and dim in a wave, as before.
    /// </summary>
    public class SkeletonShimmer : MonoBehaviour
    {
        [Tooltip("The shapes that shimmer. Leave empty to use every Image under this object.")]
        [SerializeField] private Graphic[] shapes;
        [Tooltip("The material with the travelling shine (GlowUp/SkeletonShine). When it is set the shapes are drawn with it and the four settings below are not used.")]
        [SerializeField] private Material shine;

        [Header("Without the shine: a wave of brighter and dimmer")]
        [Tooltip("How see-through the shapes are at their dimmest (0 to 1)")]
        [Range(0f, 1f)] [SerializeField] private float dimAlpha = 0.45f;
        [Tooltip("How see-through they are at their brightest")]
        [Range(0f, 1f)] [SerializeField] private float brightAlpha = 0.95f;
        [Tooltip("Waves per second")]
        [SerializeField] private float speed = 0.9f;
        [Tooltip("Width of one wave in canvas units (the canvas is 1080 wide)")]
        [SerializeField] private float waveWidth = 1300f;
        [Tooltip("In Performance Mode the shapes stay still (lighter for the phone)")]
        [SerializeField] private bool stillInPerformanceMode = true;

        void Awake()
        {
            if (shapes == null || shapes.Length == 0) shapes = GetComponentsInChildren<Image>(true);
            if (shine == null) return;
            foreach (Graphic shape in shapes) if (shape != null) shape.material = shine;
        }

        void Update()
        {
            SkeletonLook.Tick();            // the shine's clock and the theme's colours (shared by every skeleton on the screen)
            if (shapes == null || shine != null) return;
            bool still = stillInPerformanceMode && GlowUp.Performance.PerformanceSettings.ReduceEffects;
            float time = Time.unscaledTime * speed;
            foreach (Graphic shape in shapes)
            {
                if (shape == null) continue;
                float wave = still ? 0.5f : 0.5f + 0.5f * Mathf.Sin((time - shape.rectTransform.position.x / Mathf.Max(1f, waveWidth)) * Mathf.PI * 2f);
                Color c = shape.color;
                c.a = Mathf.Lerp(dimAlpha, brightAlpha, wave);
                shape.color = c;
            }
        }
    }
}
