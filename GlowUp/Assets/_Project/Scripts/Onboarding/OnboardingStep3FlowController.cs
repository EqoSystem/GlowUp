using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>Step 3 - skin tone tile (required).</summary>
    public class OnboardingStep3FlowController : MonoBehaviour
    {
        [SerializeField] private TileSelectionGroup skinToneGroup;
        [SerializeField] private Button continueButton;
        [SerializeField] private ContinueButtonGlowAnimator continueAnimator;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI feedbackText;
        [SerializeField] private string nextScene = SceneNames.Onboarding4;
        [SerializeField] private string previousScene = SceneNames.Onboarding2;
        [SerializeField] private string msgChooseTone = "Please choose your skin tone to continue.";

        void Awake()
        {
            if (skinToneGroup != null) skinToneGroup.Changed += _ => Refresh();
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            if (backButton != null) backButton.onClick.AddListener(() => SceneNavigator.Go(previousScene));
        }

        void Start()
        {
            OnboardingSession.LoadDraft();
            if (skinToneGroup != null && !string.IsNullOrEmpty(OnboardingSession.SkinTone)) skinToneGroup.Select(OnboardingSession.SkinTone, false);
            if (feedbackText != null) feedbackText.text = OnboardingSession.Social ? OnboardingSession.SocialReminder : "";
            Refresh();
        }

        void Refresh()
        {
            bool ready = skinToneGroup != null && !string.IsNullOrEmpty(skinToneGroup.Value);
            if (continueAnimator != null) continueAnimator.SetReady(ready);
            else if (continueButton != null) continueButton.interactable = ready;
        }

        void OnContinue()
        {
            if (skinToneGroup == null || string.IsNullOrEmpty(skinToneGroup.Value)) { if (feedbackText != null) feedbackText.text = msgChooseTone; return; }
            OnboardingSession.SkinTone = skinToneGroup.Value;
            OnboardingSession.SaveDraft();
            SceneNavigator.Go(nextScene);
        }
    }
}
