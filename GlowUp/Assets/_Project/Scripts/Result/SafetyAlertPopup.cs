using System.Collections;
using System.Collections.Generic;
using System.Text;
using GlowUp.Common;
using GlowUp.Scan;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Result
{
    /// <summary>
    /// Ingredient Safety Alert on the result screen: when the product that was just scanned contains one
    /// of the triggers the user switched on in Settings (their allergens, banned or harmful ingredients,
    /// ingredients that aggravate eczema, psoriasis ...), this pop-up opens by itself as soon as the result
    /// is shown, and the phone vibrates if the user left that on. It shows once per scan.
    /// </summary>
    public class SafetyAlertPopup : MonoBehaviour
    {
        [SerializeField] private OverlayPanel panel;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button okButton;
        [Tooltip("Seconds after the result screen opens before the alert appears")]
        [SerializeField] private float delaySeconds = 0.4f;
        [Tooltip("At most this many ingredient names per line; the rest is counted")]
        [SerializeField] private int namesPerLine = 4;

        [Header("Wording")]
        [SerializeField] private string intro = "This product contains something you asked to be warned about:";
        [Tooltip("{0} = what the analysis found")]
        [SerializeField] private string allergenLine = "<b>Your allergens.</b> {0}";
        [Tooltip("{0} = ingredient names")]
        [SerializeField] private string harmfulLine = "<b>Banned or harmful.</b> {0}";
        [Tooltip("{0} = the skin condition, {1} = ingredient names")]
        [SerializeField] private string conditionLine = "<b>May aggravate {0}.</b> {1}";
        [Tooltip("{0} = how many more")]
        [SerializeField] private string andMore = " and {0} more";
        [SerializeField] private string outro = "Open the ingredient list below for the reasons. If you are unsure, ask a dermatologist before using it.";
        [Tooltip("What each skin-condition word is called on screen, written as word=Name")]
        [SerializeField] private string[] conditionNames =
        {
            "acne=acne", "eczema=eczema", "psoriasis=psoriasis", "rosacea=rosacea",
            "seborrheic=seborrheic dermatitis", "melasma=melasma or dark spots", "contact=contact dermatitis"
        };

        void Awake() { if (okButton != null) okButton.onClick.AddListener(() => { if (panel != null) panel.Hide(); }); }

        IEnumerator Start()
        {
            if (!ScanSession.AlertPending || ScanSession.Alerts == null || ScanSession.Alerts.Count == 0 || panel == null) yield break;
            List<SafetyAlert> alerts = ScanSession.Alerts;
            ScanSession.AlertPending = false;                  // once per scan
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, delaySeconds));

            if (messageText != null) messageText.text = Compose(alerts);
            panel.Show();
            if (SafetyAlertPreferences.Vibrate) Vibrate();
        }

        string Compose(List<SafetyAlert> alerts)
        {
            var text = new StringBuilder(intro).Append("\n");
            foreach (SafetyAlert alert in alerts)
            {
                text.Append("\n");
                if (alert.trigger == SafetyAlert.Notice) text.Append(alert.detail);
                else if (alert.trigger == SafetyAlertPreferences.Allergens) text.AppendFormat(allergenLine, alert.detail);
                else if (alert.trigger == SafetyAlertPreferences.Harmful) text.AppendFormat(harmfulLine, Names(alert.ingredients));
                else text.AppendFormat(conditionLine, ConditionName(alert.trigger), Names(alert.ingredients));
            }
            if (!string.IsNullOrEmpty(outro)) text.Append("\n\n").Append(outro);
            return text.ToString();
        }

        string Names(List<string> names)
        {
            if (names == null || names.Count == 0) return "";
            int shown = Mathf.Min(Mathf.Max(1, namesPerLine), names.Count);
            string joined = string.Join(", ", names.GetRange(0, shown));
            return names.Count > shown ? joined + string.Format(andMore, names.Count - shown) : joined;
        }

        string ConditionName(string word)
        {
            foreach (string pair in conditionNames ?? new string[0])
            {
                int cut = pair.IndexOf('=');
                if (cut > 0 && string.Equals(pair.Substring(0, cut).Trim(), word, System.StringComparison.OrdinalIgnoreCase)) return pair.Substring(cut + 1).Trim();
            }
            return word;
        }

        static void Vibrate()
        {
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }
    }
}
