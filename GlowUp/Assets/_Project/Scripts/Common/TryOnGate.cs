using System;
using GlowUp.Data;
using GlowUp.Scan;

namespace GlowUp.Common
{
    /// <summary>
    /// AR TRY-ON NEEDS A PRODUCT (12 November 2026). A new user who opened AR Try-On right after sign-up had nothing to try on: the camera
    /// question came, and the screen went black. Now AR Try-On only opens when there is a product to try: one chosen (a scan result, a
    /// Recent Scans row) or at least one saved scan to choose from. Otherwise the screen stays where it is and says why.
    /// SceneNavigator.Go asks this before it opens AR Try-On, so every way there (bottom bar, swipe, shake, buttons) is covered.
    /// </summary>
    public static class TryOnGate
    {
        // 17 Nov 2026 (owner): say where a product comes from: a scan (new users), Recommended for You on the dashboard, or an Alternative on a scan result
        public const string Message = "Try On needs a product. Scan one, or pick one from Recent Scans, Recommended for You on the dashboard, or the Alternatives on a scan result.";

        /// <summary>Raised when AR Try-On was refused (the screen that is open shows the message).</summary>
        public static event Action Blocked;

        /// <summary>
        /// True only when a product was chosen (17 Nov 2026, owner: Try On exists to preview a certain product, so it never opens without one,
        /// even for a user who has saved scans). The product is chosen on a scan result, in Recent Scans, in Recommended for You or in the
        /// Alternatives; each of those sets ScanSession.TryOn before it opens AR Try-On.
        /// </summary>
        /// 18 Nov 2026 (owner): a user who has scanned a product may open Try On with it. When nothing was chosen, the latest scan (this
        /// session's result, else the newest saved scan) that AR Try-On can show becomes the product, so a new user's first scan is never blocked.
        public static bool Allowed
        {
            get
            {
                if (ScanSession.TryOn == null) ScanSession.TryOn = LatestScan();
                return ScanSession.TryOn != null;
            }
        }

        /// <summary>The newest scanned make-up AR Try-On can show (null = none).</summary>
        static ScanRecord LatestScan()
        {
            try
            {
                if (ScanSession.Current != null && ProductScope.CanTryOn(ScanSession.Current)) return ScanSession.Current;
                foreach (ScanRecord saved in ScanHistoryRepository.Active())
                    if (saved != null && ProductScope.CanTryOn(saved)) return saved;
            }
            catch (Exception e) { { } }
            return null;
        }

        /// <summary>Tells the open screen that AR Try-On was refused.</summary>
        public static void RaiseBlocked()
        {
            try { Blocked?.Invoke(); } catch (Exception e) { { } }
        }
    }
}
