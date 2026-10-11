using System;
using System.IO;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Data;
using GlowUp.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Clear Local Cache on the Settings page. It shows how much space the app's saved copies take and
    /// frees it after the user confirms.
    ///
    /// CLEARED: the saved copies of the ingredient list, brands and products (downloaded again the next
    ///          time the phone is online), temporary files and downloaded pictures, and (optional)
    ///          Firestore's own offline cache.
    /// NEVER CLEARED: the user's profile, their scan history, and changes still waiting to be sent.
    /// </summary>
    public class LocalCacheCleaner : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI sizeText;
        [SerializeField] private Button clearButton;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private ConfirmDialog confirm;

        [Header("What is cleared")]
        [Tooltip("The saved copies of the website's lists. File names inside the app's GlowUpCache folder, without .json.")]
        [SerializeField] private string[] catalogueCacheKeys = { "ingredients", "brands", "products" };
        [SerializeField] private bool clearTemporaryFiles = true;
        [Tooltip("Also empty Firestore's own offline cache. It is skipped while changes are being sent.")]
        [SerializeField] private bool clearFirestoreOfflineCache = true;

        [Header("Wording")]
        [Tooltip("{0} = size, e.g. 1.4 MB")]
        [SerializeField] private string sizeFormat = "Saved copies on this phone: {0}";
        [SerializeField] private string confirmTitle = "Clear saved copies?";
        [SerializeField] private string confirmMessage = "This frees space by removing the saved ingredient, brand and product lists and temporary files. They are downloaded again the next time you are online.\n\nYour profile, your scan history and changes waiting to be sent are kept.";
        [SerializeField] private string confirmMessageOffline = "You are offline. If you clear now, the app has only its built-in ingredient list until you are online again.\n\nYour profile, your scan history and changes waiting to be sent are kept.";
        [SerializeField] private string confirmButtonText = "Clear";
        [Tooltip("{0} = how much was freed")]
        [SerializeField] private string msgCleared = "Cleared {0}.";
        [SerializeField] private string msgNothing = "There was nothing to clear.";

        bool busy;

        void Awake() { if (clearButton != null) clearButton.onClick.AddListener(Ask); }
        void Start() { if (messageText != null) messageText.text = ""; ShowSize(); }

        void Ask()
        {
            if (busy) return;
            if (confirm == null) { _ = Clear(); return; }
            confirm.Ask(confirmTitle, ConnectivityMonitor.Online ? confirmMessage : confirmMessageOffline, confirmButtonText, () => { _ = Clear(); });
        }

        async Task Clear()
        {
            if (busy) return;
            busy = true;
            if (clearButton != null) clearButton.interactable = false;
            long before = Measure();
            try
            {
                foreach (string key in catalogueCacheKeys ?? new string[0])
                    if (!string.IsNullOrWhiteSpace(key)) OfflineCache.Delete(key.Trim());

                if (clearTemporaryFiles)
                {
                    EmptyFolder(Application.temporaryCachePath);
                    try { Caching.ClearCache(); } catch (Exception e) { Debug.LogWarning("Clear cache: " + e.Message); }
                }

                // Firestore's own cache can only be emptied while its connection is closed; the next use opens a new one
                if (clearFirestoreOfflineCache && !SyncQueue.IsSyncing && await FirebaseBootstrap.Ready)
                {
                    try
                    {
                        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
                        await db.TerminateAsync();
                        await db.ClearPersistenceAsync();
                    }
                    catch (Exception e) { Debug.LogWarning("Clear cache: Firestore's offline cache was left as it is: " + e.Message); }
                }
            }
            catch (Exception e) { Debug.LogWarning("Clear cache failed: " + e.Message); }

            if (this == null) return;
            busy = false;
            if (clearButton != null) clearButton.interactable = true;
            long freed = Math.Max(0L, before - Measure());
            if (messageText != null) messageText.text = freed > 0 ? string.Format(msgCleared, Readable(freed)) : msgNothing;
            ShowSize();
        }

        void ShowSize() { if (sizeText != null) sizeText.text = string.Format(sizeFormat, Readable(Measure())); }

        /// <summary>Bytes used by what Clear removes (Firestore's own cache cannot be measured from here).</summary>
        long Measure()
        {
            long total = 0;
            string cacheFolder = Path.Combine(Application.persistentDataPath, "GlowUpCache");
            foreach (string key in catalogueCacheKeys ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                try { var file = new FileInfo(Path.Combine(cacheFolder, key.Trim() + ".json")); if (file.Exists) total += file.Length; }
                catch (Exception) { /* a file that cannot be read counts as empty */ }
            }
            if (clearTemporaryFiles) total += FolderSize(Application.temporaryCachePath);
            return total;
        }

        static long FolderSize(string folder)
        {
            long total = 0;
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    try { total += new FileInfo(file).Length; } catch (Exception) { }
            }
            catch (Exception) { }
            return total;
        }

        static void EmptyFolder(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    try { File.Delete(file); } catch (Exception) { /* in use: left for next time */ }
            }
            catch (Exception e) { Debug.LogWarning("Clear cache: " + e.Message); }
        }

        static string Readable(long bytes)
        {
            if (bytes >= 1024L * 1024L) return (bytes / (1024f * 1024f)).ToString("0.#") + " MB";
            if (bytes >= 1024L) return (bytes / 1024f).ToString("0") + " KB";
            return bytes + " bytes";
        }
    }
}
