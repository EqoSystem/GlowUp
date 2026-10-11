using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// "Skeleton" loading: the grey placeholder shapes under this object brighten and dim in a wave that
    /// travels from left to right, so a screen that is still loading looks alive. Put it on the parent
    /// of the placeholder shapes; every Image under it takes part. The shapes themselves are ordinary
    /// Images: move, resize or recolour them in the Scene view.
    /// </summary>
    public class SkeletonShimmer : MonoBehaviour
    {
        [Tooltip("The shapes that shimmer. Leave empty to use every Image under this object.")]
        [SerializeField] private Graphic[] shapes;
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
        }

        void Update()
        {
            if (shapes == null) return;
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
