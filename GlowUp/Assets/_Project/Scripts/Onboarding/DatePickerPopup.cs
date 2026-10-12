using System;
using System.Collections.Generic;
using System.Globalization;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Calendar-style date picker popup (month + year dropdowns, previous/next month, day grid).
    /// Future dates are disabled. The 42 day cells (Day_01 .. Day_42) are real objects in the
    /// Hierarchy under DaysGrid. Look and range are Inspector fields.
    /// Open with <see cref="Open"/>; it calls back with the chosen date.
    /// </summary>
    public class DatePickerPopup : ThemeAwareBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Dropdown monthDropdown;
        [SerializeField] private TMP_Dropdown yearDropdown;
        [SerializeField] private Button previousMonthButton;
        [SerializeField] private Button nextMonthButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform daysGrid;
        [Tooltip("Optional. Only used to create missing day cells when DaysGrid has fewer than 42.")]
        [SerializeField] private Button dayButtonTemplate;

        [Header("Range")]
        [SerializeField] private int yearsBack = 100;
        [Tooltip("The calendar opens this many years ago when no date is chosen yet")]
        [SerializeField] private int defaultYearsAgo = 20;

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color dayColor = new Color32(247, 243, 235, 255);
        [SerializeField] private Color selectedDayColor = new Color32(217, 142, 160, 255);
        [SerializeField] private Color dayTextColor = new Color32(74, 65, 64, 255);
        [SerializeField] private Color selectedDayTextColor = new Color32(58, 46, 45, 255);
        [SerializeField] private Color disabledTextColor = new Color(0.29f, 0.25f, 0.25f, 0.3f);

        readonly List<Button> cells = new List<Button>();
        DateTime viewMonth, selected;
        bool hasSelection;
        Action<DateTime> onPicked;
        bool built, suppress;

        void Awake()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (previousMonthButton != null) previousMonthButton.onClick.AddListener(() => Shift(-1));
            if (nextMonthButton != null) nextMonthButton.onClick.AddListener(() => Shift(1));
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            dayColor = p.inputBackground;
            selectedDayColor = p.accent;
            dayTextColor = p.mainText;
            selectedDayTextColor = p.buttonText;
            disabledTextColor = new Color(p.mainText.r, p.mainText.g, p.mainText.b, 0.3f);
            if (built && panelRoot != null && panelRoot.activeSelf) Refresh();
            else PaintIdleCells();
        }

        /// <summary>Editor preview / closed state: every cell in its plain colour.</summary>
        void PaintIdleCells()
        {
            if (daysGrid == null) return;
            foreach (Transform child in daysGrid)
            {
                var img = child.GetComponent<Image>(); if (img != null) img.color = dayColor;
                var label = child.GetComponentInChildren<TextMeshProUGUI>(true); if (label != null) label.color = dayTextColor;
            }
        }

        public void Open(DateTime? current, Action<DateTime> callback)
        {
            if (panelRoot == null || daysGrid == null || monthDropdown == null || yearDropdown == null) return;
            onPicked = callback;
            BuildOnce();
            hasSelection = current.HasValue;
            selected = current ?? DateTime.Today.AddYears(-defaultYearsAgo);
            viewMonth = new DateTime(selected.Year, selected.Month, 1);
            transform.SetAsLastSibling();             // on top of everything else in the panel
            panelRoot.SetActive(true);
            Refresh();
        }

        public void Close() { if (panelRoot != null) panelRoot.SetActive(false); }

        /// <summary>True while the calendar is on the screen.</summary>
        public bool IsOpen => panelRoot != null && panelRoot.activeInHierarchy;

        void BuildOnce()
        {
            if (built) return;
            built = true;

            monthDropdown.ClearOptions();
            var months = new List<string>();
            for (int m = 1; m <= 12; m++) months.Add(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m));
            monthDropdown.AddOptions(months);
            monthDropdown.onValueChanged.AddListener(_ => OnDropdownChanged());

            yearDropdown.ClearOptions();
            var years = new List<string>();
            for (int y = DateTime.Today.Year; y >= DateTime.Today.Year - yearsBack; y--) years.Add(y.ToString());
            yearDropdown.AddOptions(years);
            yearDropdown.onValueChanged.AddListener(_ => OnDropdownChanged());

            // the day cells already exist in the Hierarchy; only create what is missing
            cells.Clear();
            foreach (Transform child in daysGrid)
            {
                var b = child.GetComponent<Button>();
                if (b != null && b != dayButtonTemplate) cells.Add(b);
            }
            while (cells.Count < 42)
            {
                Button source = dayButtonTemplate != null ? dayButtonTemplate : (cells.Count > 0 ? cells[0] : null);
                if (source == null) break;
                var b = Instantiate(source, daysGrid);
                b.gameObject.name = "Day_" + (cells.Count + 1).ToString("00");
                b.gameObject.SetActive(true);
                cells.Add(b);
            }
            if (dayButtonTemplate != null) dayButtonTemplate.gameObject.SetActive(false);
        }

        void OnDropdownChanged()
        {
            if (suppress) return;
            int year = DateTime.Today.Year - yearDropdown.value;
            viewMonth = new DateTime(year, monthDropdown.value + 1, 1);
            KeepDayInNewMonth();
            Refresh();
        }

        void Shift(int delta)
        {
            var next = viewMonth.AddMonths(delta);
            if (next > new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)) return;
            if (next.Year < DateTime.Today.Year - yearsBack) return;
            viewMonth = next;
            KeepDayInNewMonth();
            Refresh();
        }

        /// <summary>
        /// A birthday is chosen already and the user changes the month or the year: the same day of that month becomes the
        /// birthday at once (the last day when the month is shorter), so the field and the age follow without the day
        /// having to be tapped again. The calendar stays open. Nothing happens before a first day was chosen.
        /// </summary>
        void KeepDayInNewMonth()
        {
            if (!hasSelection) return;
            if (viewMonth > new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)) return;      // a month that has not begun: nothing to choose there
            DateTime moved = DayIn(viewMonth, selected.Day, DateTime.Today);
            if (moved == selected.Date) return;
            selected = moved;
            onPicked?.Invoke(selected);
        }

        /// <summary>The given day in the month of `month`: the last day when the month is shorter, and never later than `latest`.</summary>
        public static DateTime DayIn(DateTime month, int day, DateTime latest)
        {
            int last = DateTime.DaysInMonth(month.Year, month.Month);
            var date = new DateTime(month.Year, month.Month, Mathf.Clamp(day, 1, last));
            return date > latest.Date ? latest.Date : date;
        }

        void Refresh()
        {
            suppress = true;
            monthDropdown.SetValueWithoutNotify(viewMonth.Month - 1);
            yearDropdown.SetValueWithoutNotify(Mathf.Clamp(DateTime.Today.Year - viewMonth.Year, 0, yearsBack));
            suppress = false;

            int offset = (int)viewMonth.DayOfWeek;                 // Sunday-first grid
            int days = DateTime.DaysInMonth(viewMonth.Year, viewMonth.Month);
            for (int i = 0; i < cells.Count; i++)
            {
                int day = i - offset + 1;
                var b = cells[i];
                var label = b.GetComponentInChildren<TextMeshProUGUI>(true);
                var img = b.GetComponent<Image>();
                b.onClick.RemoveAllListeners();
                if (day < 1 || day > days)
                {
                    if (label != null) label.text = "";
                    if (img != null) img.color = Color.clear;
                    b.interactable = false;
                    continue;
                }

                var date = new DateTime(viewMonth.Year, viewMonth.Month, day);
                bool future = date > DateTime.Today;
                bool isSelected = hasSelection && date == selected.Date;
                if (label != null)
                {
                    label.text = day.ToString();
                    label.color = future ? disabledTextColor : (isSelected ? selectedDayTextColor : dayTextColor);
                }
                if (img != null) img.color = isSelected ? selectedDayColor : dayColor;
                b.interactable = !future;
                int captured = day;
                b.onClick.AddListener(() => Pick(captured));
            }
        }

        void Pick(int day)
        {
            selected = new DateTime(viewMonth.Year, viewMonth.Month, day);
            hasSelection = true;
            onPicked?.Invoke(selected);
            Close();
        }
    }
}
