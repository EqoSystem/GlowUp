using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>Turns its object while it is active (the spinner of a busy button or of a status message).</summary>
    public class Spinner : MonoBehaviour
    {
        public float degreesPerSecond = 320f;
        void Update() => transform.Rotate(0f, 0f, -degreesPerSecond * Time.unscaledDeltaTime);
    }
}
