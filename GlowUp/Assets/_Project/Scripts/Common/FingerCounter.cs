using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Counts how many fingers of a hand are raised, from the 21 points the hand finder gives (MediaPipe's order):
    ///
    ///    0 wrist
    ///    1-4   thumb   (base, knuckle, middle joint, tip)
    ///    5-8   index   (knuckle, first joint, second joint, tip)
    ///    9-12  middle     13-16 ring     17-20 little finger
    ///
    /// A finger counts as raised when its tip is clearly farther from the wrist than its first joint is (a bent finger
    /// brings the tip back towards the palm) and the finger is fairly straight. The thumb counts when its tip points
    /// away from the palm. The rules use distances between the points only, so it does not matter how the hand is turned
    /// or which hand it is.
    ///
    /// Pure logic: no camera, no Unity scene. The numbers below can be changed here.
    /// </summary>
    public static class FingerCounter
    {
        /// <summary>A finger's tip must be this many times farther from the wrist than its first joint (1 = just as far).</summary>
        public static float TipBeyondJoint = 1.12f;
        /// <summary>How straight a raised finger must be: 1 = perfectly straight, 0 = bent at a right angle.</summary>
        public static float Straightness = 0.35f;
        /// <summary>The thumb's tip must be this many times farther from the little finger's knuckle than the thumb's own knuckle is.</summary>
        public static float ThumbAway = 1.18f;

        /// <summary>
        /// The number of raised fingers (0 to 5). -1 when the points cannot be a hand (too few, or all in one place).
        /// `points` = 63 numbers: x, y, z of the 21 points as the hand finder gives them: x and z as shares of the picture's
        /// WIDTH, y as a share of its HEIGHT. `aspect` = the picture's width divided by its height, so distances come out true.
        /// (The finder's second set of points, in metres, is NOT used: on real photos it miscounted where these did not.)
        /// </summary>
        public static int Count(float[] points, float aspect = 1f)
        {
            if (points == null || points.Length < 63) return -1;
            var p = new Vector3[21];
            for (int i = 0; i < 21; i++) p[i] = new Vector3(points[i * 3] * aspect, points[i * 3 + 1], points[i * 3 + 2] * aspect);
            return Count(p);
        }

        public static int Count(Vector3[] p)
        {
            if (p == null || p.Length < 21) return -1;
            float palm = Vector3.Distance(p[0], p[9]);             // wrist to the middle finger's knuckle: the size of the hand
            if (palm < 1e-5f) return -1;

            int raised = 0;
            if (FingerRaised(p, 5, palm)) raised++;
            if (FingerRaised(p, 9, palm)) raised++;
            if (FingerRaised(p, 13, palm)) raised++;
            if (FingerRaised(p, 17, palm)) raised++;
            if (ThumbRaised(p, palm)) raised++;
            return raised;
        }

        /// <summary>One of the four long fingers. `knuckle` = the number of its first point (5, 9, 13 or 17).</summary>
        static bool FingerRaised(Vector3[] p, int knuckle, float palm)
        {
            Vector3 wrist = p[0], mcp = p[knuckle], pip = p[knuckle + 1], tip = p[knuckle + 3];
            if (Vector3.Distance(tip, wrist) < Vector3.Distance(pip, wrist) * TipBeyondJoint) return false;      // the tip is back near the palm
            if (Vector3.Distance(tip, mcp) < palm * 0.45f) return false;                                         // a short stub: curled
            Vector3 lower = (pip - mcp).normalized, upper = (tip - pip).normalized;
            return Vector3.Dot(lower, upper) >= Straightness;                                                    // not hooked
        }

        static bool ThumbRaised(Vector3[] p, float palm)
        {
            Vector3 knuckle = p[2], joint = p[3], tip = p[4], littleKnuckle = p[17], indexKnuckle = p[5];
            // tucked into the palm, the tip comes closer to the little finger's knuckle than the thumb's own knuckle is
            if (Vector3.Distance(tip, littleKnuckle) < Vector3.Distance(knuckle, littleKnuckle) * ThumbAway) return false;
            // lying flat along the side of the index finger is not "raised"
            if (Vector3.Distance(tip, indexKnuckle) < palm * 0.42f) return false;
            Vector3 lower = (joint - knuckle).normalized, upper = (tip - joint).normalized;
            return Vector3.Dot(lower, upper) >= Straightness;
        }
    }
}
