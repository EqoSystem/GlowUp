using UnityEngine;

namespace GlowUp.SafeAreaFix
{
    /// <summary>Marks a canvas or a canvas child the safe-area structure must leave alone (the debug overlay, special full-bleed layers).</summary>
    [DisallowMultipleComponent]
    public class SafeAreaIgnore : MonoBehaviour { }
}
