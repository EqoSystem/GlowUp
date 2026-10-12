using GlowUp.Common;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks how the short piece of the delete sound is cut out of the recording (DeleteSoundPlayer.Cut): the silence
    /// at the start is skipped, the piece has the asked length, it fades out, and a quiet recording is brought up to a
    /// normal loudness, but never by more than the allowed factor. The "recording" here is made by the test: 0.4
    /// seconds of silence and then a steady tone, like the real file.
    /// </summary>
    public class DeleteSoundTests
    {
        const int Frequency = 1000, Channels = 2;

        /// <summary>`silent` seconds of nothing, then `sounding` seconds of a square tone of the given loudness.</summary>
        static float[] Recording(float silent, float sounding, float loudness)
        {
            int quiet = Mathf.RoundToInt(silent * Frequency), loud = Mathf.RoundToInt(sounding * Frequency);
            var samples = new float[(quiet + loud) * Channels];
            for (int f = 0; f < loud; f++)
                for (int c = 0; c < Channels; c++) samples[(quiet + f) * Channels + c] = (f % 2 == 0 ? 1f : -1f) * loudness;
            return samples;
        }

        static float Peak(float[] samples, int fromFrame, int toFrame)
        {
            float most = 0f;
            for (int i = fromFrame * Channels; i < toFrame * Channels && i < samples.Length; i++) most = Mathf.Max(most, Mathf.Abs(samples[i]));
            return most;
        }

        [Test]
        public void TheSilenceAtTheStartIsSkippedAndThePieceHasItsLength()
        {
            float[] cut = DeleteSoundPlayer.Cut(Recording(0.4f, 3f, 0.2f), Channels, Frequency, true, 0f, 1f, 0.3f, 0.85f, 6f);
            Assert.AreEqual(1000 * Channels, cut.Length, "one second, two channels");
            Assert.Greater(Peak(cut, 15, 40), 0.5f, "the sound is there within a few hundredths of a second");
            // without skipping, the piece starts with the file's silence
            float[] plain = DeleteSoundPlayer.Cut(Recording(0.4f, 3f, 0.2f), Channels, Frequency, false, 0f, 1f, 0.3f, 0.85f, 6f);
            Assert.AreEqual(0f, Peak(plain, 0, 390), 0.0001f);
            Assert.Greater(Peak(plain, 410, 600), 0.5f);
        }

        [Test]
        public void AQuietRecordingIsBroughtUpButNotWithoutLimit()
        {
            float[] cut = DeleteSoundPlayer.Cut(Recording(0.4f, 3f, 0.2f), Channels, Frequency, true, 0f, 1f, 0.3f, 0.85f, 6f);
            Assert.AreEqual(0.85f, Peak(cut, 0, 1000), 0.01f, "0.2 times 4.25");
            float[] veryQuiet = DeleteSoundPlayer.Cut(Recording(0.4f, 3f, 0.01f), Channels, Frequency, true, 0f, 1f, 0.3f, 0.85f, 6f);
            Assert.AreEqual(0.06f, Peak(veryQuiet, 0, 1000), 0.005f, "at most six times louder");
            float[] asItIs = DeleteSoundPlayer.Cut(Recording(0.4f, 3f, 0.2f), Channels, Frequency, true, 0f, 1f, 0.3f, 0f, 6f);
            Assert.AreEqual(0.2f, Peak(asItIs, 0, 1000), 0.001f, "0 = leave the recording as it is");
            float[] loudAlready = DeleteSoundPlayer.Cut(Recording(0.4f, 3f, 1f), Channels, Frequency, true, 0f, 1f, 0.3f, 0.85f, 6f);
            Assert.LessOrEqual(Peak(loudAlready, 0, 1000), 0.851f, "a loud recording is brought down to the same point");
        }

        [Test]
        public void ThePieceFadesOutAndDoesNotStartWithAClick()
        {
            float[] cut = DeleteSoundPlayer.Cut(Recording(0f, 3f, 0.5f), Channels, Frequency, true, 0f, 1f, 0.4f, 0.85f, 6f);
            Assert.AreEqual(0f, Mathf.Abs(cut[0]), 0.0001f, "it starts from nothing");
            Assert.Greater(Peak(cut, 100, 500), 0.8f, "full loudness in the middle");
            Assert.Less(Peak(cut, 900, 1000), 0.3f, "nearly gone in the last tenth");
            Assert.Less(Peak(cut, 990, 1000), 0.03f);
            Assert.Greater(Peak(cut, 600, 700), Peak(cut, 800, 900), "it gets quieter and quieter");
        }

        [Test]
        public void OddRecordingsDoNotBreakIt()
        {
            Assert.AreEqual(0, DeleteSoundPlayer.Cut(Recording(1f, 0f, 0f), Channels, Frequency, true, 0f, 1f, 0.3f, 0.85f, 6f).Length, "a silent file gives no piece");
            Assert.AreEqual(0, DeleteSoundPlayer.Cut(null, Channels, Frequency, true, 0f, 1f, 0.3f, 0.85f, 6f).Length);
            Assert.AreEqual(0, DeleteSoundPlayer.Cut(new float[0], Channels, Frequency, true, 0f, 1f, 0.3f, 0.85f, 6f).Length);
            // asked for more than there is: the piece is what is left
            float[] shortOne = DeleteSoundPlayer.Cut(Recording(0.4f, 0.5f, 0.2f), Channels, Frequency, true, 0f, 5f, 0.3f, 0.85f, 6f);
            Assert.AreEqual((500 + 10) * Channels, shortOne.Length, "half a second of sound and the hundredth of a second before it");
            // a later start inside the sound
            float[] later = DeleteSoundPlayer.Cut(Recording(0.4f, 3f, 0.2f), Channels, Frequency, true, 1f, 0.5f, 0.1f, 0.85f, 6f);
            Assert.AreEqual(500 * Channels, later.Length);
            Assert.Greater(Peak(later, 10, 300), 0.8f);
        }
    }
}
