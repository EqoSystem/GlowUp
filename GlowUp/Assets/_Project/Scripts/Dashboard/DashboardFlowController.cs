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
        [SerializeField] private string msgProfileIncomplete = "Complete your skin profile to see personalized recommendations.";
        [SerializeField] private string msgNoneComplete = "We're still building recommendations for you.";
        [Tooltip("The small line under the heading that says what a recommendation means. Optional.")]
        [SerializeField] private TextMeshProUGUI recommendationsNoteText;
        [Tooltip("The small \"i\" at the end of \"Recommended for you\": the note (not medical advice ...) is inside its bubble")]
        [SerializeField] private HelpBadge recommendationsBadge;
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

        [Header("Account pop-up: photo and full skin profile (New22, 20 November 2026)")]
        [Tooltip("The round picture in the pop-up: a tap asks first, then opens the phone's photo picker")]
        [SerializeField] private Button accountAvatarButton;
        [Tooltip("Every skin answer, also those not in the Quick Summary (skin concerns)")]
        [SerializeField] private TextMeshProUGUI skinProfileText;          // the one-text profile of New22 (scenes built before 22 Nov 2026)
        [Header("Account pop-up facts (22 Nov 2026)")]
        [SerializeField] private TextMeshProUGUI skinTypeFact;
        [SerializeField] private TextMeshProUGUI skinToneFact;
        [SerializeField] private TextMeshProUGUI conditionFact;
        [SerializeField] private TextMeshProUGUI allergensFact;
        [SerializeField] private Button closeAccountButton;
        [Tooltip("Opens the saved answers for editing (sign-up Steps 1 to 5, filled in; Save edits returns here)")]
        [SerializeField] private Button editSkinProfileButton;
        [SerializeField] private string photoTitle = "Choose a profile photo?";
        [SerializeField] private string photoMessage = "GlowUp opens your phone's photo picker. Only the photo you choose is shared with the app. It is shown on your account and to the GlowUp team.";
        [SerializeField] private string photoConfirm = "Choose photo";
        [SerializeField] private string photoSaved = "Profile photo updated.";
        [SerializeField] private string photoFailed = "The photo could not be used. Try another one.";
        [SerializeField] private string photoUnavailable = "This phone has no photo picker GlowUp can open.";

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
        [SerializeField] private GlowUp.Notifications.BellReveal bellReveal;    // 22 Nov 2026: the first tap on the gear slides the bell out

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
            if (settingsButton != null) settingsButton.onClick.AddListener(() => { if (bellReveal != null && bellReveal.Reveal()) return; SceneNavigator.Go(settingsScene); });
            if (accountAvatarButton != null) accountAvatarButton.onClick.AddListener(AskPhoto);
            if (editSkinProfileButton != null) editSkinProfileButton.onClick.AddListener(EditSkinProfile);
            if (closeAccountButton != null) closeAccountButton.onClick.AddListener(() => { if (accountPanel != null) accountPanel.Hide(); });
            GlowUp.AppSettings.ProfilePhoto.Changed += ShowPhoto;
            ShowPhoto();
        }

        void OnDestroy() { GlowUp.AppSettings.ProfilePhoto.Changed -= ShowPhoto; }

        void ShowPhoto()
        {
            if (avatarButton != null) GlowUp.AppSettings.ProfilePhoto.Apply(avatarButton.targetGraphic);
            if (accountAvatarButton != null) GlowUp.AppSettings.ProfilePhoto.Apply(accountAvatarButton.targetGraphic);
        }

        void AskPhoto()
        {
            if (signOutConfirm == null) { PickPhoto(); return; }
            signOutConfirm.Ask(photoTitle, photoMessage, photoConfirm, PickPhoto);
        }

        void PickPhoto()
        {
            GlowUp.AppSettings.ProfilePhoto.Choose((ok, reason) =>
            {
                if (this == null || reason == "cancelled") return;
                FormalToast toast = FormalToast.OnScreen(transform, greetingText);
                if (toast == null) return;
                if (ok) toast.Show(FormalToast.Kind.Success, photoSaved);
                else toast.Show(FormalToast.Kind.Error, reason == "no-picker" ? photoUnavailable : photoFailed);
            });
        }

        // the saved answers open in sign-up Steps 1 to 5 (email and password locked); "Save edits" writes them and comes back here,
        // and the Quick Summary, this pop-up and the admins' User Management page all show the new answers (live)
        void EditSkinProfile()
        {
            if (accountPanel != null) accountPanel.Hide();
            if (profile == null) { GlowUp.Onboarding.OnboardingSession.BeginRetake(SceneNames.Dashboard); SceneNavigator.Go(SceneNames.Onboarding2); return; }
            GlowUp.Onboarding.OnboardingSession.BeginRetakeWith(profile, SceneNames.Dashboard);
            SceneNavigator.Go(SceneNames.Onboarding1);
        }

        async void Start()
        {
            // what this phone already knows about the user is shown at once; the cloud is asked after that
            profile = UserProfileRepository.Cached();
            ShowProfile(profile);
            bool nothingYet = profile == null;
            // while the list loads: placeholder rows when the scene has them, otherwise the "looking..." line
            if (recommendationsSkeleton != null) recommendationsSkeleton.SetActive(true);
            if (recommendationsEmptyText != null) { recommendationsEmptyText.gameObject.SetActive(recommendationsSkeleton == null); recommendationsEmptyText.text = msgLoading; }

            // nothing on the phone yet (a first sign-in here): the summary's VALUES are bars until the profile has been fetched.
            // Its labels, the titles and the buttons are never bars (see TextSkeleton).
            if (nothingYet) TextSkeleton.Waiting(this);
            try { profile = await UserProfileRepository.Load() ?? profile; }
            catch (Exception e) { { } }
            TextSkeleton.Ready(this);
            if (this == null) return;                       // the scene was left while loading
            ShowProfile(profile);

            try { await ShowRecommendations(); }
            catch (Exception e)
            {
                { }
                if (this != null && recommendationsEmptyText != null)
                {
                    recommendationsEmptyText.gameObject.SetActive(true);
                    recommendationsEmptyText.text = ProductRecommender.IsProfileComplete(profile) ? msgNoneComplete : msgProfileIncomplete;
                }
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
            string Listed(List<string> items, string none) => items != null && items.Count > 0 ? string.Join(", ", items) : none;
            Set(skinTypeFact, p != null && !string.IsNullOrWhiteSpace(p.skinType) ? p.skinType : notSet);
            Set(skinToneFact, p != null && !string.IsNullOrWhiteSpace(p.skinTone) ? p.skinTone : notSet);
            Set(conditionFact, Listed(p?.skinConditions, "None"));
            Set(allergensFact, Listed(p?.knownAllergens, noAllergens));
            if (skinProfileText != null)
            {
                string List(List<string> items, string none) => items != null && items.Count > 0 ? string.Join(", ", items) : none;
                skinProfileText.text = "<b>Skin type:</b> " + (p != null && !string.IsNullOrWhiteSpace(p.skinType) ? p.skinType : notSet)
                    + "\n<b>Skin tone:</b> " + (p != null && !string.IsNullOrWhiteSpace(p.skinTone) ? p.skinTone : notSet)
                    + "\n<b>Chronic skin condition:</b> " + List(p?.skinConditions, "None")
                    + "\n<b>Allergens:</b> " + List(p?.knownAllergens, noAllergens);
            }
        }

        static void Set(TextMeshProUGUI label, string value) { if (label != null) label.text = value; }

        // ---------------------------------------------------------------------------------------
        async System.Threading.Tasks.Task ShowRecommendations()
        {
            foreach (var c in cards) if (c != null) Destroy(c);
            cards.Clear(); tiles.Clear();

            var who = profile ?? new UserProfileData();
            if (!ProductRecommender.IsProfileComplete(who))
            {
                if (recommendationsEmptyText != null)
                {
                    recommendationsEmptyText.gameObject.SetActive(true);
                    recommendationsEmptyText.text = msgProfileIncomplete;
                }
                if (recommendationsNoteText != null) recommendationsNoteText.gameObject.SetActive(false);
                return;
            }

            await ProductRepository.LoadActive();
            await IngredientRepository.LoadAll();
            if (this == null) return;
            int rowLimit = Math.Max(0, maximumRecommendations);
            List<ProductData> products = await ProductRepository.LoadRecommendable(rowLimit + 4);
            foreach (ProductRecommender.Pick pick in ProductRecommender.For(who, products, BrandRepository.All(), rowLimit))
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
                tile.AiClicked += t => { if (aiPanel != null) aiPanel.Show(t.Data as ScanRecord); };   // rows built before 18 Nov 2026
                tile.Clicked += OnResult;                                                              // the "Result" button
                tile.DrawerOpening += CloseOtherDrawers;
                tiles.Add(tile);
                cards.Add(tile.gameObject);
            }

            if (recommendationsEmptyText != null)
            {
                recommendationsEmptyText.gameObject.SetActive(cards.Count == 0);
                recommendationsEmptyText.text = msgNoneComplete;
            }
            // what a recommendation means, and does not mean: said under the heading when there is something to show
            // 12 November 2026: the line is no longer printed under the heading; it is inside the small "i" at the end of "Recommended for you"
            string note = ProductScope.HasSkinCondition(who) ? noteWithCondition : noteStandard;
            if (recommendationsNoteText != null) { recommendationsNoteText.text = note; recommendationsNoteText.gameObject.SetActive(false); }
            if (recommendationsBadge != null) { recommendationsBadge.Message = note; recommendationsBadge.gameObject.SetActive(cards.Count > 0); }
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

        /// <summary>"Result": opens this product's AI Smart Scan Result (already checked against the user's profile); Back returns here.</summary>
        void OnResult(ProductTileView tile)
        {
            if (!(tile.Data is ScanRecord record)) return;
            ScanSession.Current = record;
            ScanSession.ResultBackScene = SceneNames.Dashboard;
            SceneNavigator.Go(SceneNames.AiResult);
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
