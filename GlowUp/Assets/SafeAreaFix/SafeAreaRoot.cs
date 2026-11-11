using UnityEngine;

namespace GlowUp.SafeAreaFix
{
    /// <summary>
    /// The content container of a screen (12 November 2026): its anchors follow the safe area for the edges SafeAreaConfig enables; every other
    /// edge stays fully stretched. It only holds content (titles, cards, buttons, lists); backgrounds stay outside it, on the canvas, so they
    /// still reach the screen's edges. Applied again when the safe area, the screen size, the orientation or the config changes.
    /// Made by SafeAreaStructure (at run time by SafeAreaBootstrap, in the Editor by Tools > SafeAreaFix > Migrate All).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaRoot : MonoBehaviour
    {
        public const string ObjectName = "SafeAreaRoot";

        RectTransform rect;
        Rect lastSafe;
        Vector2Int lastSize;
        ScreenOrientation lastOrientation;

        /// <summary>The insets this root applies now (pixels; 0 for edges the config leaves alone).</summary>
        public InsetProvider.Insets Applied { get; private set; }

        void Awake() { rect = (RectTransform)transform; }
        void OnEnable() { SafeAreaConfig.Changed += Apply; Apply(); }
        void OnDisable() { SafeAreaConfig.Changed -= Apply; }

        void Update()
        {
            if (Screen.safeArea != lastSafe || lastSize.x != Screen.width || lastSize.y != Screen.height || Screen.orientation != lastOrientation) Apply();
        }

        public void Apply()
        {
            if (rect == null) rect = (RectTransform)transform;
            lastSafe = Screen.safeArea; lastSize = new Vector2Int(Screen.width, Screen.height); lastOrientation = Screen.orientation;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            SafeAreaConfig config = SafeAreaConfig.Current;
            InsetProvider.Insets all = InsetProvider.Get();
            var used = new InsetProvider.Insets
            {
                top = config.applyTop ? all.top : 0f, bottom = config.applyBottom ? all.bottom : 0f,
                left = config.applyLeft ? all.left : 0f, right = config.applyRight ? all.right : 0f
            };
            Applied = used;
            rect.anchorMin = new Vector2(used.left / w, used.bottom / h);
            rect.anchorMax = new Vector2(1f - used.right / w, 1f - used.top / h);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
