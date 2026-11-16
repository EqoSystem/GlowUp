using GlowUp.Common;
using GlowUp.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>Step 2 - skin type tile (required) and chronic skin conditions (optional).</summary>
    public class OnboardingStep2FlowController : MonoBehaviour
    {
        [SerializeField] private TileSelectionGroup skinTypeGroup;
        [SerializeField] private ChecklistPanel conditions;
        [SerializeField] private Button continueButton;
        [SerializeField] private ContinueButtonGlowAnimator continueAnimator;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI feedbackText;
        [SerializeField] private string nextScene = SceneNames.Onboarding3;
        [SerializeField] private string previousScene = SceneNames.Onboarding1;
        [SerializeField] private string msgChooseSkinType = "Please choose your skin type to continue.";

        void Awake()
        {
            Calm();
            if (skinTypeGroup != null) skinTypeGroup.Changed += _ => Refresh();
            if (conditions != null) conditions.CustomAdded += t => { if (!OnboardingSession.CustomConditions.Contains(t)) OnboardingSession.CustomConditions.Add(t); };
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            if (backButton != null) backButton.onClick.AddListener(GoBack);
        }

        /// <summary>
        /// This page is calm (10 November 2026): no square sweeps, nothing that keeps pulsing. A skin type card turns over when chosen to
        /// say how to tell that type; a condition row, Add and Continue only give an ordinary press feedback.
        /// </summary>
        void Calm()
        {
            TMP_Text font = feedbackText;
            foreach (SelectableTile tile in GetComponentsInScene<SelectableTile>())
            {
                if (!SkinTypeHints.Back.ContainsKey(tile.Value)) continue;
                tile.Flip = FlipCardTile.AddTo(tile, font);
            }
            foreach (PixelSweepTileAnimator a in GetComponentsInScene<PixelSweepTileAnimator>()) a.Calm();
            foreach (PressSweepEffect e in GetComponentsInScene<PressSweepEffect>()) e.Calm();
            foreach (ChecklistRowAnimator r in GetComponentsInScene<ChecklistRowAnimator>()) r.Calm();
            foreach (ContinueButtonGlowAnimator c in GetComponentsInScene<ContinueButtonGlowAnimator>()) c.Calm();
        }

        static T[] GetComponentsInScene<T>() where T : Object => FindObjectsOfType<T>(true);

        /// <summary>
        /// Back: Step 1 for a new account. For a Google / Facebook account, and while the quiz is retaken from Settings,
        /// this is the first page, so Back leaves: the user is asked first, and what happens then is in OnboardingLeaveGuard
        /// (a retake returns to Settings with the saved profile untouched; a Google / Facebook account is signed out again).
        /// </summary>
        void GoBack()
        {
            if (OnboardingSession.Retake && OnboardingSession.RetakeWithDetails) SceneNavigator.Go(previousScene);     // New17: the full retake has Step 1 before this page
            else if (OnboardingSession.Retake || OnboardingSession.Social) OnboardingLeaveGuard.RequestLeave();
            else SceneNavigator.Go(previousScene);
        }

        void Start()
        {
            OnboardingSession.LoadDraft();
            // the skin conditions on offer are the admin website's list (the built-in list while it cannot be reached)
            ListsRepository.Changed += ApplyLists;
            ApplyLists();
            _ = ListsRepository.Refresh();
            if (skinTypeGroup != null && !string.IsNullOrEmpty(OnboardingSession.SkinType)) skinTypeGroup.Select(OnboardingSession.SkinType, false);
            if (conditions != null) conditions.SetSelected(OnboardingSession.SkinConditions);
            // the Google notice is a banner at the top of the page (New13 item 5C); the line under Add holds that row's own hints only
            OnboardingNotices.OneLine(feedbackText);
            if (feedbackText != null) feedbackText.text = "";
            OnboardingNotices.SocialBanner(feedbackText, "step2", feedbackText);
            Refresh();
        }

        void OnDestroy() { ListsRepository.Changed -= ApplyLists; }

        void ApplyLists()
        {
            if (this != null && conditions != null) conditions.ApplyOptions(ListsRepository.ConditionLabels());
            if (this != null) Calm();                // the condition rows are made from the list: they are calmed once they exist
        }

        void Refresh()
        {
            bool ready = skinTypeGroup != null && !string.IsNullOrEmpty(skinTypeGroup.Value);
            if (continueAnimator != null) continueAnimator.SetReady(ready);
            else if (continueButton != null) continueButton.interactable = ready;
        }

        void OnContinue()
        {
            if (skinTypeGroup == null || string.IsNullOrEmpty(skinTypeGroup.Value)) { OnboardingNotices.Error(feedbackText, msgChooseSkinType); return; }
            OnboardingSession.SkinType = skinTypeGroup.Value;
            OnboardingSession.SkinConditions = conditions != null ? conditions.Selected : new System.Collections.Generic.List<string>();
            OnboardingSession.SaveDraft();
            SceneNavigator.Go(nextScene);
        }
    }
}
