using System.Collections;
using System.Collections.Generic;
using GlowUp.Scan;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace GlowUp.Result
{
    /// <summary>
    /// WEB SEARCH RESULT (11 November 2026): the cards under the verdict that say what the web search of the user's photo found:
    ///   Brand    - logo, name, market (Global), status (pending review), official website, brand picture;
    ///   Product  - photo, name, cosmetic type, description, minimum age of use, FDA registration (checked by GlowUp), product photo
    ///              and official product page;
    ///   Sources  - primary (the maker's own site, else an unverified site), secondary (an e-commerce retailer), a third page;
    ///   Shades   - the shade printed on the pack: its colour and its code or name.
    /// The safety analysis, the ingredient list, Scan again, AR Try-On and Alternatives are the usual result's (ScanResultFlowController
    /// on the same screen). Everything here is unverified until an admin reviews the entry; the screen says so once.
    /// A value nobody found says "Not found online" in grey; a link nobody found has no button.
    /// </summary>
    public class WebSearchResultView : MonoBehaviour
    {
        [Header("Brand")]
        [SerializeField] private RawImage brandLogo;
        [SerializeField] private GameObject brandLogoPlaceholder;
        [SerializeField] private TextMeshProUGUI brandNameText;
        [SerializeField] private TextMeshProUGUI marketText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button brandWebsiteButton;
        [SerializeField] private TextMeshProUGUI brandWebsiteNote;
        [SerializeField] private Button brandPictureButton;

        [Header("Product")]
        [SerializeField] private RawImage productPhoto;
        [SerializeField] private GameObject productPhotoPlaceholder;
        [SerializeField] private TextMeshProUGUI productNameText;
        [SerializeField] private TextMeshProUGUI cosmeticTypeText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private TextMeshProUGUI minimumAgeText;
        [SerializeField] private TextMeshProUGUI fdaNumberText;
        [SerializeField] private Button productPhotoButton;
        [SerializeField] private Button productPageButton;

        [Header("Sources")]
        [SerializeField] private SourceLinkButton primarySource;
        [SerializeField] private SourceLinkButton secondarySource;
        [SerializeField] private SourceLinkButton thirdSource;
        [SerializeField] private TextMeshProUGUI noSourcesText;

        [Header("Shades")]
        [SerializeField] private GameObject shadesCard;
        [SerializeField] private Image[] shadeSwatches;
        [SerializeField] private TextMeshProUGUI[] shadeLabels;

        [Tooltip("The four web cards: hidden together when the screen opens without a web search result")]
        [SerializeField] private GameObject[] webCards;

        [Header("Words")]
        [SerializeField] private string notFound = "Not found online";
        [SerializeField] private string statusWords = "Pending review: GlowUp checks new products before they join the catalogue.";
        [SerializeField] private string officialSiteNote = "The brand's own site.";
        [SerializeField] private string suggestedSiteNote = "Suggested by the search: not confirmed as the brand's own site yet.";
        [SerializeField] private string fdaWords = "Not checked yet: GlowUp looks it up on the FDA Philippines portal during review.";
        [Tooltip("{0} = the age in years")]
        [SerializeField] private string ageFormat = "{0}+ years, as the maker states.";
        [SerializeField] private string ageNotStated = "Not stated by the maker.";
        [SerializeField] private string noSourcesWords = "No web page was found for this product: the details come from the photo alone.";
        [Tooltip("Pictures larger than this many bytes are not downloaded")]
        [SerializeField] private int maximumPictureBytes = 2000000;

        readonly List<Texture2D> downloaded = new List<Texture2D>();

        void Start()
        {
            WebSearchResult web = ScanSession.WebResult;
            if (web == null)
            {
                // opened without a web search: only the usual result shows
                if (webCards != null) foreach (GameObject c in webCards) if (c != null) c.SetActive(false);
                return;
            }
            Show(web);
        }

        void OnDestroy()
        {
            foreach (Texture2D t in downloaded) if (t != null) Destroy(t);
            downloaded.Clear();
        }

        public void Show(WebSearchResult web)
        {
            // ---- brand
            Text(brandNameText, web.brandName);
            Text(marketText, string.IsNullOrEmpty(web.market) ? "Global" : web.market);
            Text(statusText, statusWords);
            Link(brandWebsiteButton, web.brandWebsite);
            if (brandWebsiteNote != null)
            {
                brandWebsiteNote.gameObject.SetActive(web.brandWebsite.Length > 0);
                brandWebsiteNote.text = web.brandWebsiteOfficial ? officialSiteNote : suggestedSiteNote;
            }
            string brandPicture = web.brandLogoUrl.Length > 0 ? web.brandLogoUrl : web.brandImageUrl;
            Link(brandPictureButton, brandPicture);
            Picture(brandLogo, brandLogoPlaceholder, brandPicture);

            // ---- product
            Text(productNameText, web.productName);
            Text(cosmeticTypeText, Capital(web.cosmeticType));
            Text(descriptionText, web.description);
            if (minimumAgeText != null) { minimumAgeText.text = web.minimumAge > 0 ? string.Format(ageFormat, web.minimumAge) : ageNotStated; Grey(minimumAgeText, web.minimumAge <= 0); }
            if (fdaNumberText != null) { fdaNumberText.text = fdaWords; Grey(fdaNumberText, true); }
            Link(productPhotoButton, web.productPhotoUrl);
            Link(productPageButton, web.productPageUrl);
            Picture(productPhoto, productPhotoPlaceholder, web.productPhotoUrl);

            // ---- sources (each kind in its own words; the button opens the page)
            int shown = Source(primarySource, web.primary, "Primary") + Source(secondarySource, web.secondary, "Secondary") + Source(thirdSource, web.third, "Third");
            if (noSourcesText != null) { noSourcesText.gameObject.SetActive(shown == 0); noSourcesText.text = noSourcesWords; }

            // ---- shades printed on the pack
            int count = 0;
            for (int i = 0; shadeSwatches != null && i < shadeSwatches.Length; i++)
            {
                WebSearchResult.Shade shade = i < web.shades.Count ? web.shades[i] : null;
                Transform chip = shadeSwatches[i] != null ? shadeSwatches[i].transform.parent : null;
                if (chip != null) chip.gameObject.SetActive(shade != null);
                if (shade == null) continue;
                count++;
                bool hasColour = ColorUtility.TryParseHtmlString(shade.hex, out Color colour);
                shadeSwatches[i].color = hasColour ? colour : new Color(1f, 1f, 1f, 0f);
                if (shadeLabels != null && i < shadeLabels.Length && shadeLabels[i] != null)
                    shadeLabels[i].text = shade.code.Length > 0 && shade.hex.Length > 0 ? shade.code + "  ·  " + shade.hex
                        : shade.code.Length > 0 ? shade.code : shade.hex;
            }
            if (shadesCard != null) shadesCard.SetActive(count > 0);
        }

        // ---------------------------------------------------------------------------------------
        void Text(TextMeshProUGUI label, string value)
        {
            if (label == null) return;
            bool none = string.IsNullOrWhiteSpace(value);
            label.text = none ? notFound : value;
            Grey(label, none);
        }

        static void Grey(TextMeshProUGUI label, bool grey)
        {
            // a missing value reads quieter (the colour comes from the theme; only the alpha changes)
            Color c = label.color; c.a = grey ? 0.62f : 1f; label.color = c;
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static void Link(Button button, string url)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            bool ok = !string.IsNullOrEmpty(url) && url.StartsWith("https://", System.StringComparison.Ordinal);
            button.gameObject.SetActive(ok);
            if (ok) button.onClick.AddListener(() => Application.OpenURL(url));
        }

        static int Source(SourceLinkButton button, WebSearchResult.Source source, string slot)
        {
            if (button == null) return 0;
            bool ok = source != null && button.Bind(source.url);
            button.gameObject.SetActive(ok);
            if (!ok) return 0;
            button.ShowKind(slot + " · " + source.label);
            return 1;
        }

        void Picture(RawImage target, GameObject placeholder, string url)
        {
            if (target == null) return;
            target.enabled = false;
            if (placeholder != null) placeholder.SetActive(true);
            if (!string.IsNullOrEmpty(url) && url.StartsWith("https://", System.StringComparison.Ordinal) && isActiveAndEnabled)
                StartCoroutine(Download(target, placeholder, url));
        }

        /// <summary>PNG or JPEG only (what Unity can show); anything else keeps the placeholder icon. The link button still opens it.</summary>
        IEnumerator Download(RawImage target, GameObject placeholder, string url)
        {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url, true))
            {
                request.timeout = 12;
                yield return request.SendWebRequest();
                if (this == null || target == null) yield break;
                if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > (ulong)Mathf.Max(1, maximumPictureBytes)) yield break;
                Texture2D texture = null;
                try { texture = DownloadHandlerTexture.GetContent(request); } catch (System.Exception) { texture = null; }
                if (texture == null || texture.width < 8 || texture.height < 8) { if (texture != null) Destroy(texture); yield break; }
                downloaded.Add(texture);
                target.texture = texture;
                target.enabled = true;
                // the whole picture fits inside its frame, never stretched
                AspectRatioFitter fit = target.GetComponent<AspectRatioFitter>();
                if (fit != null) { fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = texture.width / (float)texture.height; }
                if (placeholder != null) placeholder.SetActive(false);
            }
        }
    }
}
