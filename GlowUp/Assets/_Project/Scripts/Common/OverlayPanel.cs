using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// A pop-up layer (dialog, bottom sheet, picker). It sits INACTIVE in the Hierarchy; tick it
    /// active to see and style it. Show() fades it in and gives its box a small scale-in;
    /// Hide() fades it out. Timings are Inspector fields.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class OverlayPanel : MonoBehaviour
    {
        [Tooltip("The box that scales in (the dim layer behind it only fades)")]
        [SerializeField] private RectTransform box;
        [SerializeField] private Button closeButton;
        [Tooltip("Optional: tapping the dim area closes the pop-up")]
        [SerializeField] private Button backdropButton;

        [Header("Animation")]
        [SerializeField] private float fadeSeconds = 0.18f;
        [Tooltip("The box starts at this scale and grows to 1")]
        [SerializeField] private float startScale = 0.94f;
        [Tooltip("If you leave the pop-up active while designing, it still starts hidden when the app runs")]
        [SerializeField] private bool hideWhenSceneStarts = true;

        [Header("Sideways drag to another screen")]
        [Tooltip("Off (the usual): while this pop-up is open, dragging sideways does not change screen. On = it still does. For a pop-up that opens by itself when the screen opens (AR Try-On's product picker), so the user can go straight back.")]
        [SerializeField] private bool letsSidewaysSwipesThrough = false;

        /// <summary>True when a sideways drag that starts on this pop-up may still change the screen (SwipeSceneNavigator asks).</summary>
        public bool LetsSidewaysSwipesThrough => letsSidewaysSwipesThrough;

        public bool IsOpen { get; private set; }
        public event Action Closed;

        CanvasGroup group;
        bool initialised, showRequested, closing;
        float k;                 // 0 hidden -> 1 shown

        void Initialise()
        {
            if (initialised) return;
            initialised = true;
            group = GetComponent<CanvasGroup>();
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (backdropButton != null) backdropButton.onClick.AddListener(Hide);
        }

        void Awake() { Initialise(); }

        void Start()
        {
            if (hideWhenSceneStarts && !showRequested) { k = 0f; IsOpen = false; gameObject.SetActive(false); }
        }

        public void Show()
        {
            showRequested = true; closing = false; IsOpen = true;
            gameObject.SetActive(true);
            Initialise();
            transform.SetAsLastSibling();          // above everything else in the panel
            group.blocksRaycasts = true; group.interactable = true;
            Paint();
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            Initialise();
            closing = true; IsOpen = false;
            group.interactable = false;
        }

        void Update()
        {
            float target = closing ? 0f : 1f;
            if (!Mathf.Approximately(k, target))
            {
                k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
                Paint();
            }
            else if (closing)
            {
                closing = false;
                gameObject.SetActive(false);
                Closed?.Invoke();
            }
        }

        void Paint()
        {
            if (group != null) group.alpha = k;
            if (box != null) box.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, Mathf.SmoothStep(0f, 1f, k));
        }
    }
}
