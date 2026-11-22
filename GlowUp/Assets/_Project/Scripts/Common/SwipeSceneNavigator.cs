using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Drag the screen sideways to jump to the neighbouring main screen. The screens stand in the row "Swipe Row", in
    /// the order of the bottom bar: Recent Scans | the dashboard | AR Try-On. The page follows the finger like a page
    /// being turned, so a drag PULLS IN the screen from the other side (the project owner's drawing of 13 October 2026):
    ///
    ///   on the dashboard:   drag RIGHT = Recent Scans,   drag LEFT = AR Try-On
    ///   on Recent Scans:    drag LEFT = back to the dashboard
    ///   on AR Try-On:       drag RIGHT = back to the dashboard
    ///
    /// The scanner and the Settings page are NOT in the row: they are opened with their buttons and left with their
    /// back button, so a sideways drag never lands in the camera by accident. On a screen that is not in the row this
    /// script does nothing.
    ///
    /// The page follows the finger and the pink pill of the bottom bar slides along (LiquidTabBar); when the drag is long
    /// or fast enough the app changes screen, otherwise the page springs back.
    ///
    /// Drags that start on a slider, a text box, a sideways list or an open pop-up are left alone. A pop-up with "Lets
    /// Sideways Swipes Through" switched on (AR Try-On's product picker, which opens by itself) does not stop the drag.
    /// </summary>
    public class SwipeSceneNavigator : MonoBehaviour
    {
        /// <summary>Other scripts set this while they use sideways drags themselves (dragging a tile to delete it).</summary>
        public static bool Suppressed;

        /// <summary>The screens a sideways drag moves through, in the order of the bottom bar.</summary>
        public static string[] DefaultRow => new[] { SceneNames.RecentScans, SceneNames.Dashboard, SceneNames.ArTryOn };

        [Header("Screens")]
        [SerializeField] private string[] swipeRow = DefaultRow;
        [Tooltip("The bottom bar button of each screen in the row above (-1 = that screen has no button)")]
        [SerializeField] private int[] barSlotOfRow = { 0, 1, 2 };
        // (this switch has a new name on purpose: a scene built with the earlier one, which went the other way, takes this default)
        [Tooltip("On = the page follows the finger like a page being turned: dragging to the LEFT opens the NEXT screen of the row (the one on the right in the bottom bar), dragging to the RIGHT the one before. Off = the other way round.")]
        [SerializeField] private bool dragLeftOpensNext = true;

        [Header("References")]
        [Tooltip("The page that follows the finger (the scene's card)")]
        [SerializeField] private RectTransform page;
        [Tooltip("On the page: lets this script stop taps from landing while the page is being dragged")]
        [SerializeField] private CanvasGroup pageGroup;
        [SerializeField] private LiquidTabBar tabBar;

        [Header("Feel (canvas units; the canvas is 1080 wide)")]
        [Tooltip("Sideways movement before a drag counts as a swipe")]
        [SerializeField] private float startDistance = 34f;
        [Tooltip("A swipe must be this many times more sideways than up-and-down")]
        [SerializeField] private float sidewaysRatio = 1.7f;
        [Tooltip("How much of the finger's movement the page follows (1 = all of it)")]
        [Range(0.1f, 1f)] [SerializeField] private float follow = 0.6f;
        [Tooltip("The page follows this little when there is no screen on that side")]
        [Range(0f, 0.5f)] [SerializeField] private float followAtTheEnd = 0.12f;
        [Tooltip("Drag this far and let go: the app changes screen")]
        [SerializeField] private float commitDistance = 250f;
        [Tooltip("Or flick faster than this (units per second)")]
        [SerializeField] private float commitSpeed = 1500f;
        [Tooltip("Seconds the page takes to spring back")]
        [SerializeField] private float returnSeconds = 0.2f;
        [Tooltip("The bar's pill reaches the next icon when the finger has moved this far. Twice 'Commit Distance' = the pill is half-way exactly when letting go changes the screen.")]
        [SerializeField] private float pillTravelDistance = 500f;

        Canvas rootCanvas;
        Vector2 pressAt, lastAt;
        Vector2 pageHome, popUpHome;
        RectTransform popUp;                 // the pop-up the drag started on, when it lets drags through: it moves with the page
        CanvasGroup popUpGroup;              // ... and takes no taps while it is dragged (letting go must not "tap" its dim area and close it)
        bool popUpTookTaps;
        bool pressed, blocked, swiping, returning;
        float velocity, returnT, returnFrom;
        static readonly List<RaycastResult> hits = new List<RaycastResult>();

        void Awake()
        {
            if (page != null) { rootCanvas = page.GetComponentInParent<Canvas>(); if (rootCanvas != null) rootCanvas = rootCanvas.rootCanvas; pageHome = page.anchoredPosition; }
        }

        void OnDisable()
        {
            if (page != null) page.anchoredPosition = pageHome;
            LetGoOfPopUp();
            if (pageGroup != null) pageGroup.blocksRaycasts = true;
            pressed = swiping = returning = false;
        }

        float Scale => rootCanvas != null && rootCanvas.scaleFactor > 0.001f ? rootCanvas.scaleFactor : 1f;

        int Index => System.Array.IndexOf(swipeRow, SceneManager.GetActiveScene().name);

        int TargetIndex(float dx) => Neighbour(swipeRow, SceneManager.GetActiveScene().name, dx > 0f, dragLeftOpensNext);

        /// <summary>
        /// The place in `row` a sideways drag leads to from the screen `current`; -1 = nowhere (there is no screen on that
        /// side, or `current` is not in the row at all: the scanner, Settings).
        /// </summary>
        public static int Neighbour(string[] row, string current, bool dragToTheRight, bool dragLeftOpensNext = true)
        {
            int index = row != null ? System.Array.IndexOf(row, current) : -1;
            if (index < 0) return -1;
            int target = index + (dragToTheRight != dragLeftOpensNext ? 1 : -1);
            return target >= 0 && target < row.Length ? target : -1;
        }

        int SlotOf(int screenIndex) => screenIndex >= 0 && barSlotOfRow != null && screenIndex < barSlotOfRow.Length ? barSlotOfRow[screenIndex] : -1;

        void Update()
        {
            if (page == null) return;
            if (returning) { SpringBack(); return; }
            if (ScreenTransition.IsBusy) return;

            if (Input.GetMouseButtonDown(0))
            {
                pressed = true; swiping = false; velocity = 0f;
                pressAt = lastAt = Input.mousePosition;
                // a screen that is not in the row (the scanner, Settings) has no sideways drag at all
                blocked = Index < 0 || Suppressed || StartsOnSomethingThatDrags(pressAt, out popUp);
                if (blocked) popUp = null;
                popUpGroup = popUp != null ? popUp.GetComponent<CanvasGroup>() : null;
                if (popUp != null) { popUpHome = popUp.anchoredPosition; popUpTookTaps = popUpGroup == null || popUpGroup.blocksRaycasts; }
                return;
            }
            if (!pressed) return;

            Vector2 now = Input.mousePosition;
            float dx = (now.x - pressAt.x) / Scale, dy = (now.y - pressAt.y) / Scale;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            velocity = Mathf.Lerp(velocity, (now.x - lastAt.x) / Scale / dt, 0.35f);
            lastAt = now;

            if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)) { Finish(dx); return; }
            if (blocked || Suppressed) { if (swiping) Finish(0f); return; }

            if (!swiping)
            {
                if (Mathf.Abs(dx) < startDistance || Mathf.Abs(dx) < Mathf.Abs(dy) * sidewaysRatio) return;
                swiping = true;
                if (pageGroup != null) pageGroup.blocksRaycasts = false;        // the finger must not "tap" whatever is under it when it lifts
                if (popUpGroup != null) popUpGroup.blocksRaycasts = false;
            }

            int target = TargetIndex(dx);
            MovePage(dx * (target >= 0 ? follow : followAtTheEnd));
            if (tabBar != null) tabBar.SetDrag(SlotOf(target), target >= 0 ? Mathf.Abs(dx) / Mathf.Max(1f, pillTravelDistance) : 0f);
        }

        void MovePage(float offset)
        {
            page.anchoredPosition = pageHome + new Vector2(offset, 0f);
            if (popUp != null) popUp.anchoredPosition = popUpHome + new Vector2(offset, 0f);
        }

        void Finish(float dx)
        {
            pressed = false;
            if (!swiping) return;
            swiping = false;
            int target = TargetIndex(dx);
            bool far = Mathf.Abs(dx) >= commitDistance;
            bool fast = Mathf.Abs(velocity) >= commitSpeed && Mathf.Abs(dx) >= startDistance * 2f && Mathf.Sign(velocity) == Mathf.Sign(dx);
            if (target >= 0 && (far || fast) && !Suppressed && swipeRow[target] == SceneNames.ArTryOn && !TryOnGate.Allowed)
            {
                TryOnGate.RaiseBlocked();                                           // no product to try on: the screen springs back and says why
                target = -1;
            }
            if (target >= 0 && (far || fast) && !Suppressed)
            {
                if (tabBar != null) tabBar.Commit(SlotOf(target));
                // the next screen comes in from the side the finger came from
                SceneNavigator.Go(swipeRow[target], dx > 0f ? -1 : 1);
                return;
            }
            if (tabBar != null) tabBar.Release();
            returning = true; returnT = 0f; returnFrom = page.anchoredPosition.x - pageHome.x;
        }

        void SpringBack()
        {
            returnT = returnSeconds <= 0.01f ? 1f : Mathf.Min(1f, returnT + Time.unscaledDeltaTime / returnSeconds);
            float k = 1f - Mathf.Pow(1f - returnT, 3f);
            MovePage(Mathf.Lerp(returnFrom, 0f, k));
            if (returnT < 1f) return;
            returning = false;
            LetGoOfPopUp();
            if (pageGroup != null) pageGroup.blocksRaycasts = true;
        }

        /// <summary>The pop-up that was dragged along is put back where it was and takes taps again.</summary>
        void LetGoOfPopUp()
        {
            if (popUp != null) popUp.anchoredPosition = popUpHome;
            if (popUpGroup != null && popUpTookTaps) popUpGroup.blocksRaycasts = true;
            popUp = null; popUpGroup = null;
        }

        // True when the press landed on something that uses sideways drags itself, or on a pop-up that keeps drags to
        // itself. `passingPopUp` = the pop-up the press landed on when that pop-up lets sideways drags through.
        static bool StartsOnSomethingThatDrags(Vector2 screenPoint, out RectTransform passingPopUp)
        {
            passingPopUp = null;
            EventSystem system = EventSystem.current;
            if (system == null) return false;
            var pointer = new PointerEventData(system) { position = screenPoint };
            hits.Clear();
            system.RaycastAll(pointer, hits);
            if (hits.Count == 0) return false;
            for (Transform t = hits[0].gameObject.transform; t != null; t = t.parent)
            {
                if (t.GetComponent<Slider>() != null || t.GetComponent<Scrollbar>() != null || t.GetComponent<TMP_InputField>() != null) return true;
                if (t.GetComponent<IOwnsSidewaysDrag>() != null) return true;        // a row swiped to act on it (TileSwipeActions)
                ScrollRect scroll = t.GetComponent<ScrollRect>();
                if (scroll != null && scroll.horizontal) return true;
                OverlayPanel panel = t.GetComponent<OverlayPanel>();
                if (panel != null)
                {
                    if (!panel.LetsSidewaysSwipesThrough) return true;
                    passingPopUp = t as RectTransform;
                    return false;
                }
            }
            return false;
        }
    }
}
