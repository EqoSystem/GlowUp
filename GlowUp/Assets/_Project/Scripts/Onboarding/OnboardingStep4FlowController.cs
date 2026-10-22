using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Step 4 - known skin allergens and sensitivities.
    /// The user cannot continue until they have answered: at least one allergen or sensitivity
    /// (ticked in the list, or added through "Not listed? Add it here"), or the "No known allergens"
    /// row. There is no way to skip this step.
    /// "No known allergens" is saved in the profile as that text, so the profile shows the user did
    /// answer; the scan analysis knows it is not an allergen to look for.
    /// </summary>
    public class OnboardingStep4FlowController : MonoBehaviour
    {
        [SerializeField] private ChecklistPanel allergens;
        [SerializeField] private Button continueButton;
        [SerializeField] private ContinueButtonGlowAnimator continueAnimator;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI feedbackText;
        [SerializeField] private string nextScene = SceneNames.Onboarding5;
        [SerializeField] private string previousScene = SceneNames.Onboarding3;
        [Tooltip("Shown while nothing is selected")]
        [SerializeField] private string msgChooseOne = "Select at least one, or tick \"No known allergens\", to continue.";

        void Awake()
        {
            if (allergens != null)
            {
                allergens.Changed += Refresh;
                allergens.CustomAdded += t => { if (!OnboardingSession.CustomAllergens.Contains(t)) OnboardingSession.CustomAllergens.Add(t); };
            }
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            if (backButton != null) backButton.onClick.AddListener(() => SceneNavigator.Go(previousScene));
        }

        void Start()
        {
            OnboardingSession.LoadDraft();
            // the allergen families on offer are the admin website's list (the built-in list while it cannot be reached)
            ListsRepository.Changed += ApplyLists;
            ApplyLists();
            _ = ListsRepository.Refresh();
            if (allergens != null)
            {
                // coming back to this step: show the answer given before
                var saved = new UserProfileData { knownAllergens = new List<string>(OnboardingSession.Allergens) };
                if (saved.SaysNoKnownAllergens()) allergens.SelectNone();
                else allergens.SetSelected(saved.RealAllergens());
            }
            if (feedbackText != null) feedbackText.text = "";
            Refresh();
        }

        void OnDestroy() { ListsRepository.Changed -= ApplyLists; }

        void ApplyLists()
        {
            if (this != null && allergens != null) allergens.ApplyOptions(ListsRepository.AllergenLabels());
        }

        /// <summary>True once the user has answered: an allergen / sensitivity, or "No known allergens".</summary>
        bool Ready => allergens != null && allergens.HasAnswer;

        void Refresh()
        {
            bool ready = Ready;
            if (continueAnimator != null) continueAnimator.SetReady(ready);
            else if (continueButton != null) continueButton.interactable = ready;

            // say why Continue is locked; clear that message (and only that one) as soon as there is an answer
            if (feedbackText == null) return;
            if (!ready) feedbackText.text = msgChooseOne;
            else if (feedbackText.text == msgChooseOne) feedbackText.text = "";
        }

        void OnContinue()
        {
            if (!Ready) { if (feedbackText != null) feedbackText.text = msgChooseOne; return; }
            OnboardingSession.Allergens = allergens.NoneSelected
                ? new List<string> { UserProfileData.NoKnownAllergens }
                : allergens.Selected;
            OnboardingSession.SaveDraft();
            if (OnboardingSession.Retake) { SaveRetake(); return; }
            SceneNavigator.Go(nextScene);
        }

        /// <summary>
        /// The quiz was retaken from the Settings page by a signed-in user: the new answers replace the skin
        /// part of their profile (on this phone at once, in the database now or as soon as they are online).
        /// </summary>
        async void SaveRetake()
        {
            if (savingRetake) return;
            savingRetake = true;
            if (continueButton != null) continueButton.interactable = false;
            string returnScene = string.IsNullOrEmpty(OnboardingSession.RetakeReturnScene) ? SceneNames.Dashboard : OnboardingSession.RetakeReturnScene;
            try
            {
                UserProfileData profile = await UserProfileRepository.Load() ?? new UserProfileData { uid = UserProfileRepository.ActiveUid };
                profile.skinType = OnboardingSession.SkinType;
                profile.skinTone = OnboardingSession.SkinTone;
                profile.skinConditions = new List<string>(OnboardingSession.SkinConditions);
                profile.knownAllergens = new List<string>(OnboardingSession.Allergens);
                await UserProfileRepository.Save(profile);

                // items the user typed that were not on the lists go to the admin website for review, as at sign-up
                string uid = UserProfileRepository.ActiveUid;
                if (!string.IsNullOrEmpty(uid))
                {
                    long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    foreach (string c in OnboardingSession.CustomConditions)
                        SyncQueue.EnqueueSet("conditionSuggestions/" + System.Guid.NewGuid().ToString("N"), new SuggestionRecord { text = c, uid = uid, status = "pending", createdAtUnix = now });
                    foreach (string a in OnboardingSession.CustomAllergens)
                        SyncQueue.EnqueueSet("allergenSuggestions/" + System.Guid.NewGuid().ToString("N"), new SuggestionRecord { text = a, uid = uid, status = "pending", createdAtUnix = now });
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Retake: the profile was not saved: " + e.Message); }
            OnboardingSession.EndRetake();
            SceneNavigator.Go(returnScene);
        }

        bool savingRetake;
    }
}
