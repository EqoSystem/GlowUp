using System;
using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// SCAN SNAPSHOT (AI photo fallback): when a scanned product is NOT in the catalogue, the user is offered to take one photo
    /// of the pack. The AI reads the brand, name, kind, shade and ingredient list from it; the result is analysed at once and
    /// marked PENDING APPROVAL, and it is staged in "pending_products" (shared with every user) until an admin approves it on
    /// the website.
    ///
    ///   1. The catalogue was checked first (the scan screen does that: 0 AI cost).
    ///   2. Another user may have snapped it already: that entry is used (at most 1 read; none when this phone looked recently).
    ///   3. Otherwise the photo (at most "Longest Side" pixels, JPG squeezed under "Maximum Photo Bytes") goes to the AI ONCE,
    ///      and the reading is staged. The same product on this phone later comes from the phone's own copy (0 reads, 0 AI).
    ///   Spam guard: the button locks the moment it is tapped, shows "Analyzing Packaging..." with a spinning icon, and stays
    ///   locked for "Cooldown Seconds"; the AI is asked at most as often as the scan screen's AI budget allows.
    ///
    /// Every word, number and the spinner's speed are Inspector fields of this object ("Scan Snapshot" in the Hierarchy).
    /// </summary>
    public class ScanSnapshotController : MonoBehaviour
    {
        [Header("The pop-up (built by the scan scene builder)")]
        [SerializeField] private OverlayPanel panel;
        [Tooltip("The sentence that tells the user why the pop-up is there")]
        [SerializeField] private TextMeshProUGUI promptText;
        [SerializeField] private Button takePhotoButton;
        [SerializeField] private TextMeshProUGUI takePhotoLabel;
        [Tooltip("The icon that turns while the AI reads the photo (inactive otherwise)")]
        [SerializeField] private RectTransform spinner;
        [Tooltip("What happened, or what to do next")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button cancelButton;

        [Header("Camera")]
        [SerializeField] private CameraFeedController cameraFeed;
        [Tooltip("The part of the picture that is photographed (the scan frame). Empty = the whole picture.")]
        [SerializeField] private RectTransform frame;

        [Header("The photo (sent to the AI and kept for the admin)")]
        [Tooltip("The longer side of the photo, in pixels (1080 = Full HD)")]
        [Range(480, 1920)] [SerializeField] private int longestSide = 1080;
        [Tooltip("The photo is squeezed (lower JPG quality, then smaller) until it is at most this many bytes. 80000 = 80 KB.")]
        [Range(20000, 200000)] [SerializeField] private int maximumPhotoBytes = 80000;
        [Tooltip("JPG quality tried first (lower = smaller file)")]
        [Range(40, 95)] [SerializeField] private int startQuality = 80;
        [Tooltip("JPG quality never goes below this; the photo is made smaller instead")]
        [Range(20, 80)] [SerializeField] private int lowestQuality = 45;
        [Tooltip("On = the admin sees the photo next to the AI's reading on the website (its own document, read by admins only)")]
        [SerializeField] private bool keepPhotoForAdmin = true;

        [Header("The server function (holds the AI key; this phone holds none for this path)")]
        [Tooltip("The https address of the photo function on Vercel, Empty = the built-in address of GlowUp's own function is used.")]
        [SerializeField] private string functionUrl = "https://glowup-admin-snowy.vercel.app/api/scan-snapshot";
        [Tooltip("The longest wait for the server's answer, in seconds. One request, never repeated.")]
        [Range(10f, 55f)] [SerializeField] private float functionTimeoutSeconds = 45f;

        [Header("Spam guard")]
        [Tooltip("How fast the spinner turns, in degrees per second")]
        [SerializeField] private float spinnerDegreesPerSecond = 300f;
        [Tooltip("A product that was looked for among other users' photos and not found is not looked for again for this many minutes")]
        [Min(0f)] [SerializeField] private float rememberMissMinutes = 30f;
        [Tooltip("A staged product kept on this phone is looked at again after this many hours (it may have been approved meanwhile)")]
        [Min(1f)] [SerializeField] private float keepStagedHours = 24f;
        [Tooltip("The AI's reading counts when it is at least this sure (0 to 1)")]
        [Range(0f, 1f)] [SerializeField] private float minimumConfidence = 0.35f;

        [Header("Wording")]
        [SerializeField] private string msgPrompt = "This product was not found in the catalogue.";
        [Tooltip("One line: nothing is taken or sent before the tap")]
        [SerializeField] private string msgConsent = "Only if you tap \"Take a photo of the product\", one photo is sent to an AI web search to identify it.";
        [SerializeField] private string msgHowTo = "Show the front of the pack, or the side with the ingredient list, inside the frame. Good light helps.";
        [SerializeField] private string idleLabel = "Take a photo of the product";
        [SerializeField] private string busyLabel = "Analyzing Packaging...";
        [Tooltip("{0} = seconds left")]
        [SerializeField] private string coolingLabel = "Wait {0} s";
        [SerializeField] private string msgNoCamera = "The camera is not running. Close this, tap the scan button, then try again.";
        [SerializeField] private string msgOffline = "No internet: the photo cannot be read now. Try again when you are online, or type the product in.";
        [SerializeField] private string msgNoAi = "Photo reading is not set up yet. Type the product in by hand.";
        [SerializeField] private string msgSignIn = "Sign in again to send a photo, or type the product in by hand.";
        [Tooltip("{0} = seconds")]
        [SerializeField] private string msgCooldown = "Wait {0} seconds before the next photo.";
        [Tooltip("{0} = photos per day")]
        [SerializeField] private string msgDailyLimit = "You have sent {0} photos today. Try again tomorrow, or type the product in.";
        [SerializeField] private string msgBusy = "Photo reading is busy for today. Type the product in by hand, or try again tomorrow.";
        [SerializeField] private string msgDown = "The photo could not be read. Type the product in by hand.";
        [SerializeField] private string msgNoWebSources = "No web sources were found: this reading comes from the photo alone.";
        [SerializeField] private string msgNothing = "The AI could not read a product in the photo. Try more light, wipe the lens, show the front of the pack, or type it in.";
        [SerializeField] private string msgNoList = "Found {0}, but no ingredient list could be read. Turn the pack to its ingredient list and take another photo.";
        [SerializeField] private string msgFromYou = "You already sent this product: its reading is used. It is waiting for an admin's approval.";

        /// <summary>
        /// The photo gave a result. listed = the product IS in the catalogue (the AI read its name); otherwise staged = the staged
        /// entry (new or another user's), with its ingredient list.
        /// </summary>
        public event Action<ProductData, PendingProductData> Analysed;
        /// <summary>The AI read a name but no ingredient list: the scan screen keeps the name and goes on.</summary>
        public event Action<ProductAiAdvisor.Identity> NameOnly;

        /// <summary>Seconds between two photos (the server enforces the same).</summary>
        public const float CooldownSeconds = 30f;
        /// <summary>Photos per device and day (the server allows 5 per account and day too).</summary>
        public const int DailyLimit = 5;
        const string DayKey = "snapshot.day", CountKey = "snapshot.count";

        string brandHint = "", productHint = "";      // what the live scan already read of this product (a head start for the web search)

        bool busy;
        float lockedUntil;
        int lastShownSecond = -1;

        public bool IsOpen => panel != null && panel.IsOpen;

        /// <summary>Used when the Inspector's address is empty (a scene saved before the address was set keeps an empty value, which would switch the photo path off).</summary>
        const string DefaultFunctionUrl = "https://glowup-admin-snowy.vercel.app/api/scan-snapshot";

        void Awake()
        {
            // A scene saved by an older build keeps the words of that build: the old defaults are replaced by the new ones. A word the
            // project owner changed by hand is not an old default, so it is kept.
            Upgrade(ref msgPrompt, "Product not in catalog yet! Tap to take a quick photo for live AI analysis.", "This product was not found in the catalogue.");
            Upgrade(ref idleLabel, "Take photo", "Take a photo of the product");
            Upgrade(ref msgNoAi, "The AI is not set up on this phone, so the photo cannot be read. Type the product in instead.", "Photo reading is not set up yet. Type the product in by hand.");
            // a scene built before the wider button holds this long label in a narrow one: it gets smaller instead of running over the icon
            if (takePhotoLabel != null && !takePhotoLabel.enableAutoSizing)
            {
                takePhotoLabel.enableWordWrapping = false; takePhotoLabel.enableAutoSizing = true;
                takePhotoLabel.fontSizeMax = takePhotoLabel.fontSize; takePhotoLabel.fontSizeMin = 18f;
                takePhotoLabel.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (takePhotoButton != null) takePhotoButton.onClick.AddListener(Take);
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
            if (spinner != null) spinner.gameObject.SetActive(false);
            ApplyNumbers();
        }

        static string Today => DateTime.UtcNow.ToString("yyyyMMdd");

        /// <summary>Photos sent from this phone today (0 on a new day).</summary>
        static int SentToday() => PlayerPrefs.GetString(DayKey, "") == Today ? PlayerPrefs.GetInt(CountKey, 0) : 0;

        static void CountOne()
        {
            PlayerPrefs.SetString(DayKey, Today);
            PlayerPrefs.SetInt(CountKey, SentToday() + 1);
            PlayerPrefs.Save();
        }

        void OnValidate() { if (lowestQuality > startQuality) lowestQuality = startQuality; }

        static void Upgrade(ref string current, string oldDefault, string newDefault)
        {
            if (string.IsNullOrWhiteSpace(current) || current == oldDefault) current = newDefault;
        }

        void ApplyNumbers()
        {
            PendingProductRepository.RememberMissMinutes = rememberMissMinutes;
            PendingProductRepository.KeepHours = keepStagedHours;
            ScanPhotoService.FunctionUrl = string.IsNullOrWhiteSpace(functionUrl) ? DefaultFunctionUrl : functionUrl.Trim();
            ScanPhotoService.TimeoutSeconds = functionTimeoutSeconds;
        }

        /// <summary>Opens the pop-up (the scan screen calls this when the catalogue does not have the product).</summary>
        public void Offer(string why = null, string brandHint = "", string productHint = "")
        {
            if (panel == null) return;
            this.brandHint = brandHint ?? ""; this.productHint = productHint ?? "";
            if (promptText != null) promptText.text = string.IsNullOrEmpty(why) ? msgPrompt : why;
            if (statusText != null) statusText.text = msgConsent + "\n" + msgHowTo;
            Paint();
            if (!panel.IsOpen) panel.Show();
        }

        public void Close() { if (panel != null && panel.IsOpen) panel.Hide(); }

        void Update()
        {
            if (spinner != null && spinner.gameObject.activeSelf) spinner.Rotate(0f, 0f, -spinnerDegreesPerSecond * Time.unscaledDeltaTime);
            // the cool-down count on the button
            if (!busy && Time.unscaledTime < lockedUntil)
            {
                int left = Mathf.CeilToInt(lockedUntil - Time.unscaledTime);
                if (left != lastShownSecond) { lastShownSecond = left; Paint(); }
            }
            else if (lastShownSecond != -1 && !busy) { lastShownSecond = -1; Paint(); }
        }

        void Paint()
        {
            bool cooling = Time.unscaledTime < lockedUntil;
            if (takePhotoButton != null) takePhotoButton.interactable = !busy && !cooling;
            if (takePhotoLabel != null)
                takePhotoLabel.text = busy ? busyLabel : cooling ? string.Format(coolingLabel, Mathf.CeilToInt(lockedUntil - Time.unscaledTime)) : idleLabel;
            if (spinner != null && spinner.gameObject.activeSelf != busy) spinner.gameObject.SetActive(busy);
        }

        void Say(string message) { if (statusText != null) statusText.text = message; }

        /// <summary>The Take photo button: locked at once, one photo, one AI request at most.</summary>
        async void Take()
        {
            if (busy || Time.unscaledTime < lockedUntil) return;           // a second tap while it works or cools down is ignored
            busy = true;
            Paint();
            try
            {
                if (cameraFeed == null || !cameraFeed.IsPlaying) { Say(msgNoCamera); return; }
                if (!ConnectivityMonitor.Online) { Say(msgOffline); return; }
                if (string.IsNullOrEmpty(ScanPhotoService.FunctionUrl)) { Say(msgNoAi); return; }
                if (SentToday() >= DailyLimit) { Say(string.Format(msgDailyLimit, DailyLimit)); return; }

                byte[] jpeg = Photo();
                if (jpeg == null) { Say(msgNoCamera); return; }

                // from here on the photo leaves the phone, after the user's tap; the cool-down starts now
                lockedUntil = Time.unscaledTime + CooldownSeconds;
                CountOne();
                Paint();
                ScanPhotoService.Reading read = await ScanPhotoService.Read(jpeg, brandHint, productHint);
                if (this == null) return;
                switch (read.outcome)
                {
                    case ScanPhotoService.Outcome.Offline: Say(msgOffline); return;
                    case ScanPhotoService.Outcome.NotSetUp: Say(msgNoAi); return;
                    case ScanPhotoService.Outcome.SignIn: Say(msgSignIn); return;
                    case ScanPhotoService.Outcome.Cooldown: Say(string.Format(msgCooldown, (int)CooldownSeconds)); return;
                    case ScanPhotoService.Outcome.DailyLimit: Say(string.Format(msgDailyLimit, DailyLimit)); return;
                    case ScanPhotoService.Outcome.Busy: Say(msgBusy); return;
                    case ScanPhotoService.Outcome.NotFound: Say(msgNothing); return;
                    case ScanPhotoService.Outcome.Down: Say(msgDown); return;
                }
                var reading = new ProductAiAdvisor.PackReading { ingredients = read.ingredients };
                reading.identity.found = true;
                reading.identity.brandName = read.brand; reading.identity.productName = read.productName; reading.identity.cosmeticType = read.cosmeticType;
                reading.identity.shadeHex = read.shadeHex; reading.identity.confidence = read.confidence; reading.identity.model = read.model;
                ProductAiAdvisor.Identity who = reading.identity;
                if (who.confidence < minimumConfidence || (who.productName.Length == 0 && who.brandName.Length == 0)) { Say(msgNothing); return; }

                // the AI read a product the catalogue DOES have (the label was hard to read): use the catalogue's entry
                ProductData listed = who.productName.Length > 0 ? ProductRepository.Index.Exact(who.brandName, who.productName) : null;
                if (listed != null) { Close(); Analysed?.Invoke(listed, null); return; }

                // the user's OWN earlier photo of this product is used again (no AI cost); another user's entry never is
                PendingProductData staged = who.productName.Length > 0 ? await PendingProductRepository.FindAsync(who.brandName, who.productName) : null;
                if (this == null) return;
                if (staged != null && staged.ingredients.Count > 0) { Say(msgFromYou); Close(); Analysed?.Invoke(null, staged); return; }

                if (reading.ingredients.Count == 0 || who.productName.Length == 0)
                {
                    string name = (who.brandName + " " + who.productName).Trim();
                    Say(string.Format(msgNoList, name.Length > 0 ? name : "the product"));
                    NameOnly?.Invoke(who);
                    return;
                }

                // MAKE-UP ONLY, as everywhere else: something else is not staged
                ProductScope.Verdict verdict = ProductScope.Judge(who.cosmeticType, who.productName);
                if (verdict.IsNotMakeup) { Say(ProductScope.StopMessage(verdict)); return; }

                var record = new PendingProductRecord
                {
                    name = Limit(who.productName, 100), brandName = Limit(who.brandName, 60), cosmeticType = Limit(who.cosmeticType, 40),
                    shadeHex = who.shadeHex ?? "", ingredients = Limited(reading.ingredients), confidence = who.confidence, model = Limit(who.model, 60),
                    variant = Limit(read.variant, 60), size = Limit(read.size, 40), sourceUrls = read.sourceUrls
                };
                PendingProductData made = PendingProductRepository.Submit(record, keepPhotoForAdmin ? jpeg : null, maximumPhotoBytes);
                if (made == null) { Say(msgNothing); return; }
                if (!read.webSources) Debug.Log(msgNoWebSources);
                Close();
                Analysed?.Invoke(null, made);
            }
            catch (Exception e)
            {
                Debug.LogWarning("AI snapshot failed: " + e.Message);
                if (this != null) Say(msgNothing);
            }
            finally
            {
                busy = false;
                if (this != null) Paint();
            }
        }

        /// <summary>The photo as JPG, at most longestSide pixels and maximumPhotoBytes bytes (quality first, then size).</summary>
        byte[] Photo()
        {
            int side = longestSide;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Texture2D picture = cameraFeed.CaptureUpright(frame, side);
                if (picture == null) return null;
                try
                {
                    byte[] best = null;
                    for (int quality = startQuality; quality >= lowestQuality; quality -= 10)
                    {
                        best = picture.EncodeToJPG(quality);
                        if (best.Length <= maximumPhotoBytes) return best;
                    }
                    if (attempt == 3) return best;                  // the smallest that could be made
                }
                finally { Destroy(picture); }
                side = Mathf.Max(320, Mathf.RoundToInt(side * 0.75f));
            }
            return null;
        }

        static string Limit(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s.Trim() : s.Substring(0, max).Trim();

        static List<string> Limited(List<string> names)
        {
            var list = new List<string>();
            foreach (string n in names ?? new List<string>()) if (!string.IsNullOrWhiteSpace(n) && list.Count < 80) list.Add(Limit(n, 120));
            return list;
        }
    }
}
