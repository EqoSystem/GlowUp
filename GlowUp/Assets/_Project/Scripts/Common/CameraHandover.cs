using System.Collections;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// ONE CAMERA, SEVERAL SCREENS (17 Nov 2026). The phone closes a camera a moment AFTER Unity's WebCamTexture.Stop() returns. The
    /// finger signs keep the selfie camera open on the dashboard, the scanner and the guided preview use it too, and AR Try-On needs the
    /// same front camera: when AR Try-On opened it in the very frames after the dashboard let go, the phone still held it, and the AR
    /// picture stayed black (dashboard > scan > result > dashboard > AR Try-On, reported on a phone).
    /// Whoever lets go of a camera says so (<see cref="Released"/>); whoever is about to open one waits (<see cref="WaitUntilFree"/>)
    /// until that has been so for <see cref="SettleSeconds"/>. Costs nothing when no camera was open.
    /// </summary>
    public static class CameraHandover
    {
        /// <summary>How long the phone is given to really close a camera.</summary>
        public const float SettleSeconds = 1.0f;

        static float releasedAt = -100f;

        /// <summary>A camera user has just stopped (or is stopping) its camera.</summary>
        public static void Released() { releasedAt = Time.unscaledTime; }

        /// <summary>Seconds still to wait before another camera user may open the camera (0 = free now).</summary>
        public static float WaitLeft() => Mathf.Max(0f, releasedAt + SettleSeconds - Time.unscaledTime);

        /// <summary>For a coroutine: yields until the last camera user's camera is really closed.</summary>
        public static IEnumerator WaitUntilFree()
        {
            while (WaitLeft() > 0f) yield return null;
        }
    }
}
