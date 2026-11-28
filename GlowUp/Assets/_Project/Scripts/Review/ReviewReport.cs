using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Review
{
    /// <summary>
    /// One tap from the scan screen (28 November 2026): a product the photo look-up could not find goes to the admins as a
    /// "New product" support report, the same kind Send for Review writes. The website shows it at once (live) in the Brand &amp;
    /// Product Inventory's Review queue and in the admin bell. Text only: the barcode and the words read on the pack. No photo
    /// and nothing about the user's skin profile leaves the phone. The document id comes from the user and what was read, so
    /// sending the same pack twice never makes two reports. Offline, Firestore keeps the write and sends it later.
    /// </summary>
    public static class ReviewReport
    {
        public enum Outcome { Sent, Queued, SignedOut, Failed }
        static readonly HashSet<string> SentIds = new HashSet<string>();

        public static async Task<Outcome> Send(string barcode, string readText, float waitSeconds = 12f)
        {
            FirebaseUser user = null;
            try { if (await FirebaseBootstrap.Ready) user = FirebaseAuth.DefaultInstance.CurrentUser; } catch (Exception) { }
            if (user == null) return Outcome.SignedOut;

            barcode = (barcode ?? "").Trim();
            string text = Limit(System.Text.RegularExpressions.Regex.Replace(readText ?? "", @"\s+", " ").Trim(), 600);
            string name = Limit(text, 100);
            if (barcode.Length == 0 && name.Length == 0) name = "Unknown product (photo)";      // the rules want a barcode or a name
            string id = "scan_" + Hash(user.UserId + "|" + barcode + "|" + text.ToLowerInvariant());
            string title = barcode.Length > 0 ? "barcode " + barcode : "a pack the scanner could not find";
            var report = new Dictionary<string, object>
            {
                { "uid", user.UserId },
                { "userName", SendForReviewController.WhoSent(user) },
                { "category", SendForReviewController.Category },
                { "message", "New product from AI Smart Scan: " + title + "." },
                { "status", "new" },
                { "appVersion", Limit(Application.version ?? "", 40) },
                { "device", Limit($"{SystemInfo.deviceModel}, {SystemInfo.operatingSystem}".Trim(), 200) },
                { "createdAtUnix", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "createdAt", FieldValue.ServerTimestamp },
                { "barcode", barcode },
                { "brand", "" },
                { "productName", name },
                { "ingredientText", "" },
                { "note", text.Length > 0 ? Limit("Words read on the pack: " + text, 500) : "" }
            };
            if (SentIds.Contains(id)) return Outcome.Sent;              // the same pack again: the first report stands (an update would be refused)
            Task write;
            try { write = FirebaseFirestore.DefaultInstance.Collection(GlowUp.AppSettings.SupportFeedbackForm.Collection).Document(id).SetAsync(report); }
            catch (Exception) { return Outcome.Failed; }
            if (!ConnectivityMonitor.Online) return Outcome.Queued;
            float until = Time.realtimeSinceStartup + waitSeconds;
            while (!write.IsCompleted && Time.realtimeSinceStartup < until) await Task.Delay(200);
            if (!write.IsCompleted) return Outcome.Queued;
            if (write.IsFaulted || write.IsCanceled) return Outcome.Failed;
            SentIds.Add(id);
            return Outcome.Sent;
        }

        static string Hash(string s)
        {
            using (var sha = SHA1.Create())
            {
                byte[] b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                for (int i = 0; i < 12; i++) sb.Append(b[i].ToString("x2"));
                return sb.ToString();
            }
        }

        static string Limit(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max).TrimEnd();
    }
}
