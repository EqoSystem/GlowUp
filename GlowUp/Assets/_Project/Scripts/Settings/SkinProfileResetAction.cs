using System;
using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Skin Profile Reset / Retake Onboarding on the Settings page. It shows the skin profile saved now
    /// and, after the user confirms, opens the skin quiz again (skin type and conditions, skin tone,
    /// allergens) with every answer empty. When the quiz is finished the new answers replace the old
    /// ones and the user comes back here; leaving the quiz with Back changes nothing.
    /// </summary>
    public class SkinProfileResetAction : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI currentProfileText;
        [SerializeField] private Button retakeButton;
        [SerializeField] private ConfirmDialog confirm;

        [Header("Behaviour")]
        [Tooltip("Off (safer): the saved skin profile stays until the quiz is finished, so leaving the quiz half-way loses nothing.\nOn: the saved skin type, tone, conditions and allergens are cleared the moment the user confirms.")]
        [SerializeField] private bool clearSavedProfileBeforeQuiz = false;
        [Tooltip("The first scene of the quiz")]
        [SerializeField] private string quizScene = SceneNames.Onboarding2;
        [Tooltip("Where the user lands after the quiz")]
        [SerializeField] private string returnScene = SceneNames.Settings;

        [Header("Wording")]
        [Tooltip("{0} = skin type, {1} = skin tone, {2} = number of skin conditions, {3} = number of allergens")]
        [SerializeField] private string profileFormat = "Skin type: {0}\nSkin tone: {1}\nSkin conditions: {2}   |   Allergens: {3}";
        [SerializeField] private string notSet = "not set";
        [SerializeField] private string noneSaved = "none";
        [SerializeField] private string confirmTitle = "Retake the skin quiz?";
        [SerializeField] private string confirmMessage = "You go through Steps 1 to 5 again with your current answers filled in. Change what you like; your email and password stay locked.\n\nOn the last step, Save edits keeps the changes and Cancel discards them.";
        [SerializeField] private string confirmMessageWhenClearing = "Your saved skin type, skin tone, skin conditions and allergens are cleared now and the quiz starts empty.\n\nYour name, email, birthday and scan history are not changed.";
        [SerializeField] private string confirmButtonText = "Retake Quiz";

        UserProfileData profile;

        void Awake() { if (retakeButton != null) retakeButton.onClick.AddListener(Ask); }

        async void Start()
        {
            // what this phone already knows is shown at once; only with nothing to show is the profile line a bar meanwhile
            profile = UserProfileRepository.Cached();
            Show(profile);
            if (profile == null) GlowUp.Common.TextSkeleton.Waiting(this);
            try { profile = await UserProfileRepository.Load() ?? profile; }
            catch (Exception e) { { } }
            GlowUp.Common.TextSkeleton.Ready(this);
            if (this == null) return;
            Show(profile);
        }

        void Show(UserProfileData p)
        {
            if (currentProfileText == null) return;
            string type = p != null && !string.IsNullOrWhiteSpace(p.skinType) ? p.skinType : notSet;
            string tone = p != null && !string.IsNullOrWhiteSpace(p.skinTone) ? p.skinTone : notSet;
            int conditions = p != null && p.skinConditions != null ? p.skinConditions.Count : 0;
            int allergens = p != null ? p.RealAllergens().Count : 0;
            currentProfileText.text = string.Format(profileFormat, type, tone, conditions == 0 ? noneSaved : conditions.ToString(), allergens == 0 ? noneSaved : allergens.ToString());
        }

        void Ask()
        {
            string message = clearSavedProfileBeforeQuiz ? confirmMessageWhenClearing : confirmMessage;
            if (confirm != null) confirm.Ask(confirmTitle, message, confirmButtonText, Retake);
            else Retake();
        }

        async void Retake()
        {
            if (clearSavedProfileBeforeQuiz && profile != null)
            {
                profile.skinType = ""; profile.skinTone = "";
                profile.skinConditions = new List<string>(); profile.knownAllergens = new List<string>();
                // the four skin fields only: the account itself is never written from here (7 November 2026)
                try { await UserProfileRepository.SaveSkinProfile("", "", new List<string>(), new List<string>()); }
                catch (Exception e) { { } }
            }
            // New17: the retake goes through Steps 1 to 5 with the saved answers filled in (email and password locked), and ends with
            // "Save edits" / "Cancel" on Step 5. With the saved profile cleared above, the quiz-only retake is kept (nothing to edit).
            if (!clearSavedProfileBeforeQuiz && profile != null)
            {
                OnboardingSession.BeginRetakeWith(profile, returnScene);
                SceneNavigator.Go(SceneNames.Onboarding1);
                return;
            }
            OnboardingSession.BeginRetake(returnScene);
            SceneNavigator.Go(quizScene);
        }
    }
}
