using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using GlowUp.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace GlowUp.Security
{
    /// <summary>
    /// The 6-digit e-mail code (panel revision, 22 Nov 2026), asked of GlowUp's server (Vercel function api/email-otp). The server mails the code
    /// from a Gmail address and, on a right code, marks the account's e-mail as confirmed. When the server is not set up for it (answer 503), or
    /// there is no connection, nothing is offered and the e-mail link stays the way. The code is never stored in the app.
    /// </summary>
    public static class EmailOtpClient
    {
        public const string Endpoint = "https://glowupapp.xyz/api/email-otp";

        public enum Outcome { Ok, Wrong, Locked, Wait, NotAvailable }

        public static Task<Outcome> Send() => Call("send", null);
        public static Task<Outcome> Verify(string code) => Call("verify", code);

        static async Task<Outcome> Call(string action, string code)
        {
            try
            {
                var sb = new StringBuilder("{\"action\":\"").Append(action).Append("\"");
                string json = null;
                using (var request = new UnityWebRequest(Endpoint, "POST"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.timeout = 20;
                    // the sign-in token goes in the body, the same way the website does it (the server checks it with Firebase)
                    string token = await AiProxy.TokenOrNull();
                    if (string.IsNullOrEmpty(token)) return Outcome.NotAvailable;
                    sb.Append(",\"idToken\":\"").Append(token).Append("\"");
                    if (code != null) sb.Append(",\"code\":\"").Append(new string(Array.FindAll(code.ToCharArray(), char.IsDigit))).Append("\"");
                    sb.Append("}");
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(sb.ToString()));
                    try { await request.SendWebRequest(); } catch (Exception) { return Outcome.NotAvailable; }
                    long status = request.responseCode;
                    json = request.downloadHandler.text;
                    if (status == 200) return Interpret(json);
                    if (status == 429) return json != null && json.Contains("locked") ? Outcome.Locked : Outcome.Wait;
                    return Outcome.NotAvailable;                        // 503 not set up, 401, 5xx: the link is the way
                }
            }
            catch (Exception) { return Outcome.NotAvailable; }
        }

        /// <summary>The server's 200 answer: {"ok":true} = right (or already confirmed), {"ok":false} = a wrong code.</summary>
        public static Outcome Interpret(string json)
        {
            if (string.IsNullOrEmpty(json)) return Outcome.NotAvailable;
            return json.Replace(" ", "").Contains("\"ok\":true") ? Outcome.Ok : Outcome.Wrong;
        }
    }
}
