using System;
using System.IO;
using GlowUp.Common;
using GlowUp.Scan;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Pictures of the parts added on 7 November 2026 (status messages, busy buttons, the photo preview, Back to top), drawn the
    /// way the app draws them, in the light and the dark theme, at a 360 dp and a 480 dp wide phone. Nothing is saved to a scene.
    /// Batch: Unity.exe -batchmode -projectPath ... -executeMethod GlowUp.EditorTools.RuntimeUiGallery.RenderFromCommandLine -galleryOut DIR
    /// </summary>
    public static class RuntimeUiGallery
    {
        public static void RenderFromCommandLine()
        {
            string dir = "Builds/Gallery";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-galleryOut") dir = args[i + 1];
            int code = 0;
            try { Render(dir); }
            catch (Exception e) { Debug.LogError("[GlowUp] Gallery failed: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        [MenuItem("GlowUp/Tools/Render New UI Parts Gallery")]
        public static void RenderFromMenu() => Render("Builds/Gallery");

        static void Render(string dir)
        {
            Directory.CreateDirectory(dir);
            ThemeProfile profile = AssetDatabase.LoadAssetAtPath<ThemeProfile>("Assets/_Project/ThemeProfile.asset");
            Debug.Log("[GlowUp] Gallery theme: " + (profile != null ? "ThemeProfile.asset" : "built-in palettes"));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (bool dark in new[] { false, true })
                foreach (var size in new[] { new Vector2Int(1080, 2340), new Vector2Int(1440, 3120) })
                {
                    RuntimeTheme.PreviewProfile = profile;
                    RuntimeTheme.PreviewDark = dark;
                    RuntimeTheme.PreviewPalette = profile != null ? profile.Get(dark) : dark ? ThemeProfile.DarkDefault() : ThemeProfile.LightDefault();
                    string tag = (dark ? "dark" : "light") + "_" + (size.x == 1080 ? "360dp" : "480dp");
                    Shoot(Path.Combine(dir, "components_" + tag + ".png"), size, c => Components(c));
                    Shoot(Path.Combine(dir, "photo_preview_" + tag + ".png"), size, c => Preview(c, false));
                    Shoot(Path.Combine(dir, "photo_unclear_" + tag + ".png"), size, c => Preview(c, true));
                    Shoot(Path.Combine(dir, "back_to_top_" + tag + ".png"), size, c => BackToTop(c));
                }
            RuntimeTheme.PreviewPalette = null; RuntimeTheme.PreviewProfile = null;
            Debug.Log("[GlowUp] Gallery written to " + Path.GetFullPath(dir));
        }

        static void Shoot(string file, Vector2Int size, Action<RectTransform> fill)
        {
            var root = new GameObject("Gallery (temporary)");
            var cameraObject = new GameObject("GalleryCamera (temporary)");
            var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RuntimeTheme.Palette.background;
                camera.orthographic = true; camera.targetTexture = target; camera.transform.position = new Vector3(5000, 5000, -50);
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10f;
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = 0f;
                root.AddComponent<GraphicRaycaster>();
                var background = root.AddComponent<Image>();
                background.color = RuntimeTheme.Palette.background;
                // the canvas takes the picture's size
                camera.aspect = size.x / (float)size.y;
                Canvas.ForceUpdateCanvases();
                fill((RectTransform)root.transform);
                for (int pass = 0; pass < 4; pass++)
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (var group in root.GetComponentsInChildren<LayoutGroup>(true)) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture before = RenderTexture.active;
                RenderTexture.active = target;
                var picture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                picture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); picture.Apply();
                RenderTexture.active = before;
                File.WriteAllBytes(file, picture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(picture);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }

        // ---------------------------------------------------------------------------------------
        static RectTransform Column(RectTransform canvas)
        {
            RectTransform column = RuntimeUiKit.Rect("Column", canvas);
            column.anchorMin = new Vector2(0, 1); column.anchorMax = new Vector2(1, 1); column.pivot = new Vector2(0.5f, 1);
            column.offsetMin = new Vector2(48, 0); column.offsetMax = new Vector2(-48, -60);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 26; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return column;
        }

        static TMP_FontAsset Body => RuntimeTheme.Font(FontRole.Body);

        static void Heading(RectTransform column, string words)
        {
            TextMeshProUGUI t = RuntimeUiKit.Label(column, "Heading", words, 34, RuntimeTheme.Palette.secondaryText, RuntimeTheme.Font(FontRole.Heading), TextAlignmentOptions.Left);
            t.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        }

        static void Status(RectTransform column, StatusMessage.Kind kind, string words, string link = null)
        {
            TextMeshProUGUI t = RuntimeUiKit.Label(column, "Text", "x", 26, RuntimeTheme.Palette.secondaryText, Body, TextAlignmentOptions.TopLeft);
            StatusMessage box = StatusMessage.Wrap(t);
            box.Show(kind, words, link, link != null ? (Action)(() => { }) : null);
        }

        static void BusyButton(RectTransform column, float width, string words, string shortWords)
        {
            RectTransform row = RuntimeUiKit.Rect("Row", column);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 132;
            Button b = RuntimeUiKit.Pill(row, "Button", "Take a photo of the product", RuntimeTheme.Palette.buttonFill, RuntimeTheme.Palette.buttonText, RuntimeTheme.Font(FontRole.Button));
            var rt = (RectTransform)b.transform;
            UnityEngine.Object.DestroyImmediate(b.GetComponent<LayoutElement>());
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f); rt.sizeDelta = new Vector2(width, 132); rt.anchoredPosition = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            TMP_Text label = b.GetComponentInChildren<TMP_Text>();
            ButtonBusy.Set(b, label, true, words, shortWords);
        }

        static void Components(RectTransform canvas)
        {
            RectTransform column = Column(canvas);
            var p = RuntimeTheme.Palette;
            Heading(column, "Scan sheet: status messages");
            Status(column, StatusMessage.Kind.Info, "Take one photo of the front of the pack. We'll look it up and show you what we find.");
            Status(column, StatusMessage.Kind.Error, "The photo couldn't be looked up.", "Type it in");
            Heading(column, "Busy buttons (spinner inside, left of the label)");
            BusyButton(column, 860, "Analyzing Packaging...", "Analyzing…");
            BusyButton(column, 470, "Analyzing Packaging...", "Analyzing…");
            BusyButton(column, 470, "Wait 18 s", null);
            BusyButton(column, 470, "Searching…", null);
            Heading(column, "Type it in: searching, found, not found");
            Status(column, StatusMessage.Kind.Progress, "Searching for <b>Maybelline New York Matte Ink</b>…");
            Status(column, StatusMessage.Kind.Progress, "Not in our catalog yet. Checking product pages online…");
            Status(column, StatusMessage.Kind.Success, "Found ingredient list · Unverified source");
            Status(column, StatusMessage.Kind.Error, "Couldn't find an ingredient list — you can type it in below.");
            Heading(column, "Log in: Google sign-in did not finish");
            Status(column, StatusMessage.Kind.Error, "Google sign-in didn't complete. Please try again.", "Try again");
            Heading(column, "Result: pending review");
            TextMeshProUGUI badge = RuntimeUiKit.Label(column, "Badge", "Unverified — pending review", 36, p.caution, RuntimeTheme.Font(FontRole.Label), TextAlignmentOptions.Left);
            badge.fontStyle = FontStyles.Bold;
            RuntimeUiKit.Label(column, "Note", "Ingredient data from the web may be incomplete. GlowUp hasn't reviewed this product yet.", 40, p.mainText, Body, TextAlignmentOptions.Left);
            TextMeshProUGUI link = RuntimeUiKit.Label(column, "Link", "<u>Unverified source · maybelline.com</u>", 40, p.accentText, Body, TextAlignmentOptions.Left);
            link.richText = true;
        }

        static void Preview(RectTransform canvas, bool unclear)
        {
            var anchor = RuntimeUiKit.Label(canvas, "FontFrom", "", 30, Color.black, Body);
            PhotoPreview preview = PhotoPreview.Build(anchor.transform, anchor, "Check your photo", "Your product photo will be reviewed by GlowUp to add it to the catalog.", "Retake", "Use photo", "Not now");
            Texture2D photo = FakePhoto();
            preview.Show(photo, true);
            if (unclear) preview.Problem("We couldn't read this clearly. Retake the photo in good light, or type the product in.", "Type it in", () => { }, false);
            else
            {
                Canvas.ForceUpdateCanvases();
                foreach (var group in canvas.GetComponentsInChildren<LayoutGroup>(true)) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
                preview.SetBusy(true, "Analyzing Packaging...", "Analyzing…");
                preview.Say(StatusMessage.Kind.Progress, "Searching for <b>Maybelline New York</b>…");
            }
        }

        static Texture2D FakePhoto()
        {
            var t = new Texture2D(600, 800, TextureFormat.RGB24, false);
            var px = new Color32[600 * 800];
            for (int y = 0; y < 800; y++)
                for (int x = 0; x < 600; x++)
                {
                    bool tube = x > 220 && x < 380 && y > 120 && y < 680;
                    bool cap = x > 230 && x < 370 && y >= 560 && y < 680;
                    px[y * 600 + x] = cap ? new Color32(40, 30, 34, 255) : tube ? new Color32(196, 70, 104, 255) : new Color32((byte)(200 + y % 30), 190, 180, 255);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static void BackToTop(RectTransform canvas)
        {
            var p = RuntimeTheme.Palette;
            RectTransform area = RuntimeUiKit.Rect("Scroll", canvas);
            RuntimeUiKit.Stretch(area, 40, 40, 60, 60);
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = RuntimeUiKit.Rect("Viewport", area);
            RuntimeUiKit.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = RuntimeUiKit.Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = new Vector2(0, 0);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            string text = "## Product Photo Submissions\n";
            for (int i = 0; i < 30; i++) text += "When a product you scan is not in our catalog yet, you can choose to take one photo of it and send it to us. ";
            RuntimeUiKit.Label(content, "Policy", text, 40, p.mainText, Body, TextAlignmentOptions.TopLeft);
            scroll.content = content; scroll.viewport = viewport; scroll.horizontal = false; scroll.vertical = true;
            BackToTopButton button = BackToTopButton.Attach(scroll);
            Canvas.ForceUpdateCanvases();
            content.anchoredPosition = new Vector2(0, 1800);                       // scrolled down about a screen and a half
            var shown = button.GetComponent<CanvasGroup>();                       // what the fade reaches once scrolled down
            shown.alpha = 1f; shown.interactable = true; shown.blocksRaycasts = true;
        }
    }
}
