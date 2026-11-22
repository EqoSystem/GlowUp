using System;
using System.Threading.Tasks;
using Firebase.Auth;
using UnityEngine.Networking;

namespace GlowUp.Security
{
    /// <summary>
    /// Every AI request of the app goes through GlowUp's own server (Vercel function api/app-ai), never straight to the AI
    /// service: the AI key lives only on the server (security guideline 6 and 11, 16 November 2026). The request body is the
    /// same Gemini "generateContent" JSON as before and the server returns the service's answer unchanged, so the readers of
    /// the answers did not change. The server only answers a signed-in GlowUp user: the Firebase sign-in token goes along.
    /// </summary>
    public static class AiProxy
    {
        public const string Endpoint = "https://glowupapp.xyz/api/app-ai";

        /// <summary>The address for one model of the configured model chain.</summary>
        public static string Url(string model) => Endpoint + "?model=" + UnityWebRequest.EscapeURL(model ?? "");

        /// <summary>
        /// Same, with the product words (a name or the barcode digits) the server may look up in GlowUp's MCP tools (Open Beauty Facts,
        /// the FDA Philippines list) before the AI answers. Product words only: never anything from the skin profile. Empty = no look-up.
        /// </summary>
        public static string Url(string model, string lookup) =>
            Url(model) + (string.IsNullOrWhiteSpace(lookup) ? "" : "&lookup=" + UnityWebRequest.EscapeURL(lookup.Trim().Length > 120 ? lookup.Trim().Substring(0, 120) : lookup.Trim()));

        /// <summary>
        /// Adds the sign-in token to the request. False when nobody is signed in with Firebase (for example after an offline
        /// sign-in that has not reached Firebase yet): the AI is then not asked.
        /// </summary>
        public static async Task<bool> Authorize(UnityWebRequest request)
        {
            FirebaseUser user = null;
            try { user = FirebaseAuth.DefaultInstance?.CurrentUser; } catch (Exception) { }
            if (user == null) return false;
            string token = null;
            try { token = await user.TokenAsync(false); } catch (Exception) { }
            if (string.IsNullOrEmpty(token)) return false;
            request.SetRequestHeader("Authorization", "Bearer " + token);
            return true;
        }

        public const string NotSignedIn = "Sign in again to use the AI analysis.";
    }
}
