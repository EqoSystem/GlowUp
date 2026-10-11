using GlowUp.Common;
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
            if (skinTypeGroup != null) skinTypeGroup.Changed += _ => Refresh();
            if (conditions != null) conditions.CustomAdded += t => { if (!OnboardingSession.CustomConditions.Contains(t)) OnboardingSession.CustomConditions.Add(t); };
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            if (backButton != null) backButton.onClick.AddListener(GoBack);
        }

        /// <summary>Back: Step 1 for a new account; the Settings page when the quiz is being retaken (nothing is changed).</summary>
        void GoBack()
        {
            if (OnboardingSession.Retake && !string.IsNullOrEmpty(OnboardingSession.RetakeReturnScene))
            {
                string scene = OnboardingSession.RetakeReturnScene;
                OnboardingSession.EndRetake();
                SceneNavigator.Go(scene);
            }
            else if (OnboardingSession.Social)
            {
                // signed in with Google / Facebook and backing out of the quiz: sign out, or the login page would send them straight back here
                GlowUp.Data.UserProfileRepository.SignOut();
                OnboardingSession.Clear();
                SceneNavigator.Go(SceneNames.Login);
            }
            else SceneNavigator.Go(previousScene);
        }

        void Start()
        {
            OnboardingSession.LoadDraft();
            if (skinTypeGroup != null && !string.IsNullOrEmpty(OnboardingSession.SkinType)) skinTypeGroup.Select(OnboardingSession.SkinType, false);
            if (conditions != null) conditions.SetSelected(OnboardingSession.SkinConditions);
            if (feedbackText != null) feedbackText.text = "";
            Refresh();
        }

        void Refresh()
        {
            bool ready = skinTypeGroup != null && !string.IsNullOrEmpty(skinTypeGroup.Value);
            if (continueAnimator != null) continueAnimator.SetReady(ready);
            else if (continueButton != null) continueButton.interactable = ready;
        }

        void OnContinue()
        {
            if (skinTypeGroup == null || string.IsNullOrEmpty(skinTypeGroup.Value)) { if (feedbackText != null) feedbackText.text = msgChooseSkinType; return; }
            OnboardingSession.SkinType = skinTypeGroup.Value;
            OnboardingSession.SkinConditions = conditions != null ? conditions.Selected : new System.Collections.Generic.List<string>();
            OnboardingSession.SaveDraft();
            SceneNavigator.Go(nextScene);
        }
    }
}
