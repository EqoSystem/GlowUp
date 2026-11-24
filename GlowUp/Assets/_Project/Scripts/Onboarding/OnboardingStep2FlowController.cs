using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Scan;
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
        [Header("Help on the page (panel revision, 22 Nov 2026)")]
        [Tooltip("Not sure? Find your type: three questions that choose the skin type (SkinTypeFinder)")]
        [SerializeField] private Button finderButton;
        [Header("How does your skin react? (optional, Sensitive skin only; 24 Nov 2026)")]
        [Tooltip("Opens the panel again; shown only while Sensitive is chosen")]
        [SerializeField] private Button reactionsButton;
        [SerializeField] private OverlayPanel reactionsPanel;
        [Tooltip("One row per reaction, in the order of SensitiveReactions.All")]
        [SerializeField] private Button[] reactionRows;
        [Tooltip("The tick drawn on a row while its reaction is chosen")]
        [SerializeField] private GameObject[] reactionTicks;
        [SerializeField] private Button reactionsDone;
        [SerializeField] private Button reactionsSkip;

        void Awake()
        {
            Calm();
            if (skinTypeGroup != null) skinTypeGroup.Changed += OnSkinTypeChanged;
            if (conditions != null) conditions.CustomAdded += t => { if (!OnboardingSession.CustomConditions.Contains(t)) OnboardingSession.CustomConditions.Add(t); };
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            if (backButton != null) backButton.onClick.AddListener(GoBack);
            if (finderButton != null) finderButton.onClick.AddListener(OpenFinder);
            if (reactionsButton != null) reactionsButton.onClick.AddListener(OpenReactions);
            if (reactionsDone != null) reactionsDone.onClick.AddListener(CloseReactions);
            if (reactionsSkip != null) reactionsSkip.onClick.AddListener(() => { picked.Clear(); PaintReactions(); CloseReactions(); });
            for (int i = 0; reactionRows != null && i < reactionRows.Length && i < SensitiveReactions.All.Length; i++)
            {
                string id = SensitiveReactions.All[i].id;
                if (reactionRows[i] != null) reactionRows[i].onClick.AddListener(() => { if (!picked.Remove(id)) picked.Add(id); PaintReactions(); });
            }
        }

        // ---- "How does your skin react?": optional, only for Sensitive skin. Opens once by itself when Sensitive is chosen; the button beside "Find my skin type" opens it again. ----
        readonly System.Collections.Generic.List<string> picked = new System.Collections.Generic.List<string>();
        bool askedThisChoice;

        void OnSkinTypeChanged(string value)
        {
            Refresh();
            bool sensitive = string.Equals(value, "Sensitive", System.StringComparison.OrdinalIgnoreCase);
            ShowReactionsButton(sensitive);
            if (!sensitive) { askedThisChoice = false; return; }
            if (!askedThisChoice && picked.Count == 0) { askedThisChoice = true; OpenReactions(); }
        }

        RectTransform finderRect;

        /// <summary>"Find my skin type" shares its row with "How it reacts" (Sensitive only); alone it spans the whole row.</summary>
        void ShowReactionsButton(bool on)
        {
            if (reactionsButton != null) reactionsButton.gameObject.SetActive(on);
            if (finderButton == null) return;
            if (finderRect == null) finderRect = (RectTransform)finderButton.transform;
            // the whole row by default (the scene is built that way); the left half while "How it reacts" takes the right half
            finderRect.anchorMax = new Vector2(on ? 0.5f : 1f, finderRect.anchorMax.y);
            finderRect.offsetMax = new Vector2(on ? -8f : -finderRect.offsetMin.x, finderRect.offsetMax.y);
        }

        void OpenReactions()
        {
            if (reactionsPanel == null) return;
            PaintReactions();
            reactionsPanel.Show();
        }

        void CloseReactions()
        {
            OnboardingSession.SensitiveReactions = SensitiveReactions.Clean(picked);
            OnboardingSession.SaveDraft();
            if (reactionsPanel != null) reactionsPanel.Hide();
        }

        void PaintReactions()
        {
            for (int i = 0; reactionTicks != null && i < reactionTicks.Length && i < SensitiveReactions.All.Length; i++)
                if (reactionTicks[i] != null) reactionTicks[i].SetActive(picked.Contains(SensitiveReactions.All[i].id));
        }

        /// <summary>Three questions, one per page of the sheet; the result selects the matching tile (the user can still pick another).</summary>
        void OpenFinder()
        {
            ChoiceSheet sheet = ChoiceSheet.Get(finderButton.transform);
            if (sheet == null) return;
            var answers = new System.Collections.Generic.List<int>();
            void Ask(int q)
            {
                if (q >= SkinTypeFinder.Questions.Length)
                {
                    string type = SkinTypeFinder.Result(answers);
                    sheet.Show("Your skin type", SkinTypeFinder.Explain(type), new System.Collections.Generic.List<ChoiceSheet.Choice>
                    {
                        new ChoiceSheet.Choice("Use " + SkinTypeNames.Display(type), () => { if (skinTypeGroup != null) skinTypeGroup.Select(type, true); sheet.Hide(); Refresh(); }, true),
                        new ChoiceSheet.Choice("Answer again", () => { answers.Clear(); Ask(0); })
                    }, SkinTypeFinder.SourceNote);
                    return;
                }
                SkinTypeFinder.Question question = SkinTypeFinder.Questions[q];
                var choices = new System.Collections.Generic.List<ChoiceSheet.Choice>();
                for (int a = 0; a < question.answers.Length; a++)
                {
                    int pick = a;
                    choices.Add(new ChoiceSheet.Choice(question.answers[a], () => { while (answers.Count > q) answers.RemoveAt(answers.Count - 1); answers.Add(pick); Ask(q + 1); }));
                }
                if (q > 0) choices.Add(new ChoiceSheet.Choice("Back to the previous question", () => { answers.RemoveAt(answers.Count - 1); Ask(q - 1); }));
                sheet.Show($"Question {q + 1} of {SkinTypeFinder.Questions.Length}", question.text, choices, "General guidance, not a diagnosis.");
            }
            Ask(0);
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
            picked.Clear(); picked.AddRange(SensitiveReactions.Clean(OnboardingSession.SensitiveReactions));
            PaintReactions();
            ShowReactionsButton(string.Equals(OnboardingSession.SkinType, "Sensitive", System.StringComparison.OrdinalIgnoreCase));
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
            OnboardingSession.SensitiveReactions = string.Equals(skinTypeGroup.Value, "Sensitive", System.StringComparison.OrdinalIgnoreCase) ? SensitiveReactions.Clean(picked) : new System.Collections.Generic.List<string>();
            OnboardingSession.SaveDraft();
            SceneNavigator.Go(nextScene);
        }
    }
}
