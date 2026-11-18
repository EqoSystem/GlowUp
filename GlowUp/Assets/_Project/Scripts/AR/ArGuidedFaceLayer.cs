using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AR
{
    /// <summary>
    /// One make-up layer of the guided try-on that follows the face (ArGuidedFace): the 468 points the face finder found, joined into the
    /// face mesh's triangles and drawn over the camera picture with the layer's mask picture. Its colour is the shade (alpha = strength).
    /// It is an ordinary UI element, so the before/after split (a RectMask2D) cuts it like the other guided shapes.
    /// Made at run time by ArGuidedFace; nothing has to be set up in the scene.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class ArGuidedFaceLayer : MaskableGraphic
    {
        Texture mask;
        Vector3[] points;
        Vector2[] uvs;
        Vector2[] cameraUvs;
        int[] triangles;

        public override Texture mainTexture => mask != null ? mask : s_WhiteTexture;

        /// <summary>The mask picture (its alpha = where the colour shows), the mesh's UVs and the triangles to draw.</summary>
        public void Setup(Texture maskPicture, Vector2[] meshUvs, int[] meshTriangles)
        {
            mask = maskPicture;
            uvs = meshUvs;
            triangles = meshTriangles;
            raycastTarget = false;
            SetMaterialDirty();
            SetVerticesDirty();
        }

        /// <summary>The camera picture's coordinates of every point (second texture coordinate), for a layer that reads the picture (skin smoothing).</summary>
        public void SetCameraUvs(Vector2[] cameraPictureUvs)
        {
            cameraUvs = cameraPictureUvs;
            SetVerticesDirty();
        }

        /// <summary>Another mask picture (the highlight follows the part being tried).</summary>
        public void SetMask(Texture maskPicture)
        {
            if (mask == maskPicture) return;
            mask = maskPicture;
            SetMaterialDirty();
        }

        /// <summary>The points, in this object's own coordinates (the same array may be handed again after it changed).</summary>
        public void SetPoints(Vector3[] localPoints)
        {
            points = localPoints;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points == null || uvs == null || triangles == null || points.Length < uvs.Length) return;
            Color32 c = color;
            for (int i = 0; i < uvs.Length; i++)
            {
                if (cameraUvs != null && cameraUvs.Length >= uvs.Length) vh.AddVert(points[i], c, uvs[i], cameraUvs[i], Vector4.zero, Vector4.zero, Vector3.back, new Vector4(1f, 0f, 0f, -1f));
                else vh.AddVert(points[i], c, uvs[i]);
            }
            for (int t = 0; t + 2 < triangles.Length; t += 3) vh.AddTriangle(triangles[t], triangles[t + 1], triangles[t + 2]);
        }
    }
}
