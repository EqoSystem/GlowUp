using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// ONE EURO FILTER (Casiez, Roussel and Vogel, CHI 2012): smooths a jittery signal without making fast movement lag.
    ///
    /// It is a low-pass filter whose cut-off frequency rises with the speed of the signal:
    ///   - when the face is still, the cut-off is low (Min Cutoff), so the small trembling of the tracker is smoothed away;
    ///   - when the face moves fast (the mouth opens, a smile), the cut-off rises by Beta x speed, so the colour keeps up.
    /// Pure logic, no Unity objects: tested in Editor/Tests/ArUpgradeTests.cs.
    /// </summary>
    public sealed class OneEuroFilter
    {
        /// <summary>The cut-off (Hz) while the signal is still. Lower = smoother, but more lag when it starts moving.</summary>
        public float minCutoff = 1.5f;
        /// <summary>How much the cut-off rises with speed. Higher = less lag on fast movement, less smoothing of it.</summary>
        public float beta = 150f;
        /// <summary>The cut-off (Hz) of the speed estimate itself.</summary>
        public float derivativeCutoff = 1f;

        float value, speed;
        bool started;

        public OneEuroFilter() { }
        public OneEuroFilter(float minCutoff, float beta, float derivativeCutoff) { this.minCutoff = minCutoff; this.beta = beta; this.derivativeCutoff = derivativeCutoff; }

        public void Reset() { started = false; value = speed = 0f; }

        /// <summary>Feeds one new reading taken <paramref name="dt"/> seconds after the previous one; returns the smoothed value.</summary>
        public float Step(float reading, float dt)
        {
            if (!started || dt <= 0f) { value = reading; speed = 0f; started = true; return value; }
            float rawSpeed = (reading - value) / dt;
            speed = Mathf.Lerp(speed, rawSpeed, Alpha(derivativeCutoff, dt));
            float cutoff = minCutoff + beta * Mathf.Abs(speed);
            value = Mathf.Lerp(value, reading, Alpha(cutoff, dt));
            return value;
        }

        /// <summary>The smoothing factor of a low-pass filter with this cut-off (Hz) for a step of dt seconds (0 = keep the old value, 1 = take the new one).</summary>
        public static float Alpha(float cutoffHz, float dt)
        {
            if (dt <= 0f) return 1f;
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(0.0001f, cutoffHz));
            return 1f / (1f + tau / dt);
        }
    }

    /// <summary>
    /// The One Euro filter for every vertex of a mesh at once (the face mesh: 468 points). The speed is measured per vertex as a
    /// 3D length, so a vertex that moves fast in any direction is followed closely. All buffers are made once and reused: no
    /// allocation per frame.
    /// </summary>
    public sealed class OneEuroVertexFilter
    {
        public float minCutoff = 1.5f, beta = 150f, derivativeCutoff = 1f;

        Vector3[] values = new Vector3[0];
        Vector3[] speeds = new Vector3[0];
        int count;
        bool started;

        /// <summary>The smoothed vertices (only the first <see cref="Count"/> are in use).</summary>
        public Vector3[] Values => values;
        public int Count => count;

        public void Reset() { started = false; count = 0; }

        /// <summary>Feeds the newest raw vertices (the first <paramref name="n"/> of <paramref name="raw"/>), dt seconds after the last call.</summary>
        public void Step(Vector3[] raw, int n, float dt)
        {
            if (raw == null || n <= 0) return;
            if (values.Length < n) { values = new Vector3[n]; speeds = new Vector3[n]; started = false; }
            if (!started || n != count || dt <= 0f)
            {
                System.Array.Copy(raw, values, n);
                for (int i = 0; i < n; i++) speeds[i] = Vector3.zero;
                count = n; started = true;
                return;
            }
            float speedAlpha = OneEuroFilter.Alpha(derivativeCutoff, dt);
            float inverseDt = 1f / dt;
            for (int i = 0; i < n; i++)
            {
                Vector3 previous = values[i];
                Vector3 rawSpeed = (raw[i] - previous) * inverseDt;
                Vector3 s = speeds[i] + (rawSpeed - speeds[i]) * speedAlpha;
                speeds[i] = s;
                float alpha = OneEuroFilter.Alpha(minCutoff + beta * s.magnitude, dt);
                values[i] = previous + (raw[i] - previous) * alpha;
            }
        }
    }
}
