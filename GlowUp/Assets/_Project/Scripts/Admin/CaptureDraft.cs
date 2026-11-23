using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// One product capture in progress (staff companion). Every value keeps where it came from and its state, in the same
    /// words the website uses (_Shared/capture.js): detected (found by a look-up or text reading, nobody checked it),
    /// review (found but uncertain), verified (the staff member confirmed it) or typed (typed by the staff member).
    /// Nothing is guessed: a value no source gave stays empty and is shown as Missing.
    ///
    /// The text part is kept on the phone (persistentDataPath, this account only) so leaving the screen, a crash or an
    /// expired session does not lose the work. The photos live in memory only. The capture id is made once and is the
    /// database id of the submission, so sending twice (a retry, a double tap) writes the same record, never a second one.
    /// </summary>
    [Serializable]
    public class CaptureDraft
    {
        public enum Mode { Barcode, Photo, Gallery, Manual }

        [Serializable]
        public class Field
        {
            public string key, value = "", state = "", from = "", method = "";
            public bool Has => !string.IsNullOrWhiteSpace(value);
        }

        public string captureId = "", uid = "", mode = "Barcode";
        public long startedUnix;
        public bool batchItem;                       // a product of the batch queue: never written to the single draft file
        public List<Field> fields = new List<Field>();
        public string rawText = "", rawFrom = "";
        public string duplicateOf = "", duplicateName = "";
        public string submittedId = "";
        public long submittedUnix;

        public static readonly string[] Keys = { "name", "brand", "cosmeticType", "barcode", "ingredients", "shade" };
        public static string Label(string key)
        {
            switch (key)
            {
                case "name": return "Product name";
                case "brand": return "Brand";
                case "cosmeticType": return "Category";
                case "barcode": return "Barcode";
                case "ingredients": return "Ingredients";
                case "shade": return "Shade (as printed)";
                default: return key;
            }
        }

        // ---- the one draft of this phone ----
        static CaptureDraft current;
        static string PathOf(string uid) => Path.Combine(Application.persistentDataPath, "staff_capture_" + uid + ".json");

        /// <summary>The draft of the signed-in staff member (restored from the phone when there is one).</summary>
        public static CaptureDraft Current
        {
            get
            {
                string uid = StaffAccess.Uid;
                if (current != null && current.uid == uid) return current;
                current = Load(uid) ?? NewFor(uid, Mode.Barcode);
                return current;
            }
        }

        /// <summary>Starts a new capture (the old one is thrown away only when it was already submitted or empty).</summary>
        public static CaptureDraft Start(Mode mode, bool keepUnsent)
        {
            CaptureDraft old = Current;
            if (keepUnsent && old.HasContent && string.IsNullOrEmpty(old.submittedId)) { old.mode = mode.ToString(); old.Save(); return old; }
            current = NewFor(StaffAccess.Uid, mode);
            current.Save();
            return current;
        }

        /// <summary>A separate draft for the batch queue (the single draft of this phone is not touched).</summary>
        public static CaptureDraft Blank(Mode mode) { CaptureDraft d = NewFor(StaffAccess.Uid ?? "", mode); d.batchItem = true; return d; }

        static CaptureDraft NewFor(string uid, Mode mode)
        {
            var d = new CaptureDraft { uid = uid, mode = mode.ToString(), startedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            // short, unique, URL-safe; the uid prefix keeps two staff phones apart
            string who = uid.Length >= 6 ? uid.Substring(0, 6).ToLowerInvariant() : "staff";
            d.captureId = "cap_" + who + "_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            foreach (string k in Keys) d.fields.Add(new Field { key = k });
            return d;
        }

        public bool HasContent { get { foreach (Field f in fields) if (f.Has) return true; return false; } }
        public Mode ModeValue => Enum.TryParse(mode, out Mode m) ? m : Mode.Barcode;

        public Field Get(string key)
        {
            foreach (Field f in fields) if (f.key == key) return f;
            var made = new Field { key = key };
            fields.Add(made);
            return made;
        }

        /// <summary>A value found by a source. It never replaces a value the staff member typed or confirmed.</summary>
        public bool Offer(string key, string value, string from, string method, bool uncertain)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0) return false;
            Field f = Get(key);
            if (f.Has && (f.state == "typed" || f.state == "verified")) return false;
            if (f.Has && f.value == value) return false;
            f.value = value; f.from = Limit(from, 80); f.method = method; f.state = uncertain ? "review" : "detected";
            return true;
        }

        /// <summary>The staff member typed or changed the value.</summary>
        public void Type(string key, string value)
        {
            Field f = Get(key);
            value = (value ?? "").Trim();
            if (value == f.value) return;
            f.value = value; f.state = value.Length > 0 ? "typed" : ""; f.from = value.Length > 0 ? "Staff (typed in the app)" : ""; f.method = "manual";
        }

        /// <summary>The staff member compared the value with the pack.</summary>
        public void Confirm(string key) { Field f = Get(key); if (f.Has && (f.state == "detected" || f.state == "review")) f.state = "verified"; }

        public void Save()
        {
            if (string.IsNullOrEmpty(uid) || batchItem) return;
            try { File.WriteAllText(PathOf(uid), JsonUtility.ToJson(this)); } catch (Exception) { }
        }

        static CaptureDraft Load(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            try
            {
                string path = PathOf(uid);
                if (!File.Exists(path)) return null;
                var d = JsonUtility.FromJson<CaptureDraft>(File.ReadAllText(path));
                return d != null && d.uid == uid && !string.IsNullOrEmpty(d.captureId) ? d : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>After a confirmed submission: the next capture starts clean.</summary>
        public static void Clear()
        {
            string uid = StaffAccess.Uid;
            try { if (!string.IsNullOrEmpty(uid) && File.Exists(PathOf(uid))) File.Delete(PathOf(uid)); } catch (Exception) { }
            current = null;
        }

        /// <summary>The photos of this capture (memory only): role + picture. Released when the capture ends.</summary>
        public static readonly List<KeyValuePair<string, Texture2D>> Photos = new List<KeyValuePair<string, Texture2D>>();
        public const int MaxPhotos = 6;
        public static void ReleasePhotos()
        {
            foreach (var p in Photos) if (p.Value != null) UnityEngine.Object.Destroy(p.Value);
            Photos.Clear();
        }

        public static string Limit(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max).TrimEnd();
    }
}
