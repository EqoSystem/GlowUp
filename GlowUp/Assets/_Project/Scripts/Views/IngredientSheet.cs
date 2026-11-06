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
    /// ingredient of a product (flagged ones first) with its classification, purpose and the reason it
    /// matters for this user, the rest behind a fold in each row, and ends with the advice to see a dermatologist.
    /// The "Key ingredients" chips open this same list with the tapped ingredient first, so both show the same card.
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
                    IngredientFinding shown = ForDisplay(finding, out IngredientRowView.Details details);
                    if (maybe.Contains(finding.name ?? "") && !string.IsNullOrWhiteSpace(mayContainNote))
                        shown.description = (mayContainNote + " " + (shown.description ?? "")).Trim();
                    row.Bind(shown, details);
                    rows.Add(row.gameObject);
                }

            int flagged = ordered.Count(f => f.level == "harmful" || f.level == "caution");
            if (flagged < 0) flagged = 0;
            if (titleText != null) titleText.text = string.Format(titleFormat, record.Title);
            if (subtitleText != null) subtitleText.text = string.Format(subtitleFormat, ordered.Count, flagged);
            if (scroll != null) { Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1f; }
        }

        /// <summary>
        /// The finding as it is shown, and what the row shows beside it: an ingredient nothing is known about says why and what to do,
        /// and the place the information comes from is on a line of its own (the saved scan itself is not changed).
        /// </summary>
        IngredientFinding ForDisplay(IngredientFinding f, out IngredientRowView.Details details)
        {
            details = new IngredientRowView.Details();
            var shown = new IngredientFinding
            {
                name = f.name, techName = f.techName, level = f.level, description = f.description ?? "", commonUses = f.commonUses,
                reason = f.reason, keyActive = f.keyActive, hidden = f.hidden, aiSourced = f.aiSourced
            };
            GlowUp.Data.IngredientData entry = GlowUp.Data.IngredientRepository.Find(string.IsNullOrEmpty(f.techName) ? f.name : f.techName) ?? GlowUp.Data.IngredientRepository.Find(f.name);
            if (entry != null) FillDetails(details, entry, f);
            CardName(f, entry, shown, details);
            if (f.level == "unknown" && string.IsNullOrWhiteSpace(shown.description))
            {
                shown.description = GlowUp.Net.ConnectivityMonitor.Reachable ? unknownOnline : unknownOffline;
                return shown;
            }
            if (string.IsNullOrWhiteSpace(shown.description)) return shown;
            string source = "";
            if (entry != null && entry.sources != null && entry.sources.Count > 0)
            {
                // "Page title | https://address" -> the page title, and the address for the link
                string first = entry.sources[0] ?? "";
                int cut = first.LastIndexOf(" | ", System.StringComparison.Ordinal);
                source = (cut > 0 ? first.Substring(0, cut) : first).Trim();
                string address = (cut > 0 ? first.Substring(cut + 3) : first).Trim();
                // a page is only offered as a LINK for an entry an admin typed, copied from a regulator's list or wrote and saved themself. Any
                // entry the app's own AI look-up wrote (source "ai"), checked or approved by itself or not, can name any address, so it keeps the
                // page's title as text and no button.
                if (!string.Equals(entry.source, "ai", System.StringComparison.OrdinalIgnoreCase) && address.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
                {
                    details.sourceUrl = address;
                    if (source == address) source = "";
                }
            }
            if (source.Length == 0 && entry != null) source = f.aiSourced ? sourceAi : sourceCurator;
            else if (source.Length > 0 && f.aiSourced) source += " (" + sourceAi + ")";
            if (source.Length > 0) details.source = string.Format(sourceFormat, source) + ".";
            return shown;
        }

        /// <summary>What the website's entry says: the common name is its first alias. Empty fields stay empty, and the row then hides them.</summary>
        static void FillDetails(IngredientRowView.Details d, GlowUp.Data.IngredientData entry, IngredientFinding f)
        {
            d.classification = GlowUp.Data.ClassificationRepository.NameOf(entry.classificationId);
            d.purpose = entry.purpose;
            d.chemical = entry.chemicalDescription;
            if (!string.IsNullOrWhiteSpace(entry.cautionNote)) d.caution = "It " + entry.cautionNote.Trim().TrimEnd('.') + ".";
            if (entry.allergenTags != null && entry.allergenTags.Count > 0) d.allergen = string.Join(", ", entry.allergenTags.Select(GlowUp.Data.ListsRepository.AllergenLabel));
            if (entry.keyActive) d.highlights.Add("Key active");
            if (entry.umbrella) d.highlights.Add("Umbrella term");
            if (entry.highlights != null) d.highlights.AddRange(entry.highlights.Select(GlowUp.Data.ListsRepository.HighlightLabel));
        }

        static readonly string[] NameCut = { " / ", "/ ", " /" };

        static List<string> NameParts(string value) => string.IsNullOrWhiteSpace(value) ? new List<string>()
            : value.Split(NameCut, System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        /// <summary>
        /// The card's title is the INCI name, with the first other name in brackets. Many entries hold two names in one ("Dimethicone /
        /// Silicone Oil", "Solvent / Isododecane"): the INCI name is the first part of techName, the other parts become aliases, and a part
        /// that is a classification's name ("Solvent") becomes the badge when the entry has no classification of its own. Without a
        /// techName nothing is split, because which part is the INCI name is then unknown. Only what is shown changes, never the saved scan.
        /// </summary>
        static void CardName(IngredientFinding f, GlowUp.Data.IngredientData entry, IngredientFinding shown, IngredientRowView.Details d)
        {
            List<string> tech = NameParts(f.techName);
            string inci = tech.Count > 0 ? tech[0] : (f.name ?? "").Trim();
            var candidates = new List<string>();
            candidates.AddRange(tech.Skip(1));                                   // "Butylene Glycol / 1,3-Dihydroxybutane": the techName's own other name first
            if (tech.Count > 0) candidates.AddRange(NameParts(f.name));
            if (entry != null && entry.aliases != null) candidates.AddRange(entry.aliases);

            var aliases = new List<string>();
            foreach (string raw in candidates)
            {
                string c = (raw ?? "").Trim();
                if (c.Length == 0 || string.Equals(c, inci, System.StringComparison.OrdinalIgnoreCase)) continue;
                string word = GlowUp.Data.ClassificationRepository.Matching(c);
                if (word.Length > 0)                                             // "Solvent" is a kind of ingredient, not a name for it
                {
                    if (string.IsNullOrWhiteSpace(d.classification)) d.classification = word;
                    continue;
                }
                // a cut-off piece of a name already listed ("3-Dihydroxybutane" beside "1,3-Dihydroxybutane") is left out, and so is a repeat
                if (aliases.Any(a => a.IndexOf(c, System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                aliases.RemoveAll(a => c.IndexOf(a, System.StringComparison.OrdinalIgnoreCase) >= 0);
                aliases.Add(c);
            }
            shown.name = inci;
            shown.techName = inci;                                               // so the row does not put the same name in brackets
            d.commonName = aliases.Count > 0 ? aliases[0] : "";
            d.aliases = aliases.Count > 0 ? string.Join(", ", aliases) : "";
        }
    }
}
