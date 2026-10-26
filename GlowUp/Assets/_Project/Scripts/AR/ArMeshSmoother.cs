using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace GlowUp.AR
{
    /// <summary>
    /// AR MESH SMOOTHER: takes the small trembling out of the tracked face mesh, so lip colour does not shimmer at its edges.
    ///
    /// The face tracker re-measures the 468 points of the face on every camera picture, and each measurement is a little off,
    /// most visibly round the lips. This script runs after the tracker has updated the mesh and replaces each point by a
    /// One Euro filtered version of it (OneEuroFilter.cs): still points are smoothed strongly, points that move fast (the mouth
    /// opening, a smile) are followed closely, so the colour does not lag. Only the SHAPE is smoothed; the head's position
    /// is not, so the colour never trails behind the face. Only the full pipeline uses it (ArAdaptiveQuality > Full Mesh
    /// Smoothing). No memory is allocated per frame.
    /// </summary>
    public class ArMeshSmoother : MonoBehaviour
    {
        [Tooltip("Off = the mesh is used exactly as the tracker gives it")]
        [SerializeField] private bool smooth = true;
        [Tooltip("How smooth a still face is (Hz). Lower = smoother, but a slower start when it moves.")]
        [Range(0.1f, 10f)] [SerializeField] private float minCutoff = 1.5f;
        [Tooltip("How closely fast movement is followed. Higher = less lag on a fast mouth movement.")]
        [Range(0f, 1000f)] [SerializeField] private float beta = 150f;
        [Tooltip("How smooth the speed estimate is (Hz)")]
        [Range(0.1f, 10f)] [SerializeField] private float derivativeCutoff = 1f;

        readonly OneEuroVertexFilter filter = new OneEuroVertexFilter();
        Vector3[] raw = new Vector3[0];
        ARFace face;
        MeshFilter meshFilter;
        bool running;

        /// <summary>True while it smooths a face.</summary>
        public bool Active => running && smooth && face != null;

        /// <summary>Live tracking started.</summary>
        public void BeginSession() { running = true; face = null; meshFilter = null; filter.Reset(); }

        /// <summary>Live tracking stopped.</summary>
        public void EndSession() { running = false; face = null; meshFilter = null; filter.Reset(); }

        /// <summary>The face to smooth this frame (null = none tracked).</summary>
        public void Follow(ARFace tracked)
        {
            if (tracked == face) return;
            face = tracked;
            meshFilter = tracked != null && tracked.TryGetComponent(out MeshFilter mf) ? mf : null;
            filter.Reset();
        }

        void LateUpdate()
        {
            if (!running || !smooth || face == null) return;
            if (meshFilter == null && !face.TryGetComponent(out meshFilter)) return;
            Mesh mesh = meshFilter.sharedMesh;
            NativeArray<Vector3> vertices = face.vertices;
            if (mesh == null || !vertices.IsCreated) return;
            int n = vertices.Length;
            if (n == 0 || mesh.vertexCount != n) return;
            if (raw.Length < n) raw = new Vector3[n];
            NativeArray<Vector3>.Copy(vertices, raw, n);

            filter.minCutoff = minCutoff; filter.beta = beta; filter.derivativeCutoff = derivativeCutoff;
            filter.Step(raw, n, Time.unscaledDeltaTime);
            mesh.SetVertices(filter.Values, 0, n);
        }
    }
}
