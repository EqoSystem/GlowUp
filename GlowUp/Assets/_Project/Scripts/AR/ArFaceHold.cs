using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GlowUp.AR
{
    /// <summary>
    /// AR FACE HOLD: a face that is lost for a moment does not vanish.
    ///
    /// Face tracking loses the face when the user turns the head far to the side, tilts it, or the room is dark. Without this,
    /// the colour disappears at that very moment and comes back with a jump. With it:
    ///  1. LAST KNOWN GOOD: the face stays on screen where it was seen last, fully shown for "Hold Seconds",
    ///  2. PREDICTION: during the first "Predict Seconds" it keeps moving the way it was moving (slowing down), which bridges a quick turn,
    ///  3. FADE: then the make-up fades out gently over "Fade Seconds", instead of switching off,
    ///  4. when the face is found again, the make-up fades back in.
    /// When the tracker removes the face altogether, a copy of its last mesh (a "ghost") is kept for the same time.
    /// The numbers are Inspector fields. Only the full pipeline uses it (ArAdaptiveQuality > Hold Lost Face).
    /// </summary>
    public class ArFaceHold : MonoBehaviour
    {
        [Header("Keeping a lost face")]
        [Tooltip("Seconds the make-up stays fully shown after the face was lost")]
        [Min(0f)] [SerializeField] private float holdSeconds = 0.2f;
        [Tooltip("Seconds the make-up then takes to fade out")]
        [Min(0.05f)] [SerializeField] private float fadeSeconds = 0.6f;
        [Tooltip("Seconds the make-up takes to fade in when the face is found again")]
        [Min(0.01f)] [SerializeField] private float fadeInSeconds = 0.15f;

        [Header("Following the head while it is lost")]
        [Tooltip("On = the face keeps moving the way it moved, for a moment, while it is lost")]
        [SerializeField] private bool predictMovement = true;
        [Tooltip("The prediction stops after this many seconds")]
        [Min(0f)] [SerializeField] private float predictSeconds = 0.4f;
        [Tooltip("How quickly the measured speed follows the real movement (bigger = quicker, jumpier)")]
        [Min(1f)] [SerializeField] private float speedSmoothing = 14f;
        [Tooltip("How quickly the predicted movement slows down (bigger = stops sooner)")]
        [Min(0f)] [SerializeField] private float slowDown = 3f;

        /// <summary>True while the face is lost and the make-up is still shown (held or fading).</summary>
        public bool IsHolding { get; private set; }
        public bool PredictionEnabled => predictMovement;

        public void SetPredictionEnabled(bool value) { predictMovement = value; }

        ARFaceManager manager;
        ArFaceMakeup makeup;
        Transform heldTransform;                // the face (or its ghost) that is shown while it is lost
        MeshRenderer heldRenderer;
        GameObject ghost;                       // a copy of the last mesh, when the tracker removed the real face
        Mesh ghostMesh;
        float lastGoodAt = -100f, presence;
        Vector3 lastPosition, velocity;
        Quaternion lastRotation = Quaternion.identity;
        Vector3 spin;                           // axis x radians per second
        bool haveLast;

        /// <summary>Live tracking started.</summary>
        public void BeginSession(ARFaceManager faceManager, ArFaceMakeup target)
        {
            manager = faceManager; makeup = target;
            heldTransform = null; heldRenderer = null; presence = 0f; haveLast = false; IsHolding = false; lastGoodAt = -100f;
        }

        /// <summary>Live tracking stopped: everything is put back and a ghost is thrown away.</summary>
        public void EndSession()
        {
            DropGhost();
            heldTransform = null; heldRenderer = null; IsHolding = false;
            if (makeup != null) makeup.Presence = 1f;
            makeup = null; manager = null;
        }

        /// <summary>
        /// The face manager reported changes. A face that is REMOVED while it was seen a moment ago leaves a ghost: a copy of its
        /// mesh, with the same materials, that is held and faded like a lost face. A new face replaces the ghost.
        /// </summary>
        public void Observe(ARFacesChangedEventArgs args)
        {
            if (manager == null) return;
            if (args.added != null && args.added.Count > 0) DropGhost();
            if (args.removed == null) return;
            foreach (ARFace gone in args.removed)
            {
                if (gone == null || heldTransform != gone.transform) continue;
                if (Time.unscaledTime - lastGoodAt > holdSeconds + fadeSeconds) continue;
                MakeGhost(gone);
                break;
            }
        }

        void MakeGhost(ARFace gone)
        {
            DropGhost();
            if (!gone.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !gone.TryGetComponent(out MeshRenderer renderer)) { heldTransform = null; heldRenderer = null; return; }
            ghost = new GameObject("AR face (held for a moment)");
            ghost.transform.SetParent(gone.transform.parent, false);
            ghost.transform.SetPositionAndRotation(gone.transform.position, gone.transform.rotation);
            ghost.transform.localScale = gone.transform.localScale;
            ghostMesh = Instantiate(filter.sharedMesh);
            ghost.AddComponent<MeshFilter>().sharedMesh = ghostMesh;
            MeshRenderer copy = ghost.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copy.receiveShadows = false;
            heldTransform = ghost.transform; heldRenderer = copy;
        }

        void DropGhost()
        {
            if (ghost != null) Destroy(ghost);
            if (ghostMesh != null) Destroy(ghostMesh);
            ghost = null; ghostMesh = null;
            if (heldTransform != null && heldTransform.gameObject == null) heldTransform = null;
        }

        /// <summary>Once per frame, after the face manager has updated. Returns true while a face (tracked or held) is on screen.</summary>
        public bool Step()
        {
            if (manager == null || makeup == null) return false;
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            ARFace best = null;
            foreach (ARFace candidate in manager.trackables)
                if (candidate != null && candidate.trackingState == TrackingState.Tracking) { best = candidate; break; }

            if (best != null)
            {
                if (ghost != null) DropGhost();
                heldTransform = best.transform; heldRenderer = best.TryGetComponent(out MeshRenderer r) ? r : null;
                lastGoodAt = Time.unscaledTime; IsHolding = false;
                Transform t = best.transform;
                if (haveLast)
                {
                    float k = 1f - Mathf.Exp(-speedSmoothing * dt);
                    velocity = Vector3.Lerp(velocity, (t.position - lastPosition) / dt, k);
                    Quaternion delta = t.rotation * Quaternion.Inverse(lastRotation);
                    delta.ToAngleAxis(out float degrees, out Vector3 axis);
                    if (degrees > 180f) degrees -= 360f;
                    if (float.IsNaN(axis.x) || axis.sqrMagnitude < 0.0001f) { axis = Vector3.up; degrees = 0f; }
                    spin = Vector3.Lerp(spin, axis.normalized * (degrees * Mathf.Deg2Rad / dt), k);
                }
                lastPosition = t.position; lastRotation = t.rotation; haveLast = true;
                presence = Mathf.MoveTowards(presence, 1f, dt / Mathf.Max(0.01f, fadeInSeconds));
                makeup.Presence = presence;
                return true;
            }

            if (heldTransform == null) { presence = Mathf.MoveTowards(presence, 0f, dt / Mathf.Max(0.05f, fadeSeconds)); makeup.Presence = presence; IsHolding = false; return false; }

            float lost = Time.unscaledTime - lastGoodAt;
            if (lost > holdSeconds + fadeSeconds)
            {
                presence = 0f; makeup.Presence = 0f; IsHolding = false;
                DropGhost(); heldTransform = null; heldRenderer = null;
                return false;
            }

            IsHolding = true;
            presence = lost <= holdSeconds ? 1f : 1f - (lost - holdSeconds) / fadeSeconds;
            makeup.Presence = Mathf.Clamp01(presence);

            // the make-up is still drawn although the tracker says "lost" (its own script switches the mesh off then)
            if (heldRenderer != null && !heldRenderer.enabled) heldRenderer.enabled = true;
            if (predictMovement && lost <= predictSeconds)
            {
                float ease = Mathf.Exp(-slowDown * lost);
                heldTransform.position += velocity * dt * ease;
                float angle = spin.magnitude * dt * ease;
                if (angle > 0.00001f) heldTransform.rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, spin.normalized) * heldTransform.rotation;
            }
            return true;
        }

        void OnDestroy() { DropGhost(); }
    }
}
