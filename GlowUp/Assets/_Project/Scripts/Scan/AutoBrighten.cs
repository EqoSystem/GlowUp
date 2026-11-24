using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// "Auto brighten" sun button on a live camera view (24 Nov 2026, owner): the first tap switches it on, the next switches it off.
    /// While it is on, the live picture is lifted by a soft white veil (so a dim room is easier to see), and a photo taken meanwhile
    /// can be evened out with <see cref="Enhance"/> (stretch of the light range plus a gamma lift; nothing is invented).
    /// The selfie tone is always read from the untouched photo: brightening changes colours and must never decide the skin tone.
    /// </summary>
    public class AutoBrighten : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image icon;
        [SerializeField] private Image background;
        [SerializeField] private Image veil;                 // white, over the live picture, under the buttons
        [SerializeField] private float veilAlpha = 0.22f;

        static readonly Color Off = new Color(1f, 1f, 1f, 1f), OnTint = new Color(1f, 0.86f, 0.5f, 1f);

        public bool On { get; private set; }
        public event Action<bool> Changed;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(Toggle);
            Paint();
        }

        public void Toggle() { SetOn(!On); }

        public void SetOn(bool on)
        {
            if (On == on) return;
            On = on;
            Paint();
            Changed?.Invoke(On);
        }

        /// <summary>The veil belongs to the LIVE picture only: it is hidden while a taken photo is on screen.</summary>
        public void ShowVeil(bool live)
        {
            if (veil != null) veil.gameObject.SetActive(On && live);
        }

        void Paint()
        {
            if (icon != null) icon.color = On ? OnTint : Off;
            if (background != null) background.color = On ? new Color(0.35f, 0.27f, 0.12f, 0.78f) : new Color(0f, 0f, 0f, 0.5f);
            if (veil != null)
            {
                veil.color = new Color(1f, 1f, 1f, veilAlpha);
                veil.raycastTarget = false;
                veil.gameObject.SetActive(On);
            }
        }

        /// <summary>A brighter copy of the picture (the source is left as it is). Dim pictures are lifted; a bright one is hardly touched.</summary>
        public static Texture2D Enhanced(Texture2D source)
        {
            if (source == null) return null;
            Color32[] pixels = source.GetPixels32();
            Stretch(pixels);
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.SetPixels32(pixels); copy.Apply();
            return copy;
        }

        /// <summary>Evens the picture out where it stands.</summary>
        public static void Enhance(Texture2D picture)
        {
            if (picture == null) return;
            Color32[] pixels = picture.GetPixels32();
            Stretch(pixels);
            picture.SetPixels32(pixels); picture.Apply();
        }

        static void Stretch(Color32[] pixels)
        {
            var histogram = new int[256];
            long total = 0, sum = 0;
            for (int i = 0; i < pixels.Length; i += 7)
            {
                int l = (pixels[i].r * 54 + pixels[i].g * 183 + pixels[i].b * 19) >> 8;
                histogram[l]++; total++; sum += l;
            }
            if (total == 0) return;
            int low = Percentile(histogram, total, 0.02f), high = Percentile(histogram, total, 0.98f);
            if (high - low < 24) { low = Mathf.Max(0, low - 12); high = Mathf.Min(255, low + 24); }
            float mean = Mathf.Clamp01(((sum / (float)total) - low) / Mathf.Max(1, high - low));
            float gamma = mean < 0.5f ? Mathf.Clamp(Mathf.Log(0.5f) / Mathf.Log(Mathf.Max(0.05f, mean)), 0.45f, 1f) : 1f;
            var table = new byte[256];
            for (int v = 0; v < 256; v++)
            {
                float t = Mathf.Clamp01((v - low) / (float)Mathf.Max(1, high - low));
                table[v] = (byte)Mathf.RoundToInt(Mathf.Pow(t, gamma) * 255f);
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i].r = table[pixels[i].r]; pixels[i].g = table[pixels[i].g]; pixels[i].b = table[pixels[i].b];
            }
        }

        static int Percentile(int[] histogram, long total, float p)
        {
            long goal = (long)(total * p), seen = 0;
            for (int v = 0; v < 256; v++) { seen += histogram[v]; if (seen >= goal) return v; }
            return 255;
        }
    }
}
