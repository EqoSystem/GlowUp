using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.Common
{
    /// <summary>
    /// TAP TO PLACE THE CARET in every text field of the app (e-mail, password, names, lists, support message...).
    ///
    /// Why it is needed: on Android the app types through the phone keyboard's own hidden text box (the field on screen only
    /// shows a copy). The field moved its caret when it was tapped, but the hidden box kept ITS caret at the end, so the next
    /// letter, or the next read of the box, put the caret back at the end. On top of that, the first tap selected the whole
    /// entry ("select all on focus"), so a typo meant deleting everything.
    ///
    /// What this does (it changes nothing else about the field):
    ///   1. A tap puts the caret where the finger was: also the FIRST tap, which no longer selects everything.
    ///   2. A long press (about half a second, without moving) selects the word under the finger (a password: all of it).
    ///   3. The caret or selection is handed to the keyboard's hidden box, so the next letter lands where the caret is.
    ///   4. When the keyboard moves ITS caret (Gboard and Samsung: swiping along the space bar), the field follows.
    /// The phone's own selection handles and copy menu are not shown: Unity draws the field itself and Android cannot.
    /// Added to every field by the Keyboard Avoider (KeyboardAvoider.Register), so no scene needs rebuilding.
    /// Nothing the user types is logged.
    /// </summary>
    [DefaultExecutionOrder(1000)]                     // its LateUpdate runs after the field's own (which reads the keyboard)
    [RequireComponent(typeof(TMP_InputField))]
    public class InputCaretTouch : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        const float LongPressSeconds = 0.5f;
        const float HoldSeconds = 0.3f;              // how long a placed caret is re-handed to the keyboard if it is read back late

        TMP_InputField field;

        // the press in progress
        bool pressing, longPressDone;
        Vector2 pressAt;
        float pressTime;
        Camera pressCamera;

        // a caret or selection waiting to be placed (after the field has become active), then held for a moment
        bool pending;
        float pendingUntil;
        Vector2 placeAt;
        Camera placeCamera;
        int wantAnchor = -1, wantFocus = -1;
        float holdUntil;
        string heldText;

        RangeInt lastKeyboardSelection = new RangeInt(-1, 0);

        void Awake()
        {
            field = GetComponent<TMP_InputField>();
            field.onFocusSelectAll = false;          // the first tap places the caret instead of selecting everything
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Usable() || eventData.button != PointerEventData.InputButton.Left) return;
            pressing = true;
            longPressDone = false;
            pressAt = eventData.position;
            pressTime = Time.unscaledTime;
            pressCamera = eventData.pressEventCamera;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressing) return;
            pressing = false;
            if (longPressDone || Moved(eventData.position)) return;   // a long press already selected; a drag selects by itself
            pending = true;
            pendingUntil = Time.unscaledTime + 0.5f;          // the field becomes active a frame or two after the first tap
            placeAt = eventData.position;
            placeCamera = eventData.pressEventCamera;
        }

        void Update()
        {
            if (!pressing || longPressDone) return;
            if (Time.unscaledTime - pressTime < LongPressSeconds) return;
            Vector2 now = Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
            if (Moved(now)) { pressing = false; return; }
            longPressDone = true;
            SelectWordAt(pressAt, pressCamera);
        }

        void LateUpdate()
        {
            if (field == null) return;
            if (!field.isFocused)
            {
                if (pending && Time.unscaledTime > pendingUntil) pending = false;
                return;
            }

            if (pending)
            {
                pending = false;
                int at = IndexAt(placeAt, placeCamera);
                if (at >= 0) Want(at, at);
            }

            TouchScreenKeyboard keyboard = Keyboard();

            // 3. hold the wanted caret for a moment: the hidden box may report its old caret for a frame or two
            if (Time.unscaledTime < holdUntil && field.text == heldText)
            {
                if (field.selectionAnchorPosition != wantAnchor || field.selectionFocusPosition != wantFocus)
                {
                    field.selectionAnchorPosition = wantAnchor;
                    field.selectionFocusPosition = wantFocus;
                    field.ForceLabelUpdate();
                }
                HandToKeyboard(keyboard, wantAnchor, wantFocus);
                return;
            }
            holdUntil = 0f;

            // 4. the keyboard moved its own caret (space-bar swipe): the field follows
            if (keyboard == null || !keyboard.active || !keyboard.canGetSelection) return;
            RangeInt now = keyboard.selection;
            if (now.start == lastKeyboardSelection.start && now.length == lastKeyboardSelection.length) return;
            lastKeyboardSelection = now;
            if (keyboard.text != field.text) return;                   // typing in progress: the field reads it itself
            int length = field.text.Length;
            int a = Mathf.Clamp(now.start, 0, length), f = Mathf.Clamp(now.start + now.length, 0, length);
            if (field.selectionAnchorPosition == a && field.selectionFocusPosition == f) return;
            field.selectionAnchorPosition = a;
            field.selectionFocusPosition = f;
            field.ForceLabelUpdate();
        }

        void SelectWordAt(Vector2 screen, Camera cam)
        {
            if (!field.isFocused) { field.ActivateInputField(); pending = false; }
            string text = field.text ?? "";
            if (text.Length == 0) return;
            if (field.inputType == TMP_InputField.InputType.Password) { Want(0, text.Length); return; }
            int at = Mathf.Clamp(IndexAt(screen, cam), 0, text.Length);
            int start = at, end = at;
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            while (end < text.Length && IsWordChar(text[end])) end++;
            if (start == end) { start = Mathf.Max(0, at - 1); end = Mathf.Min(text.Length, start + 1); }
            Want(start, end);
        }

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '\'';

        void Want(int anchor, int focus)
        {
            wantAnchor = anchor; wantFocus = focus;
            heldText = field.text;
            holdUntil = Time.unscaledTime + HoldSeconds;
            if (field.isFocused)
            {
                field.selectionAnchorPosition = anchor;
                field.selectionFocusPosition = focus;
                field.ForceLabelUpdate();
                HandToKeyboard(Keyboard(), anchor, focus);
            }
        }

        void HandToKeyboard(TouchScreenKeyboard keyboard, int anchor, int focus)
        {
            if (keyboard == null || !keyboard.active || !keyboard.canSetSelection) return;
            int start = Mathf.Min(anchor, focus), length = Mathf.Abs(focus - anchor);
            RangeInt now = keyboard.selection;
            if (now.start == start && now.length == length) return;
            keyboard.selection = new RangeInt(start, length);
            lastKeyboardSelection = new RangeInt(start, length);
        }

        /// <summary>The caret index (0..text length) under a screen point, or -1 when the field has no text component.</summary>
        int IndexAt(Vector2 screen, Camera cam)
        {
            TMP_Text shown = field.textComponent;
            if (shown == null) return -1;
            int length = (field.text ?? "").Length;
            if (length == 0 || shown.textInfo == null || shown.textInfo.characterCount == 0) return length;
            int index = TMP_TextUtilities.GetCursorIndexFromPosition(shown, screen, cam, out CaretPosition side);
            if (index < 0) return length;
            if (side == CaretPosition.Right) index++;
            return Mathf.Clamp(index, 0, length);
        }

        bool Moved(Vector2 now)
        {
            float limit = (Screen.dpi > 0f ? Screen.dpi : 160f) * 0.08f;   // about 2 mm
            return (now - pressAt).sqrMagnitude > limit * limit;
        }

        // TextMeshPro 3.0.7 keeps the keyboard in a protected field and has no public getter: it is read once by name.
        static readonly System.Reflection.FieldInfo keyboardField =
            typeof(TMP_InputField).GetField("m_SoftKeyboard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        TouchScreenKeyboard Keyboard()
        {
            try { return keyboardField != null ? keyboardField.GetValue(field) as TouchScreenKeyboard : null; }
            catch { return null; }                   // another TextMeshPro version: the caret still moves on screen, step 3 is skipped
        }

        bool Usable() => field != null && field.IsActive() && field.IsInteractable() && !field.readOnly;
    }
}
