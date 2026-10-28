using System.Collections.Generic;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// Pure logic of the guided try-on that follows the face (ArGuidedFace): where each part of the face is on the standard 468-point face mesh,
    /// the mask pictures of the make-up layers (made once, in the mesh's own picture coordinates, so the colour moves and stretches with the face),
    /// the triangles that are left out (the inside of the mouth and the eyes: never painted), and the mapping of a point the face finder found onto
    /// the camera view on screen. No Unity object is touched here, so all of it is checked by EditMode tests (GuidedFaceTests).
    ///
    /// The point numbers are MediaPipe's (the same numbering ARCore's face mesh uses; ArMouthMask and ArBrowMask rely on it too).
    /// A mask is a grey value per pixel, 0 to 1, n x n, row by row from the BOTTOM (FaceMaskPainter's order).
    /// </summary>
    public static class GuidedFaceShapes
    {
        // ---- where the parts are, as rings of point numbers ----------------------------------------------------------------
        public static readonly int[] LipsOuter = { 61, 146, 91, 181, 84, 17, 314, 405, 321, 375, 291, 409, 270, 269, 267, 0, 37, 39, 40, 185 };
        public static readonly int[] LipsInner = { 78, 95, 88, 178, 87, 14, 317, 402, 318, 324, 308, 415, 310, 311, 312, 13, 82, 81, 80, 191 };
        public static readonly int[] FaceOval = { 10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148,
                                                  176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109 };
        public static readonly int[] EyeOne = { 33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246 };
        public static readonly int[] EyeTwo = { 263, 249, 390, 373, 374, 380, 381, 382, 362, 398, 384, 385, 386, 387, 388, 466 };
        /// <summary>The upper eyelid edge, from the outer corner to the inner one.</summary>
        public static readonly int[] UpperLidOne = { 33, 246, 161, 160, 159, 158, 157, 173, 133 };
        public static readonly int[] UpperLidTwo = { 263, 466, 388, 387, 386, 385, 384, 398, 362 };
        /// <summary>The lower edge of each brow, from the outside in.</summary>
        public static readonly int[] BrowLowerOne = { 46, 53, 52, 65, 55 };
        public static readonly int[] BrowLowerTwo = { 276, 283, 282, 295, 285 };
        /// <summary>Round each brow: the upper edge from the outside in, then the lower edge back out (ArBrowMask's rings).</summary>
        public static readonly int[] BrowOne = { 70, 63, 105, 66, 107, 55, 65, 52, 53, 46 };
        public static readonly int[] BrowTwo = { 300, 293, 334, 296, 336, 285, 295, 282, 283, 276 };
        /// <summary>The middle of each cheek and the top of its cheekbone.</summary>
        public const int CheekOne = 205, CheekboneOne = 50, CheekTwo = 425, CheekboneTwo = 280;
        /// <summary>Points where the skin is measured (forehead, both cheeks, chin): no lips, eyes or brows.</summary>
        public static readonly int[] SkinPoints = { 151, 50, 280, 199 };
        /// <summary>The two sides of the face (to measure its width on screen).</summary>
        public const int FaceSideOne = 234, FaceSideTwo = 454;

        /// <summary>The order of the make-up layers (the same as ArFaceMakeup's): face, cheeks, eyes, lips, brows.</summary>
        public const int Face = 0, Cheeks = 1, Eyes = 2, Lips = 3, Brows = 4, LayerCount = 5;

        // ---- masks ----------------------------------------------------------------------------------------------------------
        /// <summary>The colour mask of one layer, n x n. `uvs` = FaceMeshData.Uvs (u, v per point).</summary>
        public static float[] ColourMask(int layer, int n, float[] uvs)
        {
            var crossings = new List<float>(32);
            switch (layer)
            {
                case Face:
                {
                    // the skin of the face: the oval, with its edge pulled in and softened (no colour on the hairline or the background),
                    // without the eyes, the lips and the brows
                    var oval = Fill(n, uvs, crossings, FaceOval);
                    oval = Erode(oval, n, Mathf.Max(1, n / 64));
                    var eyes = Fill(n, uvs, crossings, EyeOne, EyeTwo);
                    var holes = Union(Dilate(eyes, n, Mathf.Max(1, n / 85)),
                                      Dilate(Fill(n, uvs, crossings, LipsOuter), n, Mathf.Max(1, n / 128)),
                                      Fill(n, uvs, crossings, BrowOne, BrowTwo));
                    // softened for a gentle edge, then the eyes are cut out again: the blur must not put foundation on the eye itself
                    return Subtract(Soften(Subtract(oval, holes), n, Mathf.Max(1, n / 40)), eyes);
                }
                case Cheeks:
                {
                    var shape = new float[n * n];
                    Blob(shape, n, CheekCentre(uvs, CheekOne, CheekboneOne), 0.085f, 0.065f);
                    Blob(shape, n, CheekCentre(uvs, CheekTwo, CheekboneTwo), 0.085f, 0.065f);
                    return shape;
                }
                case Eyes:
                {
                    var lids = new float[n * n];
                    FaceMaskPainter.FillPolygon(lids, n, LidPolygon(uvs, n, UpperLidOne, BrowLowerOne, 0.55f), crossings);
                    FaceMaskPainter.FillPolygon(lids, n, LidPolygon(uvs, n, UpperLidTwo, BrowLowerTwo, 0.55f), crossings);
                    var eyes = Fill(n, uvs, crossings, EyeOne, EyeTwo);
                    return Subtract(Soften(lids, n, Mathf.Max(1, n / 85)), eyes);
                }
                case Lips:
                {
                    // the lips are a ring: the inside of the mouth stays bare, so teeth and tongue are never painted when the mouth opens
                    var outer = Fill(n, uvs, crossings, LipsOuter);
                    var inner = Fill(n, uvs, crossings, LipsInner);
                    var ring = Soften(Subtract(Dilate(outer, n, Mathf.Max(1, n / 256)), inner), n, Mathf.Max(1, n / 128));
                    return Subtract(ring, inner);
                }
                case Brows:
                    return Soften(Dilate(Fill(n, uvs, crossings, BrowOne, BrowTwo), n, Mathf.Max(1, n / 128)), n, Mathf.Max(1, n / 85));
                default:
                    return new float[n * n];
            }
        }

        /// <summary>Where the highlight of a finish (satin, glossy, shimmer, metallic) sits on a layer, n x n: the places light catches.</summary>
        public static float[] HighlightMask(int layer, int n, float[] uvs)
        {
            var shape = new float[n * n];
            switch (layer)
            {
                case Face:
                    Blob(shape, n, Uv(uvs, 6), 0.025f, 0.06f);                                     // nose bridge
                    Blob(shape, n, Uv(uvs, 151), 0.07f, 0.04f);                                    // forehead
                    Blob(shape, n, CheekCentre(uvs, CheekOne, CheekboneOne, 0.85f), 0.05f, 0.03f);  // cheekbones
                    Blob(shape, n, CheekCentre(uvs, CheekTwo, CheekboneTwo, 0.85f), 0.05f, 0.03f);
                    break;
                case Cheeks:
                    Blob(shape, n, CheekCentre(uvs, CheekOne, CheekboneOne, 0.75f), 0.05f, 0.03f);
                    Blob(shape, n, CheekCentre(uvs, CheekTwo, CheekboneTwo, 0.75f), 0.05f, 0.03f);
                    break;
                case Eyes:
                    Blob(shape, n, Vector2.Lerp(Uv(uvs, 159), Uv(uvs, 52), 0.3f), 0.03f, 0.015f);
                    Blob(shape, n, Vector2.Lerp(Uv(uvs, 386), Uv(uvs, 282), 0.3f), 0.03f, 0.015f);
                    break;
                case Lips:
                    Blob(shape, n, Vector2.Lerp(Uv(uvs, 14), Uv(uvs, 17), 0.45f), 0.04f, 0.012f);     // the middle of the lower lip
                    Blob(shape, n, Vector2.Lerp(Uv(uvs, 13), Uv(uvs, 0), 0.55f), 0.025f, 0.007f);     // a smaller one on the upper lip
                    break;
            }
            return Multiply(shape, ColourMask(layer, n, uvs));
        }

        // ---- triangles ------------------------------------------------------------------------------------------------------
        /// <summary>The face mesh's triangles without those inside the mouth and the eyes (all three corners on the inner lip or on an eye's rim).</summary>
        public static int[] TrianglesWithoutHoles(int[] triangles)
        {
            var mouth = new HashSet<int>(LipsInner);
            var eyeOne = new HashSet<int>(EyeOne);
            var eyeTwo = new HashSet<int>(EyeTwo);
            var kept = new List<int>(triangles.Length);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (AllIn(mouth, a, b, c) || AllIn(eyeOne, a, b, c) || AllIn(eyeTwo, a, b, c)) continue;
                kept.Add(a); kept.Add(b); kept.Add(c);
            }
            return kept.ToArray();
        }

        static bool AllIn(HashSet<int> set, int a, int b, int c) => set.Contains(a) && set.Contains(b) && set.Contains(c);

        // ---- the camera view ------------------------------------------------------------------------------------------------
        /// <summary>
        /// A point the face finder gave (shares of the upright camera view from its TOP LEFT) on the view rectangle (y up). `mirrored` = the
        /// view is shown like a mirror (the selfie camera): the finder looked at the picture as the camera took it, so x turns round.
        /// </summary>
        public static Vector2 ToView(float x, float y, Rect view, bool mirrored)
        {
            return new Vector2(view.xMin + (mirrored ? 1f - x : x) * view.width, view.yMax - y * view.height);
        }

        /// <summary>True when the finder's answer holds a whole face (468 x 3 numbers).</summary>
        public static bool IsFace(float[] points) => points != null && points.Length >= FaceMeshData.PointCount * 3;

        // ---- helpers ----------------------------------------------------------------------------------------------------------
        public static Vector2 Uv(float[] uvs, int point) => new Vector2(uvs[point * 2], uvs[point * 2 + 1]);

        static Vector2 CheekCentre(float[] uvs, int cheek, int cheekbone, float towardsBone = 0.35f) => Vector2.Lerp(Uv(uvs, cheek), Uv(uvs, cheekbone), towardsBone);

        static float[] Fill(int n, float[] uvs, List<float> crossings, params int[][] rings)
        {
            var shape = new float[n * n];
            foreach (int[] ring in rings)
            {
                var polygon = new Vector2[ring.Length];
                for (int i = 0; i < ring.Length; i++) polygon[i] = Uv(uvs, ring[i]) * n;
                FaceMaskPainter.FillPolygon(shape, n, polygon, crossings);
            }
            return shape;
        }

        /// <summary>The eyelid: from the lid's edge up to a crease part of the way to the brow's lower edge.</summary>
        static Vector2[] LidPolygon(float[] uvs, int n, int[] lid, int[] browLower, float towardsBrow)
        {
            var polygon = new Vector2[lid.Length * 2];
            for (int i = 0; i < lid.Length; i++)
            {
                Vector2 edge = Uv(uvs, lid[i]);
                float along = lid.Length > 1 ? i / (float)(lid.Length - 1) * (browLower.Length - 1) : 0f;
                int below = Mathf.FloorToInt(along), above = Mathf.Min(browLower.Length - 1, below + 1);
                Vector2 brow = Vector2.Lerp(Uv(uvs, browLower[below]), Uv(uvs, browLower[above]), along - below);
                // the corners of the eye get a thinner lid than its middle, as eye shadow is put on
                float bulge = Mathf.Sin(Mathf.PI * (lid.Length > 1 ? i / (float)(lid.Length - 1) : 0.5f));
                polygon[i] = edge * n;
                polygon[polygon.Length - 1 - i] = Vector2.Lerp(edge, brow, towardsBrow * (0.35f + 0.65f * bulge)) * n;
            }
            return polygon;
        }

        /// <summary>Adds a soft oval (centre and radii in UV) to the shape, keeping the larger value.</summary>
        static void Blob(float[] shape, int n, Vector2 centre, float radiusU, float radiusV)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((centre.x - radiusU * 1.6f) * n)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((centre.x + radiusU * 1.6f) * n));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((centre.y - radiusV * 1.6f) * n)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt((centre.y + radiusV * 1.6f) * n));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = ((x + 0.5f) / n - centre.x) / radiusU, dy = ((y + 0.5f) / n - centre.y) / radiusV;
                    float value = Mathf.Exp(-1.6f * (dx * dx + dy * dy));
                    if (value < 0.02f) continue;
                    if (value > shape[y * n + x]) shape[y * n + x] = value;
                }
        }

        static float[] Dilate(float[] shape, int n, int radius) => FaceMaskPainter.Dilate(shape, n, radius);

        static float[] Erode(float[] shape, int n, int radius)
        {
            var inverse = new float[shape.Length];
            for (int i = 0; i < shape.Length; i++) inverse[i] = 1f - shape[i];
            var grown = FaceMaskPainter.Dilate(inverse, n, radius);
            for (int i = 0; i < grown.Length; i++) grown[i] = 1f - grown[i];
            return grown;
        }

        static float[] Soften(float[] shape, int n, int radius)
        {
            float[] result = shape;
            for (int pass = 0; pass < 2; pass++) result = FaceMaskPainter.BoxBlur(result, n, radius);
            return result;
        }

        static float[] Union(params float[][] shapes)
        {
            var result = new float[shapes[0].Length];
            foreach (float[] s in shapes) for (int i = 0; i < result.Length; i++) if (s[i] > result[i]) result[i] = s[i];
            return result;
        }

        static float[] Subtract(float[] shape, float[] cut)
        {
            var result = new float[shape.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Mathf.Clamp01(shape[i] * (1f - Mathf.Clamp01(cut[i])));
            return result;
        }

        static float[] Multiply(float[] a, float[] b)
        {
            var result = new float[a.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Mathf.Clamp01(a[i] * Mathf.Clamp01(b[i] * 1.5f));
            return result;
        }
    }

    /// <summary>
    /// How big a picture the face finder gets and how often, so the try-on keeps up on every phone. It starts from the phone's AR pipeline
    /// (light or full, ArAdaptiveQuality) and then follows the time the finder really takes: slower than SlowMs, a smaller picture (and on the
    /// smallest, fewer looks a second); quicker than FastMs, a larger one, up to what the pipeline allows. Pure logic (GuidedFaceTests).
    /// </summary>
    public sealed class GuidedFacePace
    {
        /// <summary>The picture's longer side, in pixels, from the lightest to the heaviest.</summary>
        public int[] Sizes = { 192, 256, 320, 384 };
        public float SlowMs = 45f, FastMs = 18f;
        /// <summary>Looks to wait between two changes, so one slow picture does not decide.</summary>
        public int Patience = 8;
        /// <summary>Looks per second at most on the smallest picture, or in the light pipeline (0 = every camera picture).</summary>
        public float SlowestRate = 15f;

        int index, maxIndex, sinceChange;
        float average;
        bool light;

        /// <summary>A new visit: the light pipeline starts on the second size and never goes above the third; the full one starts on the largest.</summary>
        public void Begin(bool lightPipeline)
        {
            light = lightPipeline;
            maxIndex = light ? Mathf.Min(2, Sizes.Length - 1) : Sizes.Length - 1;
            index = light ? Mathf.Min(1, maxIndex) : maxIndex;
            sinceChange = 0; average = 0f;
        }

        /// <summary>The time the last look took. Returns true when the size changed.</summary>
        public bool Report(float milliseconds)
        {
            if (milliseconds <= 0f) return false;
            average = sinceChange == 0 ? milliseconds : Mathf.Lerp(average, milliseconds, 0.25f);
            sinceChange++;
            if (sinceChange < Patience) return false;
            if (average > SlowMs && index > 0) { index--; sinceChange = 0; return true; }
            if (average < FastMs && index < maxIndex) { index++; sinceChange = 0; return true; }
            return false;
        }

        public int MaxSide => Sizes[index];
        /// <summary>Seconds to wait between two looks (0 = look at every new camera picture as soon as the finder is free).</summary>
        public float MinInterval => (light || index == 0) && SlowestRate > 0f ? 1f / SlowestRate : 0f;
        public float AverageMs => average;
    }
}
