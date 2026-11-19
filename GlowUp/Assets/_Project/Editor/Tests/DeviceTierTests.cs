using GlowUp.Performance;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.EditorTools.Tests
{
    /// <summary>19 November 2026: a 4 GB phone lagged from the log-in screen. Memory at or below 4096 MB is enough for Performance Mode.</summary>
    public class DeviceTierTests
    {
        static GraphicsMode Judge(int memoryMb, int cores, int mhz, int gpuMb)
        {
            var profile = ScriptableObject.CreateInstance<PerformanceProfile>();
            var r = new DeviceTierDetector.Report { memoryMb = memoryMb, processorCores = cores, processorMhz = mhz, graphicsMemoryMb = gpuMb, known = true };
            DeviceTierDetector.Judge(ref r, profile);
            Object.DestroyImmediate(profile);
            return r.suggested;
        }

        [Test] public void FourGbPhoneWithEightCoresIsLowEnd() => Assert.AreEqual(GraphicsMode.Performance, Judge(3800, 8, 2200, 0));
        [Test] public void FourGbPhoneReportingExactly4096IsLowEnd() => Assert.AreEqual(GraphicsMode.Performance, Judge(4096, 8, 2400, 2048));
        [Test] public void SixGbPhoneStaysHighQuality() => Assert.AreEqual(GraphicsMode.HighQuality, Judge(5500, 8, 2400, 2048));
        [Test] public void BigPhoneWithOneWeakCheckStaysHighQuality() => Assert.AreEqual(GraphicsMode.HighQuality, Judge(7800, 4, 2800, 4096));
        [Test] public void BigPhoneWithTwoWeakChecksIsLowEnd() => Assert.AreEqual(GraphicsMode.Performance, Judge(7800, 4, 1800, 4096));
    }
}
