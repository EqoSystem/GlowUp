using System;
using GlowUp.AppSettings;
using GlowUp.Common;
using GlowUp.Scan;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// "Import file" on the staff dashboard (24 Nov 2026): the staff member picks a Word, PDF, Excel, JSON, CSV or TXT file that lists
    /// products with their ingredients. Each product becomes one item of the batch, marked "Needs review" with the file's name as its source;
    /// the staff member then checks the list and taps Send ready (the website receives each one as Pending, like any other capture).
    /// The phone's document picker hands over only the chosen file (no storage permission).
    /// </summary>
    public static class ProductFileImport
    {
        public const string Source = "Imported file";

        /// <summary>Opens the picker. `done(added, message)` is called on the main thread; added = 0 means the message says why.</summary>
        public static void Pick(Action<int, string> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var picker = new AndroidJavaClass("com.glowup.files.GlowUpFilePicker"))
                    picker.CallStatic("pick", activity, new Answer(done));
            }
            catch (Exception) { done?.Invoke(0, "The file picker could not open on this phone."); }
#else
            done?.Invoke(0, "Importing a file works on the phone only.");
#endif
        }

        /// <summary>Turns a read file into batch items. Public so it can be tested.</summary>
        public static int AddToBatch(ProductFileParser.Result read, string fileName, out string message)
        {
            if (read.problem.Length > 0) { message = read.problem; return 0; }
            BatchQueue.EnsureLoaded();
            int added = 0, skipped = 0, room = BatchQueue.MaxItems - BatchQueue.Items.Count;
            string from = Source + ": " + fileName;
            foreach (ProductFileParser.Record r in read.records)
            {
                if (added >= room) { skipped++; continue; }
                if (r.barcode.Length > 0 && BatchQueue.Contains(r.barcode)) { skipped++; continue; }
                CaptureDraft d = CaptureDraft.Blank(CaptureDraft.Mode.Manual);
                d.Offer("name", r.name, from, "file", true);
                d.Offer("brand", r.brand, from, "file", true);
                d.Offer("cosmeticType", MatchKind(r.category), from, "file", true);
                d.Offer("barcode", r.barcode, from, "file", true);
                d.Offer("shade", r.shade, from, "file", true);
                if (r.ingredients.Length > 0)
                {
                    IngredientTextParser.ParsedList list = IngredientTextParser.Read(r.ingredients);
                    string joined = list.inOrder != null && list.inOrder.Count >= 2 ? string.Join(", ", list.inOrder) : r.ingredients;
                    d.Offer("ingredients", joined, from, "file", true);
                    d.rawText = CaptureDraft.Limit(r.ingredients, 4000); d.rawFrom = from;
                }
                BatchQueue.Add(d, null);
                added++;
            }
            message = added == 0
                ? (skipped > 0 ? "Nothing new: " + skipped + " already in the batch or over the limit." : "No products were found in the file.")
                : added + " product" + (added == 1 ? "" : "s") + " added to the batch from " + fileName + ", marked Needs review." + (skipped > 0 ? " " + skipped + " skipped (already there or over the limit of " + BatchQueue.MaxItems + ")." : "");
            return added;
        }

        /// <summary>The category as the app names it (Foundation, Lipstick, ...), or the text as given when it matches none.</summary>
        static string MatchKind(string typed)
        {
            typed = (typed ?? "").Trim();
            if (typed.Length == 0) return "";
            foreach (string kind in ProductScope.MakeupTypes)
                if (string.Equals(kind.Trim(), typed, StringComparison.OrdinalIgnoreCase)) return kind.Trim();
            return typed;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        class Answer : AndroidJavaProxy
        {
            readonly Action<int, string> done;
            public Answer(Action<int, string> done) : base("com.glowup.files.GlowUpFilePicker$Callback") { this.done = done; }
            void onFile(string name, string base64) => MainThread.Run(() =>
            {
                string fileName = string.IsNullOrEmpty(name) ? "file" : name;
                try
                {
                    ProductFileParser.Result read = ProductFileParser.Read(fileName, Convert.FromBase64String(base64 ?? ""));
                    int added = AddToBatch(read, fileName, out string message);
                    done?.Invoke(added, message);
                }
                catch (Exception) { done?.Invoke(0, "The file could not be read."); }
            });
            void onFailed(string reason) => MainThread.Run(() =>
                done?.Invoke(0, reason == "cancelled" ? "" : reason == "too-big" ? "That file is over 12 MB. Split it into smaller files." : "The file could not be opened."));
        }
#endif
    }
}
