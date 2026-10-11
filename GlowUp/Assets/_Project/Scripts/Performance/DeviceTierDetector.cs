using UnityEngine;

namespace GlowUp.Performance
{
    /// <summary>
    /// Reads the phone's specifications (memory, processor, graphics memory) and says whether it
    /// should run in Performance Mode or High Quality. The limits come from the PerformanceProfile asset.
    /// </summary>
    public static class DeviceTierDetector
    {
        /// <summary>What was read from the phone and what was concluded from it.</summary>
        public struct Report
        {
            public GraphicsMode suggested;
            public int memoryMb, processorCores, processorMhz, graphicsMemoryMb;
            /// <summary>How many of the "low-end" checks were true.</summary>
            public int lowChecks;
            /// <summary>False when the phone reported nothing usable (the profile's fallback mode was taken).</summary>
            public bool known;

            /// <summary>For the Settings page, e.g. "4 GB memory, 8-core processor".</summary>
            public string Describe()
            {
                if (!known) return "specifications not reported";
                string memory = memoryMb >= 1024 ? (memoryMb / 1024f).ToString("0.#") + " GB memory" : memoryMb + " MB memory";
                return processorCores > 0 ? memory + ", " + processorCores + "-core processor" : memory;
            }
        }

        public static Report Inspect(PerformanceProfile profile)
        {
            var r = new Report
            {
                memoryMb = SystemInfo.systemMemorySize,
                processorCores = SystemInfo.processorCount,
                processorMhz = SystemInfo.processorFrequency,
                graphicsMemoryMb = SystemInfo.graphicsMemorySize
            };
            r.known = r.memoryMb > 0;
            if (profile == null || !r.known)
            {
                r.suggested = profile != null ? profile.modeWhenNotDetected : GraphicsMode.HighQuality;
                return r;
            }

            if (r.memoryMb <= profile.lowMemoryMb) r.lowChecks++;
            if (r.processorCores > 0 && r.processorCores <= profile.lowProcessorCores) r.lowChecks++;
            if (r.processorMhz > 0 && r.processorMhz <= profile.lowProcessorMhz) r.lowChecks++;
            if (r.graphicsMemoryMb > 0 && r.graphicsMemoryMb <= profile.lowGraphicsMemoryMb) r.lowChecks++;

            r.suggested = r.lowChecks >= Mathf.Max(1, profile.checksNeededForLowEnd) ? GraphicsMode.Performance : GraphicsMode.HighQuality;
            return r;
        }
    }
}
