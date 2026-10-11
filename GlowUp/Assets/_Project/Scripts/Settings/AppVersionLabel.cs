using TMPro;
using UnityEngine;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// About / Version Number: writes the app's version into the text it sits on. The version is the one
    /// typed in Edit > Project Settings > Player > Version (it also shows correctly in Edit mode).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TMP_Text))]
    public class AppVersionLabel : MonoBehaviour
    {
        [Tooltip("{0} = app name, {1} = version, {2} = platform (Android, iPhone ...), {3} = the Unity version it was built with")]
        [SerializeField] private string format = "{0}  version {1}";
        [Tooltip("A second line under it; leave empty for none. Same {0} to {3}.")]
        [SerializeField] private string secondLine = "Built for {2}";

        void OnEnable() { Apply(); }
        void OnValidate() { Apply(); }

        public void Apply()
        {
            var text = GetComponent<TMP_Text>();
            if (text == null) return;
            string first = Fill(format);
            string second = string.IsNullOrWhiteSpace(secondLine) ? "" : "\n<size=85%>" + Fill(secondLine) + "</size>";
            text.text = first + second;
        }

        string Fill(string pattern)
        {
            try { return string.Format(pattern ?? "", Application.productName, Application.version, Application.platform, Application.unityVersion); }
            catch (System.FormatException) { return pattern; }
        }
    }
}
