using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Drag the screen sideways to jump to the neighbouring main screen. The screens stand in the row
    /// "Screens In Order"; with the default setting a drag to the RIGHT opens the next one in the row and a
    /// drag to the LEFT the one before (from the dashboard: left = Recent Scans, right = AR Try-On; from
    /// AR Try-On: left = the dashboard, left again = Recent Scans). The scanner is not in the row: it is
    /// opened with its button, so a sideways drag never lands in the camera by accident. The page follows the finger, the bottom bar's drop flows along, and when
    /// the drag is long or fast enough the app changes screen; otherwise the page springs back.
    ///
    /// Drags that start on a slider, a text box, a sideways list or an open pop-up are left alone.
    /// </summary>
    public class SwipeSceneNavigator : MonoBehaviour
    {
        /// <summary>Other scripts set this while they use sideways drags themselves (dragging a tile to delete it).</summary>
        public static bool Suppressed;

        [Header("Screens")]
        [SerializeField] private string[] swipeRow = { SceneNames.RecentScans, SceneNames.Dashboard, SceneNames.ArTryOn };
        [Tooltip("The bottom bar button of each screen in the row above (-1 = that screen has no button)")]
        [SerializeField] private int[] barSlotOfRow = { 0, -1, 2 };
        [Tooltip("On = dragging right opens the NEXT screen of the row. Off = the other way round.")]
        [SerializeField] private bool dragRightOpensNext = true;

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

        Canvas rootCanvas;
        Vector2 pressAt, lastAt;
        Vector2 pageHome;
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
            if (pageGroup != null) pageGroup.blocksRaycasts = true;
            pressed = swiping = returning = false;
        }

        float Scale => rootCanvas != null && rootCanvas.scaleFactor > 0.001f ? rootCanvas.scaleFactor : 1f;

        int Index => System.Array.IndexOf(swipeRow, SceneManager.GetActiveScene().name);

        int TargetIndex(float dx)
        {
            int index = Index;
            if (index < 0) return -1;
            int step = (dx > 0f) == dragRightOpensNext ? 1 : -1;
            int target = index + step;
            return target >= 0 && target < swipeRow.Length ? target : -1;
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
                blocked = Suppressed || StartsOnSomethingThatDrags(pressAt);
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
            }

            int target = TargetIndex(dx);
            page.anchoredPosition = pageHome + new Vector2(dx * (target >= 0 ? follow : followAtTheEnd), 0f);
            if (tabBar != null) tabBar.SetDrag(SlotOf(target), target >= 0 ? Mathf.Abs(dx) / Mathf.Max(1f, commitDistance) : 0f, Mathf.Abs(velocity));
        }

        void Finish(float dx)
        {
            pressed = false;
            if (!swiping) return;
            swiping = false;
            int target = TargetIndex(dx);
            bool far = Mathf.Abs(dx) >= commitDistance;
            bool fast = Mathf.Abs(velocity) >= commitSpeed && Mathf.Abs(dx) >= startDistance * 2f && Mathf.Sign(velocity) == Mathf.Sign(dx);
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
            page.anchoredPosition = pageHome + new Vector2(Mathf.Lerp(returnFrom, 0f, k), 0f);
            if (returnT < 1f) return;
            returning = false;
            if (pageGroup != null) pageGroup.blocksRaycasts = true;
        }

        // True when the press landed on something that uses sideways drags itself, or on a pop-up.
        static bool StartsOnSomethingThatDrags(Vector2 screenPoint)
        {
            EventSystem system = EventSystem.current;
            if (system == null) return false;
            var pointer = new PointerEventData(system) { position = screenPoint };
            hits.Clear();
            system.RaycastAll(pointer, hits);
            if (hits.Count == 0) return false;
            for (Transform t = hits[0].gameObject.transform; t != null; t = t.parent)
            {
                if (t.GetComponent<Slider>() != null || t.GetComponent<Scrollbar>() != null || t.GetComponent<TMP_InputField>() != null) return true;
                if (t.GetComponent<OverlayPanel>() != null) return true;
                ScrollRect scroll = t.GetComponent<ScrollRect>();
                if (scroll != null && scroll.horizontal) return true;
            }
            return false;
        }
    }
}
