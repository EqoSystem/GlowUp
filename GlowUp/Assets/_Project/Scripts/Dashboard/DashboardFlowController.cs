using System;
using System.Collections.Generic;
using System.Globalization;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Scan;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Dashboard
{
    /// <summary>
    /// User Dashboard: greeting, the quick summary of the user's profile, product recommendations
    /// that are safe for this user's skin data and age, the account pop-up (sign out) and the
    /// Settings button (the gear in the top right corner).
    /// The three shortcut buttons at the bottom are handled by BottomActionBar.
    /// </summary>
    public class DashboardFlowController : MonoBehaviour
    {
        [Header("Greeting")]
        [SerializeField] private TextMeshProUGUI greetingText;
        [Tooltip("{0} = the user's first name")]
        [SerializeField] private string greetingFormat = "Hi, {0}";
        [SerializeField] private string nameWhenUnknown = "there";
        [Tooltip("A first name longer than this many letters is shortened to its first word in the greeting (\"Maria Cristina Angelica\" becomes \"Maria\"). 0 = never shortened.")]
        [SerializeField] private int greetingNameMaxLength = 16;

        [Header("Quick summary")]
        [SerializeField] private TextMeshProUGUI ageValue;
        [SerializeField] private TextMeshProUGUI birthdayValue;
        [SerializeField] private TextMeshProUGUI skinTypeValue;
        [SerializeField] private TextMeshProUGUI skinToneValue;
        [SerializeField] private TextMeshProUGUI allergensValue;
        [SerializeField] private string notSet = "Not set";
        [SerializeField] private string noAllergens = "None saved";
        [Tooltip("How the birthday is written, e.g. MMMM d, yyyy  or  dd/MM/yyyy")]
        [SerializeField] private string birthdayFormat = "MMMM d, yyyy";

        [Header("Recommendations")]
        [SerializeField] private RectTransform recommendationsContent;
        [Tooltip("The pattern tile. It is hidden when the app runs and copied once per recommended product.")]
        [SerializeField] private ProductTileView recommendationTemplate;
        [Tooltip("The grey placeholder tiles shown while the recommendations load (skeleton loading)")]
        [SerializeField] private GameObject recommendationsSkeleton;
        [Tooltip("The pop-up the tiles' AI button opens")]
        [SerializeField] private AiInsightPanel aiPanel;
        [SerializeField] private TextMeshProUGUI recommendationsEmptyText;
        [SerializeField] private int maximumRecommendations = 8;
        [SerializeField] private CosmeticTypeIcons typeIcons;
        [SerializeField] private string msgLoading = "Looking for make-up that matches your profile...";
        [Tooltip("Shown when no product passes the check. The app never falls back to showing a product that did not pass.")]
        [SerializeField] private string msgNone = "No make-up products matched your profile yet. Scan a product to check it.";
        [Tooltip("The small line under the heading that says what a recommendation means. Optional.")]
        [SerializeField] private TextMeshProUGUI recommendationsNoteText;
        [SerializeField] private string noteStandard = "No ingredients flagged for your profile. This is not medical advice.";
        [Tooltip("The same line for a user who saved a skin condition")]
        [SerializeField] private string noteWithCondition = "No ingredients flagged for your profile. This is not medical advice: ask a dermatologist about a flare-up.";
        [SerializeField] private string saveLabel = "Save";
        [SerializeField] private string savedLabel = "Saved";

        [Header("Account pop-up")]
        [SerializeField] private Button avatarButton;
        [SerializeField] private OverlayPanel accountPanel;
        [SerializeField] private TextMeshProUGUI accountNameText;
        [SerializeField] private TextMeshProUGUI accountEmailText;
        [SerializeField] private Button signOutButton;

        [Header("Log out: asked first (the same question as on the Settings page)")]
        [SerializeField] private GlowUp.AppSettings.ConfirmDialog signOutConfirm;
        [SerializeField] private string signOutTitle = "Log out?";
        [SerializeField] private string signOutMessage = "You can sign back in at any time with your email and password.";
        [Tooltip("Added in front when changes are still waiting to be sent. {0} = how many")]
        [SerializeField] private string signOutMessageWithPending = "{0} change(s) have not been sent to your account yet. They stay on this phone and are sent the next time you sign in online.";
        [SerializeField] private string signOutConfirmLabel = "Log Out";
        [Tooltip("The phone's back key (Android) closes the question or the account pop-up")]
        [SerializeField] private bool phoneBackKeyClosesPopups = true;

        [Header("Settings")]
        [Tooltip("The gear in the top right corner")]
        [SerializeField] private Button settingsButton;

        [Header("Scenes")]
        [SerializeField] private string loginScene = SceneNames.Login;
        [SerializeField] private string tryOnScene = SceneNames.ArTryOn;
        [SerializeField] private string settingsScene = SceneNames.Settings;

        UserProfileData profile;
        readonly List<GameObject> cards = new List<GameObject>();
        readonly List<ProductTileView> tiles = new List<ProductTileView>();

        void Awake()
        {
            if (recommendationTemplate != null) recommendationTemplate.gameObject.SetActive(false);
            if (avatarButton != null) avatarButton.onClick.AddListener(() => { if (accountPanel != null) accountPanel.Show(); });
            if (signOutButton != null) signOutButton.onClick.AddListener(AskSignOut);
            if (settingsButton != null) settingsButton.onClick.AddListener(() => SceneNavigator.Go(settingsScene));
        }

        async void Start()
        {
            ShowProfile(null);
            // while the list loads: grey placeholder tiles when the scene has them, otherwise the "looking..." line
            if (recommendationsSkeleton != null) recommendationsSkeleton.SetActive(true);
            if (recommendationsEmptyText != null) { recommendationsEmptyText.gameObject.SetActive(recommendationsSkeleton == null); recommendationsEmptyText.text = msgLoading; }

            try { profile = await UserProfileRepository.Load(); }
            catch (Exception e) { Debug.LogWarning("Dashboard: profile not loaded: " + e.Message); }
            if (this == null) return;                       // the scene was left while loading
            ShowProfile(profile);

            try { await ShowRecommendations(); }
            catch (Exception e)
            {
                Debug.LogWarning("Dashboard: recommendations not loaded: " + e.Message);
                if (this != null && recommendationsEmptyText != null) { recommendationsEmptyText.gameObject.SetActive(true); recommendationsEmptyText.text = msgNone; }
            }
            if (this != null && recommendationsSkeleton != null) recommendationsSkeleton.SetActive(false);
        }

        /// <summary>The name as the greeting shows it: a long first name is cut down to its first word so the line still fits.</summary>
        string GreetingName(string name)
        {
            name = (name ?? "").Trim();
            if (greetingNameMaxLength <= 0 || name.Length <= greetingNameMaxLength) return name;
            int space = name.IndexOf(' ');
            string first = space > 0 ? name.Substring(0, space) : name;
            return first.Length <= greetingNameMaxLength ? first : first.Substring(0, greetingNameMaxLength).TrimEnd() + "...";
        }

        // ---------------------------------------------------------------------------------------
        void ShowProfile(UserProfileData p)
        {
            // the greeting uses the FIRST NAME (sign-up no longer asks for a username; an account made before that, with no
            // first name on record, still falls back to its username)
            string name = p == null ? "" : !string.IsNullOrWhiteSpace(p.firstName) ? p.firstName.Trim() : (p.username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) name = nameWhenUnknown;
            if (greetingText != null) greetingText.text = string.Format(greetingFormat, GreetingName(name));

            Set(ageValue, p != null && p.age > 0 ? p.age.ToString() : notSet);
            string birthday = notSet;
            if (p != null && DateTime.TryParse(p.birthday, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
                birthday = dob.ToString(birthdayFormat, CultureInfo.CurrentCulture);
            Set(birthdayValue, birthday);
            Set(skinTypeValue, p != null && !string.IsNullOrWhiteSpace(p.skinType) ? p.skinType : notSet);
            Set(skinToneValue, p != null && !string.IsNullOrWhiteSpace(p.skinTone) ? p.skinTone : notSet);
            Set(allergensValue, p != null && p.knownAllergens != null && p.knownAllergens.Count > 0 ? string.Join(", ", p.knownAllergens) : noAllergens);

            if (accountNameText != null)
            {
                string full = p == null ? "" : string.Join(" ", new[] { p.firstName, p.lastName }).Trim();
                accountNameText.text = string.IsNullOrWhiteSpace(full) ? name : full;
            }
            if (accountEmailText != null) accountEmailText.text = p != null ? p.email : "";
        }

        static void Set(TextMeshProUGUI label, string value) { if (label != null) label.text = value; }

        // ---------------------------------------------------------------------------------------
        async System.Threading.Tasks.Task ShowRecommendations()
        {
            List<ProductData> products = await ProductRepository.LoadActive();
            await IngredientRepository.LoadAll();
            if (this == null) return;

            foreach (var c in cards) if (c != null) Destroy(c);
            cards.Clear(); tiles.Clear();

            // Only MAKE-UP whose ingredients are not flagged for this user. For a user who saved a skin condition the check is
            // stricter: the ingredient list must have a source and every ingredient must be known (see ProductRecommender).
            // All of it is worked out on the phone. (How strict it is: Assets/_Project/Resources/ScanRules.asset)
            var who = profile ?? new UserProfileData();
            foreach (ProductRecommender.Pick pick in ProductRecommender.For(who, products, maximumRecommendations))
            {
                ProductData product = pick.product;
                ScanRecord record = pick.record;
                if (recommendationTemplate == null || recommendationsContent == null) continue;
                // a long row with the kind's icon, the name and the brand; a tap makes it longer and shows Save / Try On / AI
                ProductTileView tile = Instantiate(recommendationTemplate, recommendationsContent);
                tile.gameObject.name = "Recommendation_" + product.name;
                tile.gameObject.SetActive(true);
                tile.Bind(record, product.name, product.brandName, product.cosmeticType, typeIcons != null ? typeIcons.For(product.cosmeticType) : null);
                tile.SetSaveLabel(saveLabel);
                tile.SetTryOnAvailable(GlowUp.Scan.ProductScope.CanTryOn(tile.Data as ScanRecord));
                tile.TryOnClicked += OnTryOn;
                tile.SaveClicked += OnSave;
                tile.AiClicked += t => { if (aiPanel != null) aiPanel.Show(t.Data as ScanRecord); };
                tile.DrawerOpening += CloseOtherDrawers;
                tiles.Add(tile);
                cards.Add(tile.gameObject);
            }

            if (recommendationsEmptyText != null)
            {
                recommendationsEmptyText.gameObject.SetActive(cards.Count == 0);
                recommendationsEmptyText.text = msgNone;
            }
            // what a recommendation means, and does not mean: said under the heading when there is something to show
            if (recommendationsNoteText != null)
            {
                recommendationsNoteText.gameObject.SetActive(cards.Count > 0);
                recommendationsNoteText.text = ProductScope.HasSkinCondition(who) ? noteWithCondition : noteStandard;
            }
        }

        // one row open at a time, so the list stays calm
        void CloseOtherDrawers(ProductTileView opened)
        {
            foreach (ProductTileView tile in tiles) if (tile != null && tile != opened) tile.SetDrawer(false);
        }

        void OnTryOn(ProductTileView tile)
        {
            ScanSession.TryOn = tile.Data as ScanRecord;
            SceneNavigator.Go(tryOnScene);
        }

        void OnSave(ProductTileView tile)
        {
            if (tile.Data is ScanRecord record && string.IsNullOrEmpty(record.id)) ScanHistoryRepository.Add(record);   // lands in Recent Scans
            tile.SetSaveLabel(savedLabel, false);
        }

        /// <summary>
        /// "Log out?" with Cancel / Log Out. Nothing happens unless the user confirms; a tap beside the box and the phone's
        /// back key cancel. Changes that have not been sent yet are mentioned (they stay on the phone and are sent later).
        /// </summary>
        void AskSignOut()
        {
            if (signOutConfirm == null) { SignOut(); return; }          // a scene built before the question existed
            int waiting = SyncQueue.PendingCount;
            string message = waiting > 0 ? string.Format(signOutMessageWithPending, waiting) + "\n\n" + signOutMessage : signOutMessage;
            if (accountPanel != null) accountPanel.Hide();
            signOutConfirm.Ask(signOutTitle, message, signOutConfirmLabel, SignOut, true);
        }

        void Update()
        {
            if (!phoneBackKeyClosesPopups || !Input.GetKeyDown(KeyCode.Escape)) return;
            if (signOutConfirm != null && signOutConfirm.IsOpen) signOutConfirm.Close();
            else if (accountPanel != null && accountPanel.IsOpen) accountPanel.Hide();
        }

        void SignOut()
        {
            UserProfileRepository.SignOut();
            PlayerPrefs.DeleteKey("UserEmail");
            SceneNavigator.Go(loginScene);
        }
    }
}
