using UnityEngine;

namespace GlowUp.AR
{
    [CreateAssetMenu(fileName = "AR One Euro Settings", menuName = "GlowUp/AR/One Euro Settings")]
    public sealed class ArOneEuroSettings : ScriptableObject
    {
        [Tooltip("Raise beta to cut lag while moving; lower minCutoff to cut jitter while still.")]
        [Range(0.1f, 10f)] public float minCutoff = 1.2f;
        [Tooltip("Raise beta to make the filter follow fast movement more closely.")]
        [Range(0f, 40f)] public float beta = 8f;
        [Range(0.1f, 10f)] public float derivativeCutoff = 1f;
    }
}
