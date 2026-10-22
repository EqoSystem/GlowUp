using GlowUp.Performance;
using GlowUp.Views;
using TMPro;
using UnityEngine;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// AR Try-On quality on the Settings page: three tabs (Automatic, High, Lighter) and the line that says what this phone
    /// gets. What each pipeline does is explained on ArQualitySettings and set in two places: the limits for Automatic in the
    /// PerformanceProfile asset (Assets/_Project/Resources), the numbers of each pipeline on the "AR Quality Controller"
    /// object of the AR Try-On scene.
    /// </summary>
    public class ArQualitySetting : MonoBehaviour
    {
        [Tooltip("Tab 0 = Automatic, tab 1 = High, tab 2 = Lighter")]
        [SerializeField] private ChoiceTabs tabs;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Wording")]
        [Tooltip("{0} = what this phone is, {1} = what it gets, {2} = why")]
        [SerializeField] private string statusFormat = "This phone: {0}. AR Try-On uses the {1} pipeline{2}.";
        [SerializeField] private string fullName = "full";
        [SerializeField] private string lightName = "lighter";
        [SerializeField] private string whyAutomatic = " (chosen for it automatically)";
        [SerializeField] private string whySlow = " (it could not keep up with the full one)";
        [SerializeField] private string whyChosen = " (your choice)";
        [SerializeField] private string fullEffect = "Dense face tracking, soft blending, light that follows the room, and make-up that stays off your teeth.";
        [SerializeField] private string lightEffect = "Smaller picture, one colour at a time, a steady 30 frames per second: smooth and cool on budget phones.";

        void Awake()
        {
            if (tabs != null) tabs.Changed += index => ArQualitySettings.SetChoice((ArQualityChoice)Mathf.Clamp(index, 0, 2));
        }

        void OnEnable() { ArQualitySettings.Changed += Refresh; }
        void OnDisable() { ArQualitySettings.Changed -= Refresh; }
        void Start() { Refresh(); }

        void Refresh()
        {
            ArQualitySettings.Initialise();
            if (tabs != null) tabs.Select((int)ArQualitySettings.Choice);
            if (statusText == null) return;
            ArTier tier = ArQualitySettings.Tier;
            string why = ArQualitySettings.Choice != ArQualityChoice.Automatic ? whyChosen : ArQualitySettings.FoundSlow ? whySlow : whyAutomatic;
            statusText.text = string.Format(statusFormat, ArQualitySettings.DescribeDevice(), tier == ArTier.Full ? fullName : lightName, why)
                              + "\n" + (tier == ArTier.Full ? fullEffect : lightEffect);
        }
    }
}
