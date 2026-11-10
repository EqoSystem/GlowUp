using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Scan;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.Tests
{
    /// <summary>
    /// New13, phase A (10 November 2026): the one toast never lies over a button or a text box (on the real scenes), its place, the
    /// check of a captured photo, and the blue tint outside the photo box.
    /// </summary>
    public class New13PhaseATests
    {
        // ---------------------------------------------------------------------------------------
        //  The toast on the real screens: it must not cover any button or input (item 5B)
        // ---------------------------------------------------------------------------------------
        static readonly string[] Scenes =
        {
            "LogInScene", "OnboardingStep1Scene", "OnboardingStep2Scene", "OnboardingStep3Scene", "OnboardingStep4Scene", "OnboardingStep5Scene",
            "TermsAndConditionScene", "AiSmartAnalysisScene", "SettingsScene", "UserDashboardScene"
        };

        const string LongMessage = "Enter your birthday: tap Edit and pick it. Your age is worked out from it. This sentence is made long on purpose so it needs the second line.";

        [Test]
        public void TheToastNeverLiesOverAButtonOrATextBox([ValueSource(nameof(Scenes))] string sceneName, [Values(1920f, 2400f)] float height)
        {
            string path = "Assets/_Project/Scenes/" + sceneName + ".unity";
            if (!System.IO.File.Exists(path)) Assert.Ignore("No scene " + path);
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            try
            {
                Canvas root = null;
                foreach (GameObject go in scene.GetRootGameObjects())
                    if (go.TryGetComponent(out Canvas c) && c.isRootCanvas && go.activeInHierarchy) { root = c; break; }
                Assume.That(root != null, "a root canvas");
                // a phone-sized canvas: 1080 wide (GlowUp's layout width), 1920 or 2400 high
                root.renderMode = RenderMode.WorldSpace;
                var rootRt = (RectTransform)root.transform;
                rootRt.sizeDelta = new Vector2(1080f, height); rootRt.localScale = Vector3.one; rootRt.position = Vector3.zero;
                for (int pass = 0; pass < 3; pass++) { LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt); Canvas.ForceUpdateCanvases(); }

                TMPro.TMP_Text font = root.GetComponentInChildren<TMPro.TMP_Text>(true);
                FormalToast toast = FormalToast.OnScreen(root.transform, font);
                Assert.IsNotNull(toast);
                toast.Show(FormalToast.Kind.Error, LongMessage, "Try again", () => { });
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)toast.transform);
                Rect toastRect = WorldRect((RectTransform)toast.transform);
                Assert.Greater(toastRect.width, 300f, "the toast has its size");

                var hits = new List<string>();
                foreach (Selectable control in root.GetComponentsInChildren<Selectable>(false))
                {
                    if (control.transform.IsChildOf(toast.transform)) continue;
                    if (!control.isActiveAndEnabled || !control.gameObject.activeInHierarchy) continue;
                    if (control.GetComponentInParent<CanvasGroup>() is CanvasGroup g && g.alpha < 0.01f) continue;
                    Rect r = VisibleRect((RectTransform)control.transform);
                    if (r.width < 1f || r.height < 1f) continue;          // outside its scroll view: not on screen
                    if (r.Overlaps(toastRect)) hits.Add(control.name + " " + r);
                }
                Assert.IsEmpty(hits, sceneName + " (" + height + " high): the toast " + toastRect + " lies over " + string.Join(", ", hits));
                Assert.LessOrEqual(toastRect.height, ToastPlacement.MaxHeight + 1f, "two lines at most (the link stands beside the words)");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);      // never saved: the scene file is untouched
            }
        }

        /// <summary>The part of `rt` that is on screen: cut by every Mask / RectMask2D above it (a scroll view's viewport).</summary>
        static Rect VisibleRect(RectTransform rt)
        {
            Rect r = WorldRect(rt);
            for (Transform t = rt.parent; t != null; t = t.parent)
            {
                if (t.GetComponent<RectMask2D>() == null && t.GetComponent<Mask>() == null) continue;
                Rect m = WorldRect((RectTransform)t);
                r = Rect.MinMaxRect(Mathf.Max(r.xMin, m.xMin), Mathf.Max(r.yMin, m.yMin), Mathf.Min(r.xMax, m.xMax), Mathf.Min(r.yMax, m.yMax));
                if (r.width <= 0f || r.height <= 0f) return Rect.zero;
            }
            return r;
        }

        static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(Mathf.Min(c[0].x, c[2].x), Mathf.Min(c[0].y, c[2].y), Mathf.Max(c[0].x, c[2].x), Mathf.Max(c[0].y, c[2].y));
        }

        [Test]
        public void TheToastBandSitsBetweenTheCornerButtonsAndAboveTheSubtitle()
        {
            ToastPlacement.Band b = ToastPlacement.For(1080f, 0f);
            float left = (1080f - b.width) / 2f;
            Assert.GreaterOrEqual(left, 40f + 28f + 92f, "clear of the Back arrow / the X");
            Assert.LessOrEqual(b.width, 1080f * 0.9f);
            Assert.LessOrEqual(b.top + ToastPlacement.MaxHeight, 40f + 186f, "above the onboarding subtitle line");
            Assert.LessOrEqual(b.top + ToastPlacement.MaxHeight, 40f + 160f + 20f, "above the app screens' content (camera view, lists)");
            ToastPlacement.Band notch = ToastPlacement.For(1080f, 90f);
            Assert.AreEqual(b.top + 90f, notch.top, 0.01f, "below the safe area (a notch)");
        }

        [Test]
        public void ErrorsStayLongerThanConfirmations()
        {
            var go = new GameObject("toast");
            try
            {
                FormalToast t = go.AddComponent<FormalToast>();
                Assert.AreEqual(3f, t.SecondsFor(FormalToast.Kind.Success), 0.01f);
                Assert.AreEqual(3f, t.SecondsFor(FormalToast.Kind.Info), 0.01f);
                Assert.AreEqual(5f, t.SecondsFor(FormalToast.Kind.Error), 0.01f);
                Assert.AreEqual(5f, t.SecondsFor(FormalToast.Kind.Warning), 0.01f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------------------------------------------------------------------------------------
        //  The captured photo (item 8)
        // ---------------------------------------------------------------------------------------
        static Color32[] Picture(int w, int h, System.Func<int, int, byte> grey)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { byte g = grey(x, y); px[y * w + x] = new Color32(g, g, g, 255); }
            return px;
        }

        [Test]
        public void ABlackPhotoIsNotShown()
        {
            Assert.AreEqual(PhotoCaptureCheck.Verdict.TooDark, PhotoCaptureCheck.Judge(Picture(400, 600, (x, y) => 0), 400, 600));
            Assert.AreEqual(PhotoCaptureCheck.Verdict.TooDark, PhotoCaptureCheck.Judge(Picture(400, 600, (x, y) => (byte)((x * 7 + y * 3) % 12)), 400, 600));
        }

        [Test]
        public void AnUnreadyCameraFrameIsNotShown()
        {
            Assert.AreEqual(PhotoCaptureCheck.Verdict.NotReady, PhotoCaptureCheck.Judge(Picture(16, 16, (x, y) => 120), 16, 16), "Unity's 16 x 16 frame before the camera runs");
            Assert.AreEqual(PhotoCaptureCheck.Verdict.NotReady, PhotoCaptureCheck.Judge(null, 400, 600));
        }

        [Test]
        public void ADarkPhotoWithLitLettersAndANormalPhotoPass()
        {
            // a dark label with white letters: mostly black, but more than 4 % lit, with sharp edges
            Assert.AreEqual(PhotoCaptureCheck.Verdict.Ok, PhotoCaptureCheck.Judge(Picture(400, 600, (x, y) => (byte)((x / 8 + y / 20) % 9 == 0 ? 230 : 10)), 400, 600));
            Assert.AreEqual(PhotoCaptureCheck.Verdict.Ok, PhotoCaptureCheck.Judge(Picture(400, 600, (x, y) => (byte)(60 + ((x * 31 + y * 17) % 140))), 400, 600));
        }

        [Test]
        public void AFlatPhotoIsTooBlurry()
        {
            Assert.AreEqual(PhotoCaptureCheck.Verdict.TooBlurry, PhotoCaptureCheck.Judge(Picture(400, 600, (x, y) => 128), 400, 600));
        }

        [Test]
        public void TheBlueTintCoversExactlyTheOutsideOfTheBox()
        {
            foreach (Rect inside in new[] { new Rect(0.1f, 0.1f, 0.8f, 0.8f), new Rect(0.3f, 0.05f, 0.2f, 0.9f), new Rect(0f, 0f, 1f, 1f), new Rect(0.4f, 0.6f, 0.15f, 0.15f) })
            {
                Rect[] parts = PhotoCropBox.OutsideParts(inside);
                float area = inside.width * inside.height;
                foreach (Rect p in parts) { area += Mathf.Max(0f, p.width) * Mathf.Max(0f, p.height); Assert.IsFalse(p.width > 0f && p.height > 0f && p.Overlaps(inside), "no tint inside the box"); }
                Assert.AreEqual(1f, area, 0.0001f, "box + tint = the whole photo, once");
            }
        }
    }
}
