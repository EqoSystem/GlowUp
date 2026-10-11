using System.Collections.Generic;
using UnityEngine;

namespace GlowUp.Scanning
{
    [CreateAssetMenu(fileName = "GeminiApiConfig", menuName = "GlowUp/Gemini Api Config")]
    public class GeminiApiConfig : ScriptableObject
    {
        [Header("Model chain (tried in order; falls forward on 404 / model-not-found)")]
        [Tooltip("First entry is the primary model. Add older/cheaper models after it as fallbacks.")]
        public List<string> ModelChain = new List<string> { "gemini-3.6-flash", "gemini-3.5-flash" };

        [Header("Endpoint")]
        public string ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
        [Tooltip(":generateContent is appended automatically per model.")]
        public string GenerateContentSuffix = ":generateContent";

        [Header("Timeouts & Retries")]
        [Tooltip("Seconds before a single attempt is abandoned as a timeout.")]
        public int TimeoutSeconds = 20;
        [Tooltip("Retries PER MODEL in the chain (not counting the initial attempt).")]
        public int MaxRetriesPerModel = 2;
        [Tooltip("Base delay for exponential backoff, in seconds. Doubles each retry, plus jitter.")]
        public float BaseBackoffSeconds = 1.5f;
        [Tooltip("Hard ceiling on any single backoff wait, regardless of attempt count.")]
        public float MaxBackoffSeconds = 12f;

        public string BuildUrl(string model) => $"{ApiBaseUrl}{model}{GenerateContentSuffix}";
    }
}