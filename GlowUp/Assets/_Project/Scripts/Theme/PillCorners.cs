using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Theme
{
    /// <summary>
    /// Keeps a sliced rounded Image perfectly round at any size: the corner radius follows the
    /// object's height (a full pill for buttons and inputs) up to a maximum for big cards.
    /// Edit "Max Radius" / "Radius Ratio" in the Inspector. Works in the Editor too.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    public class PillCorners : MonoBehaviour
    {
        /// <summary>Corner radius (in pixels) baked into the generated round sprites.</summary>
        public const float SpriteRadius = 64f;

        [Tooltip("0.5 = fully round ends (pill). Smaller = squarer.")]
        [Range(0.05f, 0.5f)] [SerializeField] private float radiusRatio = 0.5f;
        [Tooltip("Large cards/tiles stop rounding at this radius")]
        [SerializeField] private float maxRadius = 80f;
        [Tooltip("Only for glow / neon sprites: the soft margin (in sprite pixels) drawn around the shape. 0 for plain shapes.")]
        [SerializeField] private float spriteMargin = 0f;

        Image image;
        float lastH = -1;

        void OnEnable() { lastH = -1; Refresh(); }
        void OnRectTransformDimensionsChange() { Refresh(); }
#if UNITY_EDITOR
        void OnValidate() { lastH = -1; if (isActiveAndEnabled) Refresh(); }
#endif

        void Refresh()
        {
            if (image == null) image = GetComponent<Image>();
            if (image == null || image.sprite == null) return;
            var rt = (RectTransform)transform;
            float h = Mathf.Min(rt.rect.height, rt.rect.width);
            if (h <= 1f || Mathf.Approximately(h, lastH)) return;
            lastH = h;
            // when the sprite carries a glow margin, the rect is larger than the shape it surrounds
            float shapeHeight = h / (1f + 2f * spriteMargin * radiusRatio / SpriteRadius);
            float radius = Mathf.Max(4f, Mathf.Min(shapeHeight * radiusRatio, maxRadius));
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = SpriteRadius / radius;
        }
    }
}
