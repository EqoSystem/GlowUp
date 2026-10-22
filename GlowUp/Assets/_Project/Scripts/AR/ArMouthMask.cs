using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace GlowUp.AR
{
    /// <summary>
    /// AR MOUTH MASK: keeps the make-up off the inside of an open mouth (teeth and tongue).
    ///
    /// Face tracking draws the colour layers over the whole face mesh, and when the user smiles or talks the lips layer
    /// would spill onto the teeth. This script makes a small picture of where the INSIDE of the mouth is on the face mesh
    /// (from the mesh's own picture coordinates, once per face), measures how open the mouth is on every frame, and hands both
    /// to the colour layers (ArFaceMakeup.SetMouth); the shader leaves the picture bare there.
    ///
    /// ARCore gives no depth or hair/hand cut-out on phones, so teeth and tongue are what can be masked. Hair and hands are not.
    /// Every number below is an Inspector field. Only the full pipeline uses it (ArAdaptiveQuality > Full Mouth Mask).
    /// </summary>
    public class ArMouthMask : MonoBehaviour
    {
        [Header("Where the inside of the mouth is (numbers of the face mesh's points)")]
        [Tooltip("The points round the inner edge of the lips, in order round the mouth (the standard 468-point face mesh)")]
        [SerializeField] private int[] innerLipRing = { 78, 191, 80, 81, 82, 13, 312, 311, 310, 415, 308, 324, 318, 402, 317, 14, 87, 178, 88, 95 };
        [Tooltip("The middle point of the upper inner lip and of the lower inner lip: the gap between them is how open the mouth is")]
        [SerializeField] private int upperInnerLip = 13;
        [SerializeField] private int lowerInnerLip = 14;
        [Tooltip("The two corners of the mouth: the gap is compared with the mouth's width, so it does not depend on how far the phone is")]
        [SerializeField] private int leftCorner = 61;
        [SerializeField] private int rightCorner = 291;

        [Header("How open counts")]
        [Tooltip("Gap / width of the mouth below which the mouth counts as closed (nothing is cut out)")]
        [Range(0f, 0.3f)] [SerializeField] private float closedBelow = 0.03f;
        [Tooltip("Gap / width at which the whole inside of the mouth is cut out")]
        [Range(0.02f, 0.6f)] [SerializeField] private float fullyOpenAt = 0.12f;
        [Tooltip("How quickly the cut-out follows the mouth (bigger = quicker)")]
        [Min(1f)] [SerializeField] private float followSpeed = 18f;

        [Header("The mask picture")]
        [Tooltip("Its size in pixels on each side (it is made once per face)")]
        [Range(64, 512)] [SerializeField] private int maskSize = 256;
        [Tooltip("How soft the edge of the cut-out is, in pixels")]
        [Range(0, 12)] [SerializeField] private int softEdgePixels = 3;
        [Tooltip("The cut-out is made this many pixels bigger than the inner lips, so no colour is left on the teeth's edge")]
        [Range(0, 8)] [SerializeField] private int growPixels = 2;

        ArFaceMakeup makeup;
        Texture2D mask;
        bool maskBuilt;
        float open;

        /// <summary>Live tracking started with this make-up.</summary>
        public void BeginSession(ArFaceMakeup target)
        {
            makeup = target;
            maskBuilt = false; open = 0f;
        }

        /// <summary>Live tracking stopped: the layers get no mask, and the picture is released.</summary>
        public void EndSession()
        {
            if (makeup != null) makeup.SetMouth(null, 0f);
            makeup = null;
            if (mask != null) { Destroy(mask); mask = null; }
            maskBuilt = false;
        }

        /// <summary>Called on every frame while a face is tracked.</summary>
        public void Step(ARFace face)
        {
            if (makeup == null || face == null) return;
            var vertices = face.vertices;
            if (!vertices.IsCreated || vertices.Length <= Mathf.Max(upperInnerLip, lowerInnerLip, leftCorner, rightCorner)) return;
            if (!maskBuilt && !BuildMask(face)) return;

            float width = Vector3.Distance(vertices[leftCorner], vertices[rightCorner]);
            float gap = Vector3.Distance(vertices[upperInnerLip], vertices[lowerInnerLip]);
            float ratio = width > 0.0001f ? gap / width : 0f;
            float wanted = Mathf.Clamp01((ratio - closedBelow) / Mathf.Max(0.01f, fullyOpenAt - closedBelow));
            open = Mathf.Lerp(open, wanted, 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));
            makeup.SetMouth(mask, open);
        }

        /// <summary>Called when no face is tracked: the cut-out closes.</summary>
        public void Lost()
        {
            if (makeup == null || mask == null) return;
            open = Mathf.Lerp(open, 0f, 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));
            makeup.SetMouth(mask, open);
        }

        /// <summary>Draws the inside of the inner lips, in the mesh's picture coordinates, into a small grey-scale picture.</summary>
        bool BuildMask(ARFace face)
        {
            var uvs = face.uvs;
            if (!uvs.IsCreated || uvs.Length == 0 || innerLipRing == null || innerLipRing.Length < 3) return false;
            foreach (int index in innerLipRing) if (index < 0 || index >= uvs.Length) return false;

            int n = maskSize;
            var poly = new Vector2[innerLipRing.Length];
            for (int i = 0; i < poly.Length; i++) poly[i] = new Vector2(uvs[innerLipRing[i]].x * (n - 1), uvs[innerLipRing[i]].y * (n - 1));

            // fill the polygon: for every row, the crossings of the edges, in pairs
            var filled = new float[n * n];
            var crossings = new System.Collections.Generic.List<float>(16);
            for (int y = 0; y < n; y++)
            {
                crossings.Clear();
                float sy = y + 0.5f;
                for (int i = 0; i < poly.Length; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                    if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy)) crossings.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                crossings.Sort();
                for (int c = 0; c + 1 < crossings.Count; c += 2)
                {
                    int from = Mathf.Max(0, Mathf.CeilToInt(crossings[c] - 0.5f)), to = Mathf.Min(n - 1, Mathf.FloorToInt(crossings[c + 1] - 0.5f));
                    for (int x = from; x <= to; x++) filled[y * n + x] = 1f;
                }
            }

            // bigger by a few pixels, then soft: a box blur twice, with the grown area counted as full
            float[] shape = growPixels > 0 ? Dilate(filled, n, growPixels) : filled;
            for (int pass = 0; pass < 2 && softEdgePixels > 0; pass++) shape = BoxBlur(shape, n, softEdgePixels);

            var pixels = new Color32[n * n];
            for (int i = 0; i < pixels.Length; i++) { byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(shape[i]) * 255f); pixels[i] = new Color32(v, v, v, 255); }
            if (mask != null) Destroy(mask);
            mask = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "AR mouth mask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            mask.SetPixels32(pixels);
            mask.Apply(false, true);
            maskBuilt = true;
            return true;
        }

        static float[] Dilate(float[] source, int n, int radius)
        {
            var result = new float[source.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float best = 0f;
                    for (int dy = -radius; dy <= radius && best < 1f; dy++)
                    {
                        int yy = y + dy; if (yy < 0 || yy >= n) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int xx = x + dx; if (xx < 0 || xx >= n) continue;
                            if (source[yy * n + xx] > best) best = source[yy * n + xx];
                        }
                    }
                    result[y * n + x] = best;
                }
            return result;
        }

        static float[] BoxBlur(float[] source, int n, int radius)
        {
            var horizontal = new float[source.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float sum = 0f; int count = 0;
                    for (int dx = -radius; dx <= radius; dx++) { int xx = x + dx; if (xx < 0 || xx >= n) continue; sum += source[y * n + xx]; count++; }
                    horizontal[y * n + x] = sum / count;
                }
            var result = new float[source.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float sum = 0f; int count = 0;
                    for (int dy = -radius; dy <= radius; dy++) { int yy = y + dy; if (yy < 0 || yy >= n) continue; sum += horizontal[yy * n + x]; count++; }
                    result[y * n + x] = sum / count;
                }
            return result;
        }

        void OnDestroy() { if (mask != null) Destroy(mask); }
    }
}
