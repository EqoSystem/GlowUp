using System;

namespace GlowUp.Performance
{
    /// <summary>
    /// AR TIER POLICY: from measured frame times, decides whether AR Try-On should stay in its pipeline, move down to Light,
    /// or try Full again. Pure logic (no Unity objects): ArAdaptiveQuality feeds it, Editor/Tests/ArUpgradeTests.cs tests it.
    ///
    ///   Down: a window with an average below <see cref="SlowBelowFps"/> or a 95th-percentile frame time above
    ///         <see cref="SlowP95Ms"/> moves Full to Light.
    ///   Up:   only after an AUTOMATIC move down, and at most <see cref="UpgradeTrialsPerVisit"/> times per visit of AR Try-On:
    ///         when Light keeps up with its frame cap and its 95th percentile stays below <see cref="SteadyP95Ms"/> for
    ///         <see cref="SteadySecondsBeforeTrial"/> seconds, Full is tried again. A trial that fails its first window goes
    ///         straight back to Light; one that passes <see cref="WindowsToPassTrial"/> windows is kept.
    ///   A tier the user picked on the Settings page (High or Light) is never changed.
    /// </summary>
    public sealed class ArTierPolicy
    {
        public enum Verdict { Stay, MoveToLight, TryFull, TrialPassed }

        /// <summary>The numbers of one measuring window.</summary>
        public struct Window
        {
            public float averageFps;
            public float p95Ms;
            public float seconds;
        }

        public float SlowBelowFps = 20f;
        public float SlowP95Ms = 80f;
        public float SteadyP95Ms = 45f;
        public float SteadySecondsBeforeTrial = 20f;
        public int WindowsToPassTrial = 3;
        public int UpgradeTrialsPerVisit = 1;

        public ArTier Tier { get; private set; }
        public bool InTrial { get; private set; }
        public int TrialsUsed { get; private set; }
        bool movedDownAutomatically;
        float steadyFor;
        int trialWindowsPassed;

        /// <summary>A new visit of AR Try-On. `startTier` is what the settings give; `foundSlowBefore` = an earlier visit moved this phone down.</summary>
        public void Begin(ArTier startTier, bool foundSlowBefore)
        {
            Tier = startTier;
            movedDownAutomatically = foundSlowBefore && startTier == ArTier.Light;
            InTrial = false; TrialsUsed = 0; steadyFor = 0f; trialWindowsPassed = 0;
        }

        /// <summary>True when the window counts as too slow for the pipeline it ran in.</summary>
        public bool IsSlow(Window w) => w.averageFps < SlowBelowFps || w.p95Ms > SlowP95Ms;

        /// <summary>
        /// One finished window. `automatic` = the Settings choice is Automatic; `capFps` = the frame cap of the Light pipeline
        /// (0 = none). Returns what to do; <see cref="Tier"/> already holds the new tier.
        /// </summary>
        public Verdict Observe(Window w, bool automatic, int capFps)
        {
            if (!automatic) { InTrial = false; return Verdict.Stay; }

            if (Tier == ArTier.Full)
            {
                if (IsSlow(w))
                {
                    Tier = ArTier.Light; movedDownAutomatically = true; InTrial = false; steadyFor = 0f; trialWindowsPassed = 0;
                    return Verdict.MoveToLight;
                }
                if (InTrial && ++trialWindowsPassed >= Math.Max(1, WindowsToPassTrial)) { InTrial = false; return Verdict.TrialPassed; }
                return Verdict.Stay;
            }

            // Light
            if (!movedDownAutomatically || TrialsUsed >= UpgradeTrialsPerVisit) return Verdict.Stay;
            bool keepsUp = capFps <= 0 ? w.averageFps >= SlowBelowFps * 1.5f : w.averageFps >= capFps * 0.95f;
            if (keepsUp && w.p95Ms < SteadyP95Ms) steadyFor += Math.Max(0f, w.seconds);
            else steadyFor = 0f;
            if (steadyFor < SteadySecondsBeforeTrial) return Verdict.Stay;
            Tier = ArTier.Full; InTrial = true; TrialsUsed++; steadyFor = 0f; trialWindowsPassed = 0;
            return Verdict.TryFull;
        }

        /// <summary>
        /// The 95th-percentile frame time (ms) of the first `count` frame times (seconds) in `times`. `scratch` must be at least
        /// as long; it is sorted in place, so no memory is allocated.
        /// </summary>
        public static float P95Ms(float[] times, int count, float[] scratch)
        {
            if (times == null || scratch == null || count <= 0) return 0f;
            count = Math.Min(count, Math.Min(times.Length, scratch.Length));
            Array.Copy(times, scratch, count);
            Array.Sort(scratch, 0, count);
            int index = Math.Min(count - 1, (int)Math.Ceiling(0.95 * count) - 1);
            return scratch[Math.Max(0, index)] * 1000f;
        }
    }
}
