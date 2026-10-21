using System.Text;
using GlowUp.Data;
using GlowUp.Scan;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// The FDA Philippines line of a result: a round icon and two short lines that say what the FDA's public list of
    /// notified cosmetics answered for this product. Five answers are possible (worked out by PhFdaVerification.StatusOf):
    ///
    ///   registered   - the product is on the list and its record is still valid;
    ///   similar      - only a record with a similar name was found: the user is shown it and judges;
    ///   expired      - the product is on the list but its record has ended;
    ///   none         - the list was searched and has no record for this name;
    ///   unavailable  - the check could not be done (no internet, or the FDA's site did not answer).
    /// While a search is still running (the FDA's site takes 12 to 15 seconds) the badge says "checking".
    ///
    /// The wording never says "genuine", "legitimate" or "fake": a record shows that a product is registered for sale.
    /// It does not show that the item in the user's hand is real or safe, and the list holds no ingredient lists.
    /// That is said in the text that opens under the badge (the ExpandableSection on the same object).
    /// </summary>
    public class FdaBadgeView : ThemeAwareBehaviour
    {
        [Header("References")]
        [SerializeField] private Image icon;
        [Tooltip("The round background behind the icon")]
        [SerializeField] private Image iconBackground;
        [SerializeField] private TextMeshProUGUI titleLabel;
        [SerializeField] private TextMeshProUGUI detailLabel;
        [Tooltip("The longer text in the block that opens under the badge")]
        [SerializeField] private TextMeshProUGUI moreText;
        [Tooltip("Opens the FDA Philippines site, where the same search can be done by hand")]
        [SerializeField] private Button portalButton;
        [SerializeField] private string portalUrl = PhFdaVerification.Portal;

        [Header("A similar record: is it the user's product?")]
        [Tooltip("The question with its two buttons, in the block that opens under the badge. Shown for a similar record only.")]
        [SerializeField] private GameObject confirmRow;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;

        [Header("Wording: the two lines of the badge")]
        [SerializeField] private string registeredTitle = "Registered with FDA Philippines";
        [Tooltip("{0} = the date the record is valid until")]
        [SerializeField] private string registeredDetail = "Valid until {0}";
        [SerializeField] private string registeredDetailNoDate = "On the FDA's list of notified cosmetics";
        [SerializeField] private string similarTitle = "A similar FDA Philippines record";
        [Tooltip("{0} = the name on the FDA's record")]
        [SerializeField] private string similarDetail = "\"{0}\". Check that it is your product.";
        [SerializeField] private string similarDetailNoName = "Check that it is your product.";
        [SerializeField] private string expiredTitle = "FDA Philippines registration expired";
        [Tooltip("{0} = the date the record ended")]
        [SerializeField] private string expiredDetail = "It ended {0}";
        [SerializeField] private string noneTitle = "No FDA Philippines record found";
        [SerializeField] private string noneDetail = "Searched by this product's name and brand";
        [SerializeField] private string unavailableTitle = "FDA check unavailable";
        [SerializeField] private string unavailableDetail = "It needs the internet. Scan again when you are online.";
        [SerializeField] private string checkingTitle = "Checking FDA Philippines...";
        [SerializeField] private string checkingDetail = "The FDA's site takes about 15 seconds to answer.";

        [Header("Wording: the text that opens under the badge")]
        [Tooltip("{0} = the name on the FDA's record")]
        [SerializeField] private string recordNameFormat = "FDA record: {0}";
        [Tooltip("{0} = the company that registered it")]
        [SerializeField] private string companyFormat = "Registered by: {0}";
        [Tooltip("{0} = the notification number")]
        [SerializeField] private string numberFormat = "Notification no.: {0}";
        [TextArea] [SerializeField] private string noneMore = "The FDA Philippines list was searched with this product's name and brand and gave no record. The product may be listed under another name.";
        [TextArea] [SerializeField] private string unavailableMore = "The check needs the internet and an answer from the FDA's site. That was not possible this time.";
        [TextArea] [SerializeField] private string checkingMore = "The FDA Philippines list is being searched for this product. The answer shows here by itself.";
        [TextArea] [SerializeField] private string aboutText = "The FDA Philippines list shows which cosmetics are registered (notified) for sale. It holds no ingredient lists, and a record does not prove that the item in your hand is genuine or safe.";

        [Header("Icons")]
        [SerializeField] private Sprite registeredIcon;
        [SerializeField] private Sprite similarIcon;
        [SerializeField] private Sprite expiredIcon;
        [SerializeField] private Sprite noneIcon;
        [SerializeField] private Sprite unavailableIcon;
        [SerializeField] private Sprite checkingIcon;

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color registeredColor = new Color32(94, 127, 75, 255);
        [SerializeField] private Color cautionColor = new Color32(169, 116, 28, 255);
        [SerializeField] private Color expiredColor = new Color32(181, 71, 47, 255);
        [SerializeField] private Color unavailableColor = new Color32(110, 98, 92, 255);

        [Tooltip("Which answer is shown in the Editor and before a product is loaded: registered, similar, expired, none or unavailable")]
        [SerializeField] private string state = PhFdaVerification.Registered;

        /// <summary>Raised when the user answered "is this record your product?" for a similar record: true = yes.</summary>
        public event System.Action<bool> SimilarAnswered;

        void Awake()
        {
            if (portalButton != null) portalButton.onClick.AddListener(() => { if (!string.IsNullOrWhiteSpace(portalUrl)) Application.OpenURL(portalUrl); });
            if (confirmYesButton != null) confirmYesButton.onClick.AddListener(() => SimilarAnswered?.Invoke(true));
            if (confirmNoButton != null) confirmNoButton.onClick.AddListener(() => SimilarAnswered?.Invoke(false));
            if (confirmRow != null) confirmRow.SetActive(state == PhFdaVerification.Similar);
            Paint();
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            registeredColor = p.success; cautionColor = p.caution; expiredColor = p.warning; unavailableColor = p.secondaryText;
            Paint();
        }

        const string Checking = "checking";

        /// <summary>Shows what the FDA Philippines check said about this scan. `stillChecking`: the search has not ended yet.</summary>
        public void Bind(ScanRecord record, bool stillChecking = false)
        {
            state = stillChecking ? Checking : PhFdaVerification.StatusOf(record);
            string until = PhFdaVerification.NiceDate(PhFdaVerification.ValidUntilOf(record));
            string name = PhFdaVerification.RecordNameOf(record), company = PhFdaVerification.CompanyOf(record);
            string number = record != null ? (record.fdaNumber ?? "").Trim() : "";

            string title, detail;
            switch (state)
            {
                case PhFdaVerification.Registered: title = registeredTitle; detail = until.Length > 0 ? string.Format(registeredDetail, until) : registeredDetailNoDate; break;
                case PhFdaVerification.Expired: title = expiredTitle; detail = until.Length > 0 ? string.Format(expiredDetail, until) : ""; break;
                case PhFdaVerification.Similar: title = similarTitle; detail = name.Length > 0 ? string.Format(similarDetail, name) : similarDetailNoName; break;
                case PhFdaVerification.None: title = noneTitle; detail = noneDetail; break;
                case Checking: title = checkingTitle; detail = checkingDetail; break;
                default: title = unavailableTitle; detail = unavailableDetail; break;
            }
            if (titleLabel != null) titleLabel.text = title;
            // the second line always keeps its place (an empty one holds a no-break space), so the screen does not jump
            // when the FDA's answer arrives a few seconds after the screen opened
            if (detailLabel != null) { detailLabel.text = detail.Length > 0 ? detail : "\u00A0"; detailLabel.gameObject.SetActive(true); }
            if (confirmRow != null) confirmRow.SetActive(state == PhFdaVerification.Similar && record != null);

            if (moreText != null)
            {
                var more = new StringBuilder();
                bool hasRecord = state == PhFdaVerification.Registered || state == PhFdaVerification.Expired || state == PhFdaVerification.Similar;
                if (hasRecord)
                {
                    if (name.Length > 0) more.Append(string.Format(recordNameFormat, name)).Append('\n');
                    if (company.Length > 0) more.Append(string.Format(companyFormat, company)).Append('\n');
                    if (number.Length > 0) more.Append(string.Format(numberFormat, number)).Append('\n');
                }
                else more.Append(state == PhFdaVerification.None ? noneMore : state == Checking ? checkingMore : unavailableMore).Append('\n');
                if (more.Length > 0) more.Append('\n');
                more.Append(aboutText);
                moreText.text = more.ToString().Trim();
            }
            Paint();
        }

        void Paint()
        {
            Color c = state == PhFdaVerification.Registered ? registeredColor : state == PhFdaVerification.Expired ? expiredColor
                    : state == PhFdaVerification.Unavailable || state == Checking ? unavailableColor : cautionColor;
            Sprite s = state == PhFdaVerification.Registered ? registeredIcon : state == PhFdaVerification.Expired ? expiredIcon
                     : state == PhFdaVerification.Similar ? similarIcon : state == PhFdaVerification.None ? noneIcon
                     : state == Checking && checkingIcon != null ? checkingIcon : unavailableIcon;
            if (iconBackground != null) { iconBackground.color = c; if (icon != null) icon.color = Color.white; }
            else if (icon != null) icon.color = c;
            if (icon != null && s != null) icon.sprite = s;
        }
    }
}
