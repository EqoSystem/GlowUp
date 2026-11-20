using System;
using System.Collections.Generic;
using Firebase.Firestore;
using GlowUp.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Maintenance Mode in the app (New22, 20 November 2026). ONE live listener for the whole app on systemConfig/maintenanceMode
    /// (the admins set it on the website; anyone may read it, also before signing in). Three stages:
    ///   1. Advance warning: notifyUsers true and not active yet: a dismissible banner at the top of every screen
    ///      ("Heads up! GlowUp will be unavailable ... starting at ... until ..."). Closing it hides it for this run of the app;
    ///      it comes back next time until the admins reset the notice.
    ///   2. Maintenance active: a full-screen maintenance screen over EVERY scene (log-in included), with the admins' title and
    ///      message and a live countdown to the expected end ("We'll be back soon" without one). Nothing behind it can be used.
    ///   3. Ended: the screen goes away by itself and a toast says "GlowUp is back online!"; no restart needed.
    /// Everything shown is drawn from the latest snapshot. The overlay is its own canvas (sorting order 30000) built at run time
    /// from the theme's colours and fonts, inside the phone's safe area.
    /// </summary>
    public class MaintenanceWatcher : MonoBehaviour
    {
        public static MaintenanceWatcher Instance { get; private set; }
        public static bool Active { get; private set; }

        ListenerRegistration listener;
        bool isActive, notify, dirty, bannerClosed, wasActive;
        DateTime start, end;
        string title = "", message = "";

        Canvas canvas;
        RectTransform safe;
        GameObject screen, banner;
        TextMeshProUGUI screenTitle, screenMessage, countdown, bannerText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("MaintenanceWatcher");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MaintenanceWatcher>();
        }

        async void Start()
        {
            if (!await FirebaseBootstrap.Ready) return;
            listener = FirebaseFirestore.DefaultInstance.Collection("systemConfig").Document("maintenanceMode").Listen(snap =>
            {
                var f = snap.Exists ? snap.ToDictionary() : new Dictionary<string, object>();
                isActive = f.TryGetValue("isActive", out object a) && a is bool ab && ab;
                notify = f.TryGetValue("notifyUsers", out object n) && n is bool nb && nb;
                start = f.TryGetValue("scheduledStart", out object s) && s is Timestamp st ? st.ToDateTime().ToLocalTime() : DateTime.MinValue;
                end = f.TryGetValue("scheduledEnd", out object e) && e is Timestamp et ? et.ToDateTime().ToLocalTime() : DateTime.MinValue;
                title = f.TryGetValue("bannerTitle", out object t) && t is string ts ? ts : "";
                message = f.TryGetValue("customMessage", out object m) && m is string ms ? ms : "";
                if (!notify) bannerClosed = false;
                dirty = true;
            });
        }

        void OnDestroy() { listener?.Stop(); if (Instance == this) Instance = null; }

        void Update()
        {
            if (dirty) { dirty = false; Apply(); }
            if (isActive && countdown != null) countdown.text = Countdown(end);
        }

        void Apply()
        {
            Active = isActive;
            bool showBanner = notify && !isActive && !bannerClosed && start > DateTime.Now;
            if ((isActive || showBanner) && canvas == null) Build();
            if (screen != null) screen.SetActive(isActive);
            if (banner != null) banner.SetActive(showBanner);
            if (isActive)
            {
                screenTitle.text = string.IsNullOrWhiteSpace(title) ? "GlowUp is being updated" : title;
                screenMessage.text = string.IsNullOrWhiteSpace(message) ? "We are making GlowUp better. Thank you for waiting." : message;
                countdown.text = Countdown(end);
                // anything typed or open behind the screen is left as it is; nothing behind it keeps the focus
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            }
            if (showBanner)
                bannerText.text = "Heads up! GlowUp will be unavailable for updates starting at " + start.ToString("MMM d, h:mm tt")
                                  + (end > start ? " until " + end.ToString("MMM d, h:mm tt") : "") + ".";
            if (wasActive && !isActive)
            {
                FormalToast toast = FormalToast.OnScreen(safe != null ? (Transform)safe : transform, screenTitle);
                if (toast != null) toast.Show(FormalToast.Kind.Success, "GlowUp is back online!");
            }
            wasActive = isActive;
        }

        static string Countdown(DateTime end)
        {
            if (end == DateTime.MinValue) return "We'll be back soon";
            TimeSpan left = end - DateTime.Now;
            if (left.TotalSeconds <= 0) return "Almost done. We'll be back any moment.";
            return string.Format("Back in {0}d {1:00}h {2:00}m {3:00}s", (int)left.TotalDays, left.Hours, left.Minutes, left.Seconds);
        }

        void Build()
        {
            var p = RuntimeTheme.Palette;
            TMP_FontAsset titleFont = RuntimeTheme.Font(GlowUp.Theme.FontRole.Title), body = RuntimeTheme.Font(GlowUp.Theme.FontRole.Body);
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = 0f;
            gameObject.AddComponent<GraphicRaycaster>();

            // ---- stage 2: the maintenance screen (blocks every tap behind it) ----
            Image cover = RuntimeUiKit.Box(transform, "MaintenanceScreen", p.background, false);
            cover.raycastTarget = true;
            RuntimeUiKit.Stretch(cover.rectTransform);
            screen = cover.gameObject;
            safe = RuntimeUiKit.Rect("SafeArea", cover.transform);
            RuntimeUiKit.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            Image card = RuntimeUiKit.Box(safe, "Card", p.surface);
            RuntimeUiKit.Stretch(card.rectTransform, 48, 48, 260, 260);
            var column = card.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(64, 64, 96, 96); column.spacing = 36; column.childAlignment = TextAnchor.MiddleCenter;
            column.childControlWidth = true; column.childControlHeight = true; column.childForceExpandHeight = false;
            Image icon = RuntimeUiKit.Icon(card.transform, "Icon", RuntimeIcons.Info, p.accent, 160);
            var iconSize = icon.gameObject.AddComponent<LayoutElement>(); iconSize.preferredHeight = 160; iconSize.preferredWidth = 160;
            screenTitle = RuntimeUiKit.Label(card.transform, "Title", "", 60, p.heading, titleFont);
            screenMessage = RuntimeUiKit.Label(card.transform, "Message", "", 36, p.mainText, body);
            countdown = RuntimeUiKit.Label(card.transform, "Countdown", "", 44, p.accentText, body);
            countdown.fontStyle = FontStyles.Bold;
            RuntimeUiKit.Label(card.transform, "Note", "You can't log in or use the app until the update is finished. This screen closes by itself.", 28, p.secondaryText, body);
            screen.SetActive(false);

            // ---- stage 1: the advance-warning banner (top of the safe area) ----
            RectTransform bannerArea = RuntimeUiKit.Rect("BannerArea", transform);
            RuntimeUiKit.Stretch(bannerArea);
            bannerArea.gameObject.AddComponent<SafeAreaFitter>();
            Image bar = RuntimeUiKit.Box(bannerArea, "AdvanceNotice", RuntimeTheme.Mix(p.surface, p.accent, 0.14f));
            bar.raycastTarget = true;
            var brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.offsetMin = new Vector2(32, -260); brt.offsetMax = new Vector2(-32, -24);
            bannerText = RuntimeUiKit.Label(bar.transform, "Text", "", 30, p.mainText, body, TextAlignmentOptions.MidlineLeft);
            RuntimeUiKit.Stretch(bannerText.rectTransform, 36, 168, 16, 16);
            Image xBack = RuntimeUiKit.Box(bar.transform, "CloseButton", Color.clear, false);
            xBack.raycastTarget = true;
            var xr = xBack.rectTransform;
            xr.anchorMin = xr.anchorMax = new Vector2(1, 0.5f); xr.pivot = new Vector2(1, 0.5f); xr.sizeDelta = new Vector2(144, 144); xr.anchoredPosition = new Vector2(-8, 0);
            TextMeshProUGUI x = RuntimeUiKit.Label(xBack.transform, "X", "×", 64, p.secondaryText, body);
            RuntimeUiKit.Stretch(x.rectTransform);
            var close = xBack.gameObject.AddComponent<Button>();
            close.targetGraphic = xBack;
            close.onClick.AddListener(() => { bannerClosed = true; dirty = true; });
            banner = bar.gameObject;
            banner.SetActive(false);
        }
    }
}
