using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace GlowUp.AR
{
    /// <summary>
    /// AR BROW MASK: where brow colour goes on the tracked face.
    ///
    /// The other parts of the face have mask pictures made in advance. The brows are made here, from the face mesh itself:
    /// the points round each eyebrow (the standard 468-point face mesh, the same numbering ArMouthMask uses) are joined
    /// into two shapes, drawn into a small picture in the mesh's picture coordinates, grown a little and softened at the
    /// edge. It is made once per face and handed to the brow colour layer (ArFaceMakeup.SetBrowMask), so it follows the brows
    /// as the face moves, like every other layer. Every number below is an Inspector field.
    /// </summary>
    public class ArBrowMask : MonoBehaviour
    {
        [Header("Where the brows are (numbers of the face mesh's points, in order round each brow)")]
        [Tooltip("Right brow on the picture: upper edge from the outside in, then the lower edge back out")]
        [SerializeField] private int[] browOne = { 70, 63, 105, 66, 107, 55, 65, 52, 53, 46 };
        [Tooltip("The other brow, in the same way")]
        [SerializeField] private int[] browTwo = { 300, 293, 334, 296, 336, 285, 295, 282, 283, 276 };

        [Header("The mask picture")]
        [Tooltip("Its size in pixels on each side (it is made once per face)")]
        [Range(64, 512)] [SerializeField] private int maskSize = 256;
        [Tooltip("The shape is made this many pixels bigger than the points, so the colour reaches the brow hairs")]
        [Range(0, 8)] [SerializeField] private int growPixels = 2;
        [Tooltip("How soft the edge is, in pixels")]
        [Range(0, 12)] [SerializeField] private int softEdgePixels = 3;

        ArFaceMakeup makeup;
        Texture2D mask;
        bool built;

        /// <summary>Live tracking started with this make-up.</summary>
        public void BeginSession(ArFaceMakeup target) { makeup = target; built = false; }

        /// <summary>Live tracking stopped: the layer gets no mask, and the picture is released.</summary>
        public void EndSession()
        {
            if (makeup != null) makeup.SetBrowMask(null);
            makeup = null;
            if (mask != null) { Destroy(mask); mask = null; }
            built = false;
        }

        /// <summary>Called while a face is tracked: the mask is made the first time.</summary>
        public void Step(ARFace face)
        {
            if (built || makeup == null || face == null) return;
            if (!Build(face)) return;
            built = true;
            makeup.SetBrowMask(mask);
        }

        bool Build(ARFace face)
        {
            var uvs = face.uvs;
            if (!uvs.IsCreated || uvs.Length == 0) return false;
            if (!Fits(browOne, uvs.Length) || !Fits(browTwo, uvs.Length)) return false;

            int n = maskSize;
            var filled = new float[n * n];
            var crossings = new List<float>(16);
            FaceMaskPainter.FillPolygon(filled, n, Polygon(browOne, uvs, n), crossings);
            FaceMaskPainter.FillPolygon(filled, n, Polygon(browTwo, uvs, n), crossings);
            float[] shape = FaceMaskPainter.Dilate(filled, n, growPixels);
            for (int pass = 0; pass < 2 && softEdgePixels > 0; pass++) shape = FaceMaskPainter.BoxBlur(shape, n, softEdgePixels);

            if (mask != null) Destroy(mask);
            mask = FaceMaskPainter.ToAlphaTexture(shape, n, "AR brow mask");
            return true;
        }

        static bool Fits(int[] ring, int count)
        {
            if (ring == null || ring.Length < 3) return false;
            foreach (int index in ring) if (index < 0 || index >= count) return false;
            return true;
        }

        static Vector2[] Polygon(int[] ring, Unity.Collections.NativeArray<Vector2> uvs, int n)
        {
            var poly = new Vector2[ring.Length];
            for (int i = 0; i < ring.Length; i++) poly[i] = new Vector2(uvs[ring[i]].x * (n - 1), uvs[ring[i]].y * (n - 1));
            return poly;
        }

        void OnDestroy() { if (mask != null) Destroy(mask); }
    }
}
