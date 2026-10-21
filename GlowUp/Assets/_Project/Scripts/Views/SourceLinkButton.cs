using System;
using GlowUp.Scan;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// A small round button that opens a web page. The result screen uses it for the page an ingredient list came
    /// from, instead of printing the long web address as text: the button shows the site's name ("incidecoder.com")
    /// and a tap opens the page in the phone's browser. Only http and https addresses are ever opened.
    ///
    /// The pattern button in the scene is copied once per page: restyle that one object.
    /// </summary>
    public class SourceLinkButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TextMeshProUGUI label;
        [Tooltip("The small second line: what KIND of page it is (Regulator / government, Retailer listing ...). The words are shared with the admin website (ListSources.LabelOf), so they are not edited here. Optional.")]
        [SerializeField] private TextMeshProUGUI kindLabel;
        [Tooltip("Longest label, in letters (a longer site name is cut)")]
        [SerializeField] private int maximumLabelLength = 24;

        /// <summary>The web address this button opens ("" until Bind succeeded).</summary>
        public string Url { get; private set; } = "";
        /// <summary>The kind of page (ListSources.KindRegulator ... KindUnverified), set by Bind.</summary>
        public string Kind { get; private set; } = ListSources.KindUnverified;

        void Awake() { if (button != null) button.onClick.AddListener(Open); }

        /// <summary>
        /// `source` is a source line as the app keeps it: "page title | https://address" (the title is optional).
        /// `brandName` = the product's brand, so the maker's own site is recognised. Returns false when the line holds no
        /// web address; the button is then of no use.
        /// </summary>
        public bool Bind(string source, string brandName = "")
        {
            Url = AddressIn(source);
            if (Url.Length == 0) return false;
            string name = LabelFor(source, Url);
            int most = Mathf.Max(8, maximumLabelLength);
            if (label != null) label.text = name.Length > most ? name.Substring(0, most - 3).TrimEnd() + "..." : name;
            Kind = ListSources.SourceKind(source, brandName);
            if (kindLabel != null) kindLabel.text = ListSources.LabelOf(Kind);
            return true;
        }

        void Open() { if (Url.Length > 0) Application.OpenURL(Url); }

        // ---------------------------------------------------------------------------------------
        //  Reading a source line (no Unity objects involved, so these can be tested on their own)
        // ---------------------------------------------------------------------------------------
        /// <summary>The web address inside a source line, or "" when there is none (or it is not http / https).</summary>
        public static string AddressIn(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            int at = source.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
            if (at < 0) at = source.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return "";
            int end = at;
            while (end < source.Length && !char.IsWhiteSpace(source[end]) && source[end] != '|' && source[end] != '"' && source[end] != '<' && source[end] != '>') end++;
            string address = source.Substring(at, end - at).TrimEnd('.', ',', ';', ')', ']');
            return Uri.TryCreate(address, UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Contains(".")
                ? address : "";
        }

        /// <summary>"https://www.incidecoder.com/products/x" gives "incidecoder.com" ("" when it is not a web address).</summary>
        public static string SiteName(string url)
        {
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out Uri uri)) return "";
            string host = uri.Host.ToLowerInvariant();
            return host.StartsWith("www.", StringComparison.Ordinal) ? host.Substring(4) : host;
        }

        /// <summary>The page title in front of the "|" of a source line, or "".</summary>
        public static string TitleIn(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            int bar = source.IndexOf('|');
            return bar > 0 ? source.Substring(0, bar).Trim() : "";
        }

        /// <summary>
        /// The words on the button: the site's name. A page the AI's web search opened arrives as a pass-through address
        /// of the search service, which says nothing to a reader: for those the page's own title is used instead.
        /// </summary>
        public static string LabelFor(string source, string url)
        {
            string site = SiteName(url);
            bool passThrough = site.Contains("vertexaisearch") || site.EndsWith("googleusercontent.com", StringComparison.Ordinal) || url.Contains("grounding-api-redirect");
            if (!passThrough) return site;
            string title = TitleIn(source);
            return title.Length > 0 && !string.Equals(title, "web page", StringComparison.OrdinalIgnoreCase) ? title : "Source page";
        }
    }
}
