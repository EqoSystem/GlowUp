using System;
using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Scan;
using GlowUp.Scanning;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// The pop-up behind every "AI" button on a product tile. It asks the AI what the product is, who it
    /// suits, what to take care with and how it is used, and lists the sources. An answer is kept on the
    /// phone, so the same product can be read again without internet; without internet and without an
    /// earlier answer it says so. All wording is in the Inspector.
    /// </summary>
    public class AiInsightPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private OverlayPanel panel;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private TextMeshProUGUI bodyText;
        [Tooltip("The small line under the answer (where it came from)")]
        [SerializeField] private TextMeshProUGUI noteText;
        [Tooltip("Turns while the AI is being asked")]
        [SerializeField] private RectTransform busyIndicator;
        [SerializeField] private Button retryButton;
        [SerializeField] private ScrollRect scroll;

        [Header("Sources")]
        [SerializeField] private TextMeshProUGUI sourcesHeader;
        [SerializeField] private RectTransform sourcesContent;
        [Tooltip("The pattern button, copied once per source; tapping one opens the page")]
        [SerializeField] private Button sourceTemplate;
        [SerializeField] private int maximumSources = 4;

        [Header("AI")]
        [Tooltip("The AI settings (models, timeout)")]
        [SerializeField] private GeminiApiConfig aiConfig;
        [SerializeField] private float busyTurnsPerSecond = 0.9f;

        [Header("Wording")]
        [SerializeField] private string msgAsking = "Asking the AI about this product...";
        [SerializeField] private string msgOffline = "You are offline. Connect to the internet and tap Try again to ask the AI about this product.";
        [SerializeField] private string msgNothing = "The AI could not find anything reliable about this product. Try again in a moment.";
        [SerializeField] private string noteWebSearch = "Checked with a live web search. General information, not medical advice.";
        [SerializeField] private string noteNoWebSearch = "The AI answered from what it knows and could not open the sources itself. General information, not medical advice.";
        [Tooltip("{0} = the date the answer was saved")]
        [SerializeField] private string noteSaved = "Saved on this phone on {0}.";
        [SerializeField] private string sourcesTitle = "Sources";

        readonly List<GameObject> sourceButtons = new List<GameObject>();
        string productName, brand, type;
        List<string> ingredients;
        bool busy;
        int request;

        void Awake()
        {
            if (aiConfig != null) ProductAiAdvisor.Config = aiConfig;
            if (sourceTemplate != null) sourceTemplate.gameObject.SetActive(false);
            if (retryButton != null) retryButton.onClick.AddListener(Ask);
            if (busyIndicator != null) busyIndicator.gameObject.SetActive(false);
        }

        void Update()
        {
            if (busy && busyIndicator != null) busyIndicator.Rotate(0f, 0f, -360f * busyTurnsPerSecond * Time.unscaledDeltaTime);
        }

        /// <summary>Opens the pop-up for a scanned or recommended product.</summary>
        public void Show(ScanRecord record)
        {
            if (record == null) return;
            Show(!string.IsNullOrWhiteSpace(record.productName) ? record.productName : record.Title, record.brandName, record.cosmeticType, record.ingredients);
        }

        public void Show(string name, string brandName, string cosmeticType, List<string> ingredientList)
        {
            productName = name ?? ""; brand = brandName ?? ""; type = cosmeticType ?? ""; ingredients = ingredientList;
            if (titleText != null) titleText.text = productName;
            if (subtitleText != null)
            {
                bool hasBrand = !string.IsNullOrWhiteSpace(brand), hasType = !string.IsNullOrWhiteSpace(type);
                subtitleText.text = hasBrand && hasType ? brand + "  |  " + type : hasBrand ? brand : type;
            }
            if (panel != null) panel.Show();

            ProductAiAdvisor.Insight saved = ProductAiAdvisor.Remembered(productName, brand);
            if (saved != null) { Display(saved, true); return; }
            Ask();
        }

        async void Ask()
        {
            if (busy) return;
            if (!ConnectivityMonitor.Online || !ProductAiAdvisor.Available) { Message(msgOffline, true); return; }
            int mine = ++request;
            SetBusy(true);
            Message(msgAsking, false);
            ProductAiAdvisor.Insight answer = null;
            try { answer = await ProductAiAdvisor.Describe(productName, brand, type, ingredients); }
            catch (Exception e) { Debug.LogWarning("AI product description: " + e.Message); }
            if (this == null || mine != request) return;
            SetBusy(false);
            if (answer == null || string.IsNullOrWhiteSpace(answer.text)) { Message(msgNothing, true); return; }
            Display(answer, false);
        }

        void SetBusy(bool value)
        {
            busy = value;
            if (busyIndicator != null) busyIndicator.gameObject.SetActive(value);
            if (retryButton != null) retryButton.gameObject.SetActive(!value);
        }

        void Message(string text, bool offerRetry)
        {
            if (bodyText != null) bodyText.text = text;
            if (noteText != null) noteText.text = "";
            if (retryButton != null) retryButton.gameObject.SetActive(offerRetry);
            ShowSources(null);
        }

        void Display(ProductAiAdvisor.Insight insight, bool fromThisPhone)
        {
            if (bodyText != null) bodyText.text = insight.text;
            if (noteText != null)
            {
                string note = insight.webSearched ? noteWebSearch : noteNoWebSearch;
                if (fromThisPhone && insight.savedAtUnix > 0)
                    note += " " + string.Format(noteSaved, DateTimeOffset.FromUnixTimeSeconds(insight.savedAtUnix).ToLocalTime().ToString("d MMM yyyy"));
                noteText.text = note;
            }
            if (retryButton != null) retryButton.gameObject.SetActive(fromThisPhone);     // lets the user ask again for a fresh answer
            ShowSources(insight.sources);
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        void ShowSources(List<string> sources)
        {
            foreach (GameObject b in sourceButtons) if (b != null) Destroy(b);
            sourceButtons.Clear();
            bool any = sources != null && sources.Count > 0 && sourceTemplate != null && sourcesContent != null;
            if (sourcesHeader != null) { sourcesHeader.gameObject.SetActive(any); sourcesHeader.text = sourcesTitle; }
            if (!any) return;
            foreach (string line in sources)
            {
                if (sourceButtons.Count >= maximumSources) break;
                int bar = line.LastIndexOf('|');
                string title = bar > 0 ? line.Substring(0, bar).Trim() : line.Trim();
                string address = bar > 0 ? line.Substring(bar + 1).Trim() : "";
                if (!address.StartsWith("http")) continue;
                Button button = Instantiate(sourceTemplate, sourcesContent);
                button.gameObject.name = "Source_" + (sourceButtons.Count + 1);
                button.gameObject.SetActive(true);
                var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.text = title;
                string open = address;
                button.onClick.AddListener(() => Application.OpenURL(open));
                sourceButtons.Add(button.gameObject);
            }
            if (sourcesHeader != null) sourcesHeader.gameObject.SetActive(sourceButtons.Count > 0);
        }
    }
}
