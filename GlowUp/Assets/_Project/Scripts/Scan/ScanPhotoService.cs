using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Firebase.Auth;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace GlowUp.Scan
{
    /// <summary>
    /// Sends ONE photo of a pack to GlowUp's own server function (api/scan-snapshot.js), which asks the AI with a web search and
    /// answers with separate fields. The AI key is on the server only; this phone holds none for this path. Never throws.
    /// The server checks the sign-in, the cool-down (30 s), 5 photos a day per account and a daily cap for everybody.
    /// </summary>
    public static class ScanPhotoService
    {
        public enum Outcome { Ok, NotFound, NotSetUp, Offline, SignIn, Cooldown, DailyLimit, Busy, Down }

        public class Reading
        {
            public Outcome outcome = Outcome.Down;
            public string brand = "", productName = "", variant = "", size = "", cosmeticType = "", shadeHex = "", model = "";
            public float confidence;
            public List<string> ingredients = new List<string>();
            public List<string> sourceUrls = new List<string>();
            public bool webSources;
        }

        /// <summary>The address of the function, set by ScanSnapshotController (https only).</summary>
        public static string FunctionUrl = "";
        public static float TimeoutSeconds = 45f;

        public static async Task<Reading> Read(byte[] jpeg)
        {
            var result = new Reading();
            if (string.IsNullOrEmpty(FunctionUrl) || !FunctionUrl.StartsWith("https://", StringComparison.Ordinal)) { result.outcome = Outcome.NotSetUp; return result; }
            if (jpeg == null || jpeg.Length == 0) return result;
            if (!ConnectivityMonitor.Online) { result.outcome = Outcome.Offline; return result; }
            try
            {
                if (!await FirebaseBootstrap.Ready) { result.outcome = Outcome.Down; return result; }
                FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;
                if (user == null) { result.outcome = Outcome.SignIn; return result; }
                string token = await user.TokenAsync(false).OrTimeout(8f, "Getting the sign-in token");

                string body = "{\"image\":\"" + Convert.ToBase64String(jpeg) + "\"}";
                using (var request = new UnityWebRequest(FunctionUrl, "POST"))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Authorization", "Bearer " + token);
                    request.timeout = Mathf.Max(5, Mathf.RoundToInt(TimeoutSeconds));
                    await request.SendWebRequest();                          // ONE request, never repeated

                    long status = request.responseCode;
                    Dictionary<string, object> json = AiResearchClient.JsonObjectIn(request.downloadHandler != null ? request.downloadHandler.text : "");
                    if (status == 401) { result.outcome = Outcome.SignIn; return result; }
                    if (status == 429)
                    {
                        string code = Str(json, "code");
                        result.outcome = code == "cooldown" ? Outcome.Cooldown : code == "daily" ? Outcome.DailyLimit : Outcome.Busy;
                        return result;
                    }
                    if (request.result != UnityWebRequest.Result.Success || status != 200 || json == null) return result;     // Down

                    result.model = Str(json, "model");
                    result.webSources = json.TryGetValue("webSources", out object w) && w is bool b && b;
                    if (!(json.TryGetValue("draft", out object d) && d is Dictionary<string, object> draft) || !(draft.TryGetValue("found", out object f) && f is bool found && found))
                    { result.outcome = Outcome.NotFound; return result; }
                    result.brand = Str(draft, "brand"); result.productName = Str(draft, "productName"); result.variant = Str(draft, "variant");
                    result.size = Str(draft, "size"); result.cosmeticType = Str(draft, "cosmeticType"); result.shadeHex = Str(draft, "shadeHex");
                    result.confidence = draft.TryGetValue("confidence", out object c) && c is IConvertible ? Mathf.Clamp01(Convert.ToSingle(c)) : 0f;
                    result.ingredients = Strings(draft, "ingredients", 80, 120);
                    // only https links go on (the server already cleaned them; the phone does not trust that)
                    foreach (string url in Strings(json, "sourceUrls", 5, 300)) if (url.StartsWith("https://", StringComparison.Ordinal)) result.sourceUrls.Add(url);
                    result.outcome = Outcome.Ok;
                    return result;
                }
            }
            catch (Exception e)
            {
                TaskLimits.NoteFailure(e);
                Debug.LogWarning("Photo reading failed: " + e.Message);
                return new Reading();                                         // Down
            }
        }

        static string Str(Dictionary<string, object> json, string key) =>
            json != null && json.TryGetValue(key, out object v) && v is string s ? s.Trim() : "";

        static List<string> Strings(Dictionary<string, object> json, string key, int most, int longest)
        {
            var list = new List<string>();
            if (json == null || !json.TryGetValue(key, out object raw) || !(raw is List<object> items)) return list;
            foreach (object item in items)
            {
                string s = (item as string ?? "").Trim();
                if (s.Length == 0 || s.Length > longest || list.Count >= most) continue;
                list.Add(s);
            }
            return list;
        }
    }
}
