using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Put on a TMP_InputField (e.g. BirthdayInputField). The field becomes read-only: tapping it
    /// opens the calendar popup, and the chosen date is written into the field and raised through
    /// <see cref="DateChanged"/>.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public class DatePickerField : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private DatePickerPopup popup;
        [Tooltip("Display format, e.g. MMMM d, yyyy  or  dd/MM/yyyy")]
        [SerializeField] private string displayFormat = "MMMM d, yyyy";
        [Tooltip("When off, tapping does nothing")]
        [SerializeField] private bool pickerEnabled = true;

        public event Action<DateTime> DateChanged;
        public DateTime? Value { get; private set; }

        TMP_InputField field;

        /// <summary>Works even before this object has been activated for the first time.</summary>
        TMP_InputField Field
        {
            get
            {
                if (field == null)
                {
                    field = GetComponent<TMP_InputField>();
                    if (field != null) { field.readOnly = true; field.shouldHideMobileInput = true; }   // only the calendar can change it
                }
                return field;
            }
        }

        void Awake() { _ = Field; }

        public void SetPickerEnabled(bool enabled) { pickerEnabled = enabled; }

        public void SetValue(DateTime? date, bool notify = false)
        {
            Value = date;
            var f = Field;
            if (f != null) f.SetTextWithoutNotify(date.HasValue ? date.Value.ToString(displayFormat, CultureInfo.CurrentCulture) : "");
            if (notify && date.HasValue) DateChanged?.Invoke(date.Value);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (popup == null || !pickerEnabled) return;
            popup.Open(Value, d => SetValue(d, true));
        }
    }
}
