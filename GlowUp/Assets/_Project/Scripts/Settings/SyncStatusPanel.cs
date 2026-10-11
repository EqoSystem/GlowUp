using System;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Sync Status on the Settings page: Online / Offline, how many changes are waiting to be sent
    /// (scans made offline, profile edits), when everything was last sent, and the Sync Now button.
    /// It repaints itself when the connection or the queue changes.
    /// </summary>
    public class SyncStatusPanel : ThemeAwareBehaviour
    {
        [SerializeField] private Image statusDot;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI pendingText;
        [SerializeField] private TextMeshProUGUI lastSyncText;
        [SerializeField] private Button syncButton;
        [SerializeField] private TextMeshProUGUI syncButtonLabel;
        [SerializeField] private TextMeshProUGUI messageText;
        [Tooltip("Sync Now gives up waiting after this many seconds (the changes stay queued and are tried again later)")]
        [SerializeField] private float syncTimeoutSeconds = 20f;

        [Header("Wording")]
        [SerializeField] private string onlineText = "Online";
        [SerializeField] private string offlineText = "Offline";
        [SerializeField] private string nothingWaiting = "Everything is saved to your account.";
        [SerializeField] private string oneWaiting = "1 change is waiting to be sent.";
        [Tooltip("{0} = how many")]
        [SerializeField] private string manyWaiting = "{0} changes are waiting to be sent.";
        [Tooltip("{0} = date and time")]
        [SerializeField] private string lastSyncFormat = "Last sent: {0}";
        [SerializeField] private string neverSynced = "Nothing has been sent from this phone yet.";
        [SerializeField] private string syncLabel = "Sync Now";
        [SerializeField] private string syncingLabel = "Sending...";
        [SerializeField] private string msgOffline = "You are offline. Your changes are kept on this phone and sent as soon as you are online.";
        [SerializeField] private string msgDone = "Done. Everything is saved to your account.";
        [SerializeField] private string msgStillWaiting = "Some changes could not be sent yet. They stay on this phone and are tried again.";
        [SerializeField] private string msgNeedsOnlineSignIn = "Sign in once with internet so your changes can be sent to your account.";

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color onlineColor = new Color32(79, 114, 64, 255);
        [SerializeField] private Color offlineColor = new Color32(148, 99, 18, 255);

        bool busy, started;

        void Awake() { if (syncButton != null) syncButton.onClick.AddListener(SyncNow); }

        protected override void OnEnable()
        {
            base.OnEnable();
            ConnectivityMonitor.ConnectivityChanged += OnConnection;
            SyncQueue.StateChanged += Refresh;
            Refresh();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ConnectivityMonitor.ConnectivityChanged -= OnConnection;
            SyncQueue.StateChanged -= Refresh;
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            onlineColor = p.success; offlineColor = p.caution;
            if (Application.isPlaying) Refresh();
            else if (statusDot != null) statusDot.color = onlineColor;      // Edit mode: the "Online" look
        }

        void Start()
        {
            started = true;             // from here on every shared service of the scene exists
            if (messageText != null) messageText.text = "";
            Refresh();
        }

        void OnConnection(bool online) { Refresh(); }

        void Refresh()
        {
            if (this == null || !started) return;
            bool online = ConnectivityMonitor.Online;
            int waiting = SyncQueue.PendingCount;
            bool sending = busy || SyncQueue.IsSyncing;

            if (statusDot != null) statusDot.color = online ? onlineColor : offlineColor;
            if (statusText != null) { statusText.text = online ? onlineText : offlineText; statusText.color = online ? onlineColor : offlineColor; }
            if (pendingText != null) pendingText.text = waiting == 0 ? nothingWaiting : waiting == 1 ? oneWaiting : string.Format(manyWaiting, waiting);
            if (lastSyncText != null)
            {
                long at = SyncQueue.LastSyncedUnix;
                lastSyncText.text = at > 0 ? string.Format(lastSyncFormat, ScanHistoryRepository.FormatTime(at)) : neverSynced;
            }
            if (syncButton != null) syncButton.interactable = !sending;
            if (syncButtonLabel != null) syncButtonLabel.text = sending ? syncingLabel : syncLabel;
        }

        async void SyncNow()
        {
            if (busy) return;
            if (!ConnectivityMonitor.Online) { Say(msgOffline); return; }
            busy = true; Say(""); Refresh();
            try
            {
                float deadline = Time.realtimeSinceStartup + Mathf.Max(3f, syncTimeoutSeconds);
                var sending = SyncQueue.SyncNow();
                // also covers a send that was already running when the button was pressed
                while ((!sending.IsCompleted || SyncQueue.IsSyncing) && Time.realtimeSinceStartup < deadline) await System.Threading.Tasks.Task.Delay(150);
            }
            catch (Exception e) { Debug.LogWarning("Sync Now: " + e.Message); }
            if (this == null) return;
            busy = false;
            Refresh();
            if (SyncQueue.PendingCount == 0) Say(msgDone);
            else Say(string.IsNullOrEmpty(UserProfileRepository.CurrentUid) ? msgNeedsOnlineSignIn : msgStillWaiting);
        }

        void Say(string message) { if (messageText != null) messageText.text = message; }
    }
}
