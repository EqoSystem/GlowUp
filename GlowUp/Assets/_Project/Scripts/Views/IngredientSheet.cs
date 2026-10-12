using System.Collections.Generic;
using System.Linq;
using GlowUp.Common;
using GlowUp.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// The ingredient list pop-up used by the Recent Scans and Result scenes. It lists every
    /// ingredient of a product (flagged ones first) with its description, uses and the reason it
    /// matters for this user, and ends with the advice to see a dermatologist.
    /// IngredientRowTemplate is the pattern row: the app makes one copy per ingredient.
    /// </summary>
    public class IngredientSheet : MonoBehaviour
    {
        [SerializeField] private OverlayPanel panel;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private RectTransform listContent;
        [SerializeField] private IngredientRowView rowTemplate;

        [Header("See a specialist")]
        [SerializeField] private Button dermatologistButton;
        [Tooltip("Opened by the button. A map search works everywhere; replace it with your own directory if you have one.")]
        [SerializeField] private string dermatologistUrl = "https://www.google.com/maps/search/dermatologist+near+me";

        [Header("Wording")]
        [Tooltip("{0} = product name")]
        [SerializeField] private string titleFormat = "Ingredients";
        [Tooltip("{0} = number of ingredients, {1} = number flagged")]
        [SerializeField] private string subtitleFormat = "{0} ingredients, {1} to look at";

        [Header("An ingredient nothing is known about")]
        [Tooltip("Shown when the phone is offline: the look-up needs the internet")]
        [TextArea] [SerializeField] private string unknownOffline = "No information about this ingredient is saved on this phone. Connect to the internet and scan the product again: the app then looks it up.";
        [Tooltip("Shown when the phone is online and the look-up still found nothing it can trust")]
        [TextArea] [SerializeField] private string unknownOnline = "The app looked this ingredient up and found no source it can trust yet, so it gives no verdict on it. Check the label, or ask a dermatologist.";
        [Tooltip("Put in front of the place the information comes from. {0} = the source")]
        [SerializeField] private string sourceFormat = "Source: {0}";
        [SerializeField] private string sourceCurator = "GlowUp ingredient list (checked by the GlowUp team)";
        [SerializeField] private string sourceAi = "AI look-up, not checked by the GlowUp team yet";
        [Tooltip("Put in front of the description of an ingredient that stood behind \"may contain\" or \"+/-\" on the pack")]
        [SerializeField] private string mayContainNote = "May contain, depending on the shade.";

        readonly List<GameObject> rows = new List<GameObject>();
        bool hooked;

        void Hook()
        {
            if (hooked) return;
            hooked = true;
            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
            if (dermatologistButton != null) dermatologistButton.onClick.AddListener(() => Application.OpenURL(dermatologistUrl));
        }

        /// <summary>Opens the list for a product. `focusIngredient` (optional) is shown first.</summary>
        public void Show(ScanRecord record, string focusIngredient = null)
        {
            if (record == null || panel == null) return;
            panel.Show();                                     // activates the pop-up (and this component with it)
            Hook();

            foreach (var r in rows) if (r != null) Destroy(r);
            rows.Clear();

            int Rank(IngredientFinding f) => f.level == "harmful" ? 0 : f.level == "caution" ? 1 : f.level == "unknown" ? 3 : 2;
            var ordered = (record.findings ?? new List<IngredientFinding>())
                .OrderBy(f => focusIngredient != null && string.Equals(f.name, focusIngredient, System.StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(Rank).ToList();

            // names behind "may contain": the pack lists them for the whole range of shades, this shade may hold only some
            var maybe = new HashSet<string>(record.mayContain ?? new List<string>(), System.StringComparer.OrdinalIgnoreCase);
            if (rowTemplate != null && listContent != null)
                foreach (var finding in ordered)
                {
                    var row = Instantiate(rowTemplate, listContent);
                    row.gameObject.name = "Ingredient_" + finding.name;
                    row.gameObject.SetActive(true);
                    IngredientFinding shown = ForDisplay(finding);
                    if (maybe.Contains(finding.name ?? "") && !string.IsNullOrWhiteSpace(mayContainNote))
                        shown.description = (mayContainNote + " " + (shown.description ?? "")).Trim();
                    row.Bind(shown);
                    rows.Add(row.gameObject);
                }

            int flagged = ordered.Count(f => f.level == "harmful" || f.level == "caution");
            if (flagged < 0) flagged = 0;
            if (titleText != null) titleText.text = string.Format(titleFormat, record.Title);
            if (subtitleText != null) subtitleText.text = string.Format(subtitleFormat, ordered.Count, flagged);
            if (scroll != null) { Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1f; }
        }

        /// <summary>
        /// The line as it is shown: the description always ends with where the information comes from, and an
        /// ingredient nothing is known about says why and what to do (the saved scan itself is not changed).
        /// </summary>
        IngredientFinding ForDisplay(IngredientFinding f)
        {
            var shown = new IngredientFinding
            {
                name = f.name, techName = f.techName, level = f.level, description = f.description ?? "", commonUses = f.commonUses,
                reason = f.reason, keyActive = f.keyActive, hidden = f.hidden, aiSourced = f.aiSourced
            };
            if (f.level == "unknown" && string.IsNullOrWhiteSpace(shown.description))
            {
                shown.description = GlowUp.Net.ConnectivityMonitor.Reachable ? unknownOnline : unknownOffline;
                return shown;
            }
            if (string.IsNullOrWhiteSpace(shown.description)) return shown;
            GlowUp.Data.IngredientData entry = GlowUp.Data.IngredientRepository.Find(string.IsNullOrEmpty(f.techName) ? f.name : f.techName) ?? GlowUp.Data.IngredientRepository.Find(f.name);
            string source = "";
            if (entry != null && entry.sources != null && entry.sources.Count > 0)
            {
                // "Page title | https://address" -> the page title
                string first = entry.sources[0] ?? "";
                int cut = first.LastIndexOf(" | ", System.StringComparison.Ordinal);
                source = (cut > 0 ? first.Substring(0, cut) : first).Trim();
            }
            if (source.Length == 0 && entry != null) source = f.aiSourced ? sourceAi : sourceCurator;
            else if (source.Length > 0 && f.aiSourced) source += " (" + sourceAi + ")";
            if (source.Length > 0) shown.description = shown.description.TrimEnd() + " " + string.Format(sourceFormat, source) + ".";
            return shown;
        }
    }
}
