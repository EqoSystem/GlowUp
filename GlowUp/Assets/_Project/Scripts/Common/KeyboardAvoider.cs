using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// KEYBOARD AVOIDER: the field being typed in, and its badges, never sit under the phone's keyboard.
    ///
    /// One object per scene ("Keyboard Avoider", made by KeyboardKit in the shared last pass of the scene builders). It listens to
    /// the select / deselect events of ALL text fields of the scene, so nothing is polled while no field is in use.
    ///   1. A field inside a ScrollRect: the content gets BOTTOM PADDING equal to the part of the scroll view the keyboard covers
    ///      (the content's layout group padding; a content without a layout group shrinks the viewport instead). Then the
    ///      ScrollRect eases (about 0.25 s) so the field AND its badges (KeyboardKeepVisible) sit just above the keyboard.
    ///      This is redone when the badges change size (a checklist that grows).
    ///   2. A field with no ScrollRect (a single-field screen or pop-up): the part of the screen that holds it slides up instead.
    ///   3. While a field is focused the keyboard is measured 10 times a second. When the keyboard reports hidden (also the
    ///      Android back button, which fires no deselect) the padding and the slide ease back to nothing.
    /// Nothing the user types is read or logged. Fields created at run time: call Register(field).
    /// In the Editor there is no keyboard: tick "Pretend A Keyboard In Editor" to try it.
    /// </summary>
    public class KeyboardAvoider : MonoBehaviour
    {
        [Header("What it does")]
        [Tooltip("Off = the keyboard is left alone (nothing moves)")]
        [SerializeField] private bool avoidKeyboard = true;
        [Tooltip("On = Android's extra typing bar above the keyboard is not shown: the user types straight into the field on screen, which is what this keeps visible")]
        [SerializeField] private bool hideAndroidTypingBar = true;

        [Header("Room (canvas units: the canvas is 1080 wide)")]
        [Tooltip("Space between the keyboard's top edge and the lowest thing that must be seen")]
        [Min(0f)] [SerializeField] private float gapAboveKeyboard = 28f;
        [Tooltip("Room kept under a field that has no KeyboardKeepVisible list, for a small badge or check line under it")]
        [Min(0f)] [SerializeField] private float spaceBelowField = 64f;
        [Tooltip("The field's top never goes higher than this far under the screen's top (slide only; the header and back button stay usable)")]
        [Min(0f)] [SerializeField] private float topMargin = 150f;

        [Header("Movement")]
        [Tooltip("How quickly padding, scroll and slide ease (bigger = quicker; 14 is about a quarter of a second)")]
        [Min(1f)] [SerializeField] private float followSpeed = 14f;
        [Tooltip("For this long after a tap, a keyboard change or a badge change the scroll view is moved; after that the user's own scrolling wins")]
        [Min(0.1f)] [SerializeField] private float scrollSeconds = 0.35f;
        [Tooltip("How often the keyboard is measured while a field is focused (seconds). Nothing is measured while no field is focused.")]
        [Range(0.03f, 0.5f)] [SerializeField] private float measureEverySeconds = 0.1f;
        [Tooltip("Optional: always slide THIS part of the screen (fields without a ScrollRect). Empty = the canvas child that holds the field.")]
        [SerializeField] private RectTransform shiftTarget;

        [Header("Trying it in the Editor (opt-in)")]
        [SerializeField] private bool pretendAKeyboardInEditor = false;
        [Tooltip("The share of the screen's height the pretend keyboard takes")]
        [Range(0f, 0.6f)] [SerializeField] private float editorKeyboardShare = 0.38f;

        [Header("Development builds only")]
        [Tooltip("Logs the raw keyboard numbers (never the text) when they change, in development builds and the Editor")]
        [SerializeField] private bool logNumbers = false;

        // bottom padding of one scroll view (or one slid part of the screen); `now` is in canvas units
        class Part
        {
            public RectTransform rect;                  // the scroll content / viewport, or the slid part of the screen
            public LayoutGroup group;                   // scroll content's layout group, if it has one
            public ScrollRect scroll;                   // null for a slid part
            public int originalBottom;                  // group.padding.bottom before we touched it
            public Vector2 originalValue;               // viewport.offsetMin, or the slid part's anchoredPosition
            public float now;
        }

        readonly List<TMP_InputField> registered = new List<TMP_InputField>();
        readonly List<Part> parts = new List<Part>();
        readonly Vector3[] corners = new Vector3[4];
        TMP_InputField current;
        float share, lastShare = -1f, nextMeasure, scrollUntil, lastTop, lastBottom;

        void Start()
        {
            KeyboardInsets.LogNumbers = logNumbers;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (logNumbers) MakeNumbersLabel();
#endif
            foreach (TMP_InputField f in FindObjectsOfType<TMP_InputField>(true)) Register(f);
            enabled = false;                           // nothing to do until a field is selected
        }

        void OnDestroy()
        {
            foreach (TMP_InputField f in registered)
                if (f != null) { f.onSelect.RemoveListener(OnSelect); f.onDeselect.RemoveListener(OnDeselect); }
            registered.Clear();
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // Development builds and the Editor only: not compiled into a release build, so nothing is created there.
        // Shows the method used and the insets / frame / area / used numbers. Never any typed text.
        TMPro.TextMeshProUGUI numbersLabel;
        string shownNumbers;

        void MakeNumbersLabel()
        {
            var root = new GameObject("Keyboard Numbers (development)", typeof(Canvas));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(root.transform, false);
            numbersLabel = textObject.AddComponent<TMPro.TextMeshProUGUI>();
            numbersLabel.raycastTarget = false;
            numbersLabel.fontSize = 28;
            numbersLabel.color = Color.yellow;
            numbersLabel.alignment = TMPro.TextAlignmentOptions.TopLeft;
            numbersLabel.text = "keyboard: " + KeyboardInsets.Describe();
            var rect = numbersLabel.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, -60f); rect.sizeDelta = new Vector2(-24f, 120f);
        }

        void ShowNumbers()
        {
            if (numbersLabel == null) return;
            string now = KeyboardInsets.Describe();
            if (now == shownNumbers) return;
            shownNumbers = now;
            numbersLabel.text = "keyboard: " + now;
        }
#endif

        void OnDisable()
        {
            foreach (Part p in parts) Apply(p, 0f);
            parts.Clear();
        }

        /// <summary>Watches a field. Called for every field of the scene at start; call it for fields made at run time.</summary>
        public void Register(TMP_InputField field)
        {
            if (field == null || registered.Contains(field)) return;
            registered.Add(field);
            if (hideAndroidTypingBar) field.shouldHideMobileInput = true;
            field.onSelect.AddListener(OnSelect);
            field.onDeselect.AddListener(OnDeselect);
        }

        // the string these events carry is the typed text: it is never read
        void OnSelect(string _)
        {
            if (!avoidKeyboard) return;
            current = Selected();                       // TMP raises onSelect before isFocused turns true; Update checks it from the next frame
            if (current == null) return;
            lastShare = -1f; nextMeasure = 0f;
            scrollUntil = Time.unscaledTime + scrollSeconds;
            enabled = true;
        }

        void OnDeselect(string _)
        {
            if (current != null && !current.isFocused) current = null;
        }

        void Update()
        {
            if (current != null && !current.isFocused) current = null;       // lost focus without a deselect event

            if (current != null && Time.unscaledTime >= nextMeasure)
            {
                nextMeasure = Time.unscaledTime + measureEverySeconds;
                share = Application.isEditor ? (pretendAKeyboardInEditor ? editorKeyboardShare : 0f) : KeyboardInsets.MeasureShare();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                ShowNumbers();
#endif
                if (Mathf.Abs(share - lastShare) > 0.01f) { lastShare = share; scrollUntil = Time.unscaledTime + scrollSeconds; }
            }

            // keyboard hidden (back button included): the field stays focused but nothing is kept for it
            bool covered = current != null && share > 0f;                      // not covered = every goal below is zero

            float t = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            Part active = null;
            float goal = 0f;

            if (covered)
            {
                Canvas canvas = current.GetComponentInParent<Canvas>();
                RectTransform canvasRect = canvas != null ? (RectTransform)canvas.rootCanvas.transform : null;
                if (canvasRect != null)
                {
                    float h = canvasRect.rect.height;
                    float keyboardTop = -h * 0.5f + share * h + gapAboveKeyboard;      // canvas space, the middle is 0
                    Bounds(current, canvasRect, out float bottom, out float top);
                    if (Mathf.Abs(bottom - lastBottom) > 1f || Mathf.Abs(top - lastTop) > 1f)   // badges grew or shrank
                    {
                        lastBottom = bottom; lastTop = top;
                        scrollUntil = Time.unscaledTime + scrollSeconds;
                    }

                    ScrollRect scroll = current.GetComponentInParent<ScrollRect>();
                    if (scroll != null && scroll.vertical && scroll.content != null && scroll.viewport != null)
                    {
                        active = PartFor(scroll);
                        Edges(scroll.viewport, canvasRect, out float viewBottom, out float viewTop);
                        float baseBottom = active.group != null ? viewBottom : viewBottom - active.now;   // viewport before any shrink
                        goal = Mathf.Max(0f, keyboardTop - baseBottom);                                    // 1. bottom padding

                        if (Time.unscaledTime < scrollUntil)                                               // 2. ease the field into view
                        {
                            float visibleBottom = Mathf.Max(viewBottom, keyboardTop);
                            float delta = 0f;
                            if (top - bottom <= viewTop - visibleBottom)
                            {
                                if (bottom < visibleBottom) delta = visibleBottom - bottom;
                                else if (top > viewTop) delta = viewTop - top;
                            }
                            else if (top > viewTop || top < visibleBottom) delta = viewTop - top;           // too tall: its top in view
                            if (Mathf.Abs(delta) > 0.5f) ScrollBy(scroll, canvasRect, delta * t);
                        }
                    }
                    else
                    {
                        // 3. no scroll area: the part of the screen that holds the field slides up
                        RectTransform slide = SlideTargetOf(current, canvasRect);
                        if (slide != null)
                        {
                            active = PartFor(slide);
                            float need = active.now + (keyboardTop - bottom);
                            float roomAtTop = active.now + (h * 0.5f - topMargin - top);
                            goal = Mathf.Clamp(need, 0f, Mathf.Max(0f, roomAtTop));
                        }
                    }
                }
            }

            // every part goes where it should: the active one to `goal`, the others back home
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Part p = parts[i];
                if (p.rect == null) { parts.RemoveAt(i); continue; }
                float to = p == active ? goal : 0f;
                p.now = Mathf.Abs(to - p.now) < 0.5f ? to : Mathf.Lerp(p.now, to, t);
                Apply(p, p.now);
                if (p != active && p.now == 0f) parts.RemoveAt(i);
            }

            // idle again: no field in use, everything is back home
            if (current == null && parts.Count == 0) enabled = false;
        }

        /// <summary>The text field that was just selected, or null. Reads no text.</summary>
        static TMP_InputField Selected()
        {
            var events = UnityEngine.EventSystems.EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            TMP_InputField field = selected != null ? selected.GetComponentInParent<TMP_InputField>() : null;
            return field != null && !field.readOnly ? field : null;
        }

        Part PartFor(ScrollRect scroll)
        {
            foreach (Part p in parts) if (p.scroll == scroll) return p;
            var layout = scroll.content.GetComponent<LayoutGroup>();
            var made = new Part { scroll = scroll, group = layout, now = 0f };
            if (layout != null) { made.rect = scroll.content; made.originalBottom = layout.padding.bottom; }
            else { made.rect = scroll.viewport; made.originalValue = scroll.viewport.offsetMin; }
            parts.Add(made);
            return made;
        }

        Part PartFor(RectTransform slide)
        {
            foreach (Part p in parts) if (p.scroll == null && p.rect == slide) return p;
            var made = new Part { rect = slide, originalValue = slide.anchoredPosition, now = 0f };
            parts.Add(made);
            return made;
        }

        /// <summary>Puts `amount` canvas units on a part: padding, a shrunk viewport, or a slide.</summary>
        void Apply(Part p, float amount)
        {
            if (p.rect == null) return;
            Canvas canvas = p.rect.GetComponentInParent<Canvas>();
            float canvasScale = canvas != null ? canvas.rootCanvas.transform.lossyScale.y : 1f;
            if (p.scroll == null)
            {
                p.rect.anchoredPosition = p.originalValue + new Vector2(0f, amount);       // a slid part (canvas units = its parent's units)
                return;
            }
            if (p.group != null)
            {
                float scale = p.rect.lossyScale.y > 0f ? canvasScale / p.rect.lossyScale.y : 1f;
                int bottom = p.originalBottom + Mathf.RoundToInt(amount * scale);
                if (p.group.padding.bottom == bottom) return;
                p.group.padding.bottom = bottom;
                LayoutRebuilder.MarkLayoutForRebuild(p.scroll.content);
            }
            else
            {
                Transform parent = p.rect.parent;
                float scale = parent != null && parent.lossyScale.y > 0f ? canvasScale / parent.lossyScale.y : 1f;
                p.rect.offsetMin = p.originalValue + new Vector2(0f, amount * scale);
            }
        }

        /// <summary>The part of the screen that slides: the Inspector's choice when it holds the field, else the canvas child that holds it.</summary>
        RectTransform SlideTargetOf(TMP_InputField field, RectTransform canvasRect)
        {
            if (shiftTarget != null && field.transform.IsChildOf(shiftTarget)) return shiftTarget;
            Transform t = field.transform;
            while (t.parent != null && t.parent != canvasRect) t = t.parent;
            return t as RectTransform;
        }

        /// <summary>The lowest and highest point (canvas space) of the field and of everything that must be seen with it.</summary>
        void Bounds(TMP_InputField field, RectTransform canvasRect, out float bottom, out float top)
        {
            Edges((RectTransform)field.transform, canvasRect, out bottom, out top);
            KeyboardKeepVisible extras = field.GetComponent<KeyboardKeepVisible>();
            bool any = false;
            if (extras != null && extras.AlsoShow != null)
                foreach (RectTransform part in extras.AlsoShow)
                {
                    if (part == null || !part.gameObject.activeInHierarchy) continue;
                    Edges(part, canvasRect, out float b, out float tp);
                    bottom = Mathf.Min(bottom, b); top = Mathf.Max(top, tp);
                    any = true;
                }
            bottom -= any ? extras.ExtraBelow : spaceBelowField;
        }

        void Edges(RectTransform rect, RectTransform canvasRect, out float bottom, out float top)
        {
            rect.GetWorldCorners(corners);
            bottom = float.MaxValue; top = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float y = canvasRect.InverseTransformPoint(corners[i]).y;
                bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
            }
        }

        /// <summary>Moves a scroll view's content by `delta` canvas units (up = positive), inside its scrolling range.</summary>
        static void ScrollBy(ScrollRect scroll, RectTransform canvasRect, float delta)
        {
            RectTransform content = scroll.content, viewport = scroll.viewport;
            float scale = viewport.lossyScale.y > 0f ? canvasRect.lossyScale.y / viewport.lossyScale.y : 1f;
            Vector2 position = content.anchoredPosition;
            position.y += delta * scale;
            float range = Mathf.Max(0f, content.rect.height - viewport.rect.height);
            if (Mathf.Approximately(content.pivot.y, 1f)) position.y = Mathf.Clamp(position.y, 0f, range);          // content hangs from the top
            else if (Mathf.Approximately(content.pivot.y, 0f)) position.y = Mathf.Clamp(position.y, -range, 0f);    // content stands on the bottom
            scroll.StopMovement();
            content.anchoredPosition = position;
        }
    }
}
