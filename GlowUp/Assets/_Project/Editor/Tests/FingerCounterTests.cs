using GlowUp.Common;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the finger counting of the finger signs (FingerCounter). The hand points below are REAL: they are what
    /// Google's hand finder (MediaPipe Hand Landmarker, the same model the app uses) found in its own public sample
    /// photos. Each set is 63 numbers: x, y, z of the 21 points, x and z as shares of the picture's width, y of its height.
    /// </summary>
    public class FingerCounterTests
    {
        // mediapipe-assets_fist.jpg (358x354): 0 raised
        static readonly float[] Fist = { 0.4771f, 0.6613f, -0.0000f, 0.6126f, 0.5578f, -0.0414f, 0.7112f, 0.4317f, -0.0645f, 0.6836f, 0.3200f, -0.0875f, 0.5593f, 0.3206f, -0.0988f, 0.6083f, 0.3069f, -0.0148f, 0.6294f, 0.2141f, -0.0601f, 0.6244f, 0.3287f, -0.0833f, 0.6078f, 0.3685f, -0.0966f, 0.5157f, 0.3219f, -0.0217f, 0.5293f, 0.2477f, -0.0626f, 0.5485f, 0.3805f, -0.0703f, 0.5443f, 0.3881f, -0.0746f, 0.4316f, 0.3492f, -0.0375f, 0.4486f, 0.2765f, -0.0817f, 0.4772f, 0.3965f, -0.0650f, 0.4679f, 0.4083f, -0.0490f, 0.3483f, 0.3781f, -0.0574f, 0.3862f, 0.3282f, -0.0728f, 0.4114f, 0.3973f, -0.0476f, 0.3940f, 0.4115f, -0.0296f };
        const float FistAspect = 1.0113f;
        // mediapipe-tasks_gesture_recognizer_pointing_up.jpg (419x419): 1 raised
        static readonly float[] PointingUp = { 0.7973f, 0.3230f, -0.0000f, 0.7600f, 0.2995f, -0.0139f, 0.7357f, 0.2542f, -0.0191f, 0.7380f, 0.2167f, -0.0247f, 0.7492f, 0.1891f, -0.0284f, 0.7369f, 0.2000f, -0.0098f, 0.7187f, 0.1362f, -0.0282f, 0.7104f, 0.1002f, -0.0407f, 0.7085f, 0.0706f, -0.0501f, 0.7677f, 0.2038f, -0.0113f, 0.7687f, 0.1671f, -0.0402f, 0.7708f, 0.2093f, -0.0470f, 0.7723f, 0.2399f, -0.0447f, 0.7965f, 0.2125f, -0.0153f, 0.8010f, 0.1755f, -0.0415f, 0.7940f, 0.2137f, -0.0349f, 0.7901f, 0.2420f, -0.0235f, 0.8224f, 0.2256f, -0.0207f, 0.8214f, 0.2014f, -0.0410f, 0.8100f, 0.2241f, -0.0378f, 0.8043f, 0.2446f, -0.0303f };
        const float PointingUpAspect = 1.0000f;
        // mediapipe-assets_pointing_up_rotated.jpg (376x358): 1 raised
        static readonly float[] PointingUpTurned = { 0.2502f, 0.4787f, 0.0000f, 0.3321f, 0.5399f, -0.0356f, 0.4351f, 0.5771f, -0.0607f, 0.4962f, 0.5283f, -0.0860f, 0.4940f, 0.4435f, -0.1105f, 0.5514f, 0.4742f, -0.0468f, 0.6670f, 0.4808f, -0.0834f, 0.7345f, 0.4848f, -0.1061f, 0.7977f, 0.4756f, -0.1265f, 0.5224f, 0.3962f, -0.0595f, 0.5727f, 0.4173f, -0.1151f, 0.4734f, 0.4577f, -0.1255f, 0.4406f, 0.4542f, -0.1240f, 0.4780f, 0.3373f, -0.0752f, 0.5083f, 0.3590f, -0.1344f, 0.4229f, 0.4076f, -0.1184f, 0.4023f, 0.4006f, -0.0971f, 0.4232f, 0.2832f, -0.0939f, 0.4527f, 0.3089f, -0.1304f, 0.4027f, 0.3526f, -0.1111f, 0.3860f, 0.3486f, -0.0917f };
        const float PointingUpTurnedAspect = 1.0503f;
        // mediapipe-tasks_gesture_recognizer_victory.jpg (418x418): 2 raised
        static readonly float[] Victory = { 0.2901f, 0.7538f, 0.0000f, 0.3465f, 0.7566f, -0.0688f, 0.3702f, 0.7416f, -0.1173f, 0.3326f, 0.7439f, -0.1619f, 0.2897f, 0.7407f, -0.2077f, 0.3553f, 0.6069f, -0.0963f, 0.3903f, 0.5148f, -0.1534f, 0.4062f, 0.4553f, -0.1874f, 0.4157f, 0.4015f, -0.2074f, 0.2940f, 0.5986f, -0.0946f, 0.2658f, 0.4987f, -0.1608f, 0.2432f, 0.4354f, -0.1966f, 0.2236f, 0.3795f, -0.2104f, 0.2461f, 0.6178f, -0.0971f, 0.2120f, 0.6028f, -0.1800f, 0.2276f, 0.6749f, -0.1953f, 0.2443f, 0.7215f, -0.1815f, 0.2140f, 0.6528f, -0.1029f, 0.1905f, 0.6566f, -0.1673f, 0.2050f, 0.7109f, -0.1688f, 0.2225f, 0.7485f, -0.1526f };
        const float VictoryAspect = 1.0000f;
        // mediapipe-assets_right_hands.jpg (720x382): 5 raised
        static readonly float[] OpenHand = { 0.8096f, 0.9245f, 0.0000f, 0.7103f, 0.8815f, -0.0282f, 0.6405f, 0.7583f, -0.0501f, 0.5943f, 0.6213f, -0.0669f, 0.5476f, 0.5351f, -0.0857f, 0.6945f, 0.5715f, -0.0662f, 0.6667f, 0.3697f, -0.0937f, 0.6519f, 0.2420f, -0.1090f, 0.6438f, 0.1348f, -0.1194f, 0.7543f, 0.5453f, -0.0664f, 0.7456f, 0.3177f, -0.0925f, 0.7408f, 0.1694f, -0.1078f, 0.7378f, 0.0542f, -0.1191f, 0.8091f, 0.5576f, -0.0668f, 0.8176f, 0.3491f, -0.0953f, 0.8235f, 0.2078f, -0.1173f, 0.8260f, 0.1022f, -0.1310f, 0.8574f, 0.5888f, -0.0673f, 0.8802f, 0.4258f, -0.0934f, 0.8940f, 0.3239f, -0.1094f, 0.9052f, 0.2370f, -0.1194f };
        const float OpenHandAspect = 1.8848f;
        // mediapipe-assets_thumb_up.jpg (382x406): 1 raised
        static readonly float[] ThumbUp = { 0.6388f, 0.6713f, -0.0000f, 0.6349f, 0.5367f, -0.0697f, 0.5747f, 0.4128f, -0.0938f, 0.4997f, 0.3255f, -0.1080f, 0.4736f, 0.2510f, -0.1059f, 0.4075f, 0.4713f, -0.0469f, 0.3372f, 0.4674f, -0.0997f, 0.4418f, 0.5096f, -0.1112f, 0.4806f, 0.5188f, -0.1102f, 0.3922f, 0.5495f, -0.0289f, 0.3405f, 0.5561f, -0.0821f, 0.4615f, 0.5831f, -0.0839f, 0.4706f, 0.5641f, -0.0789f, 0.3924f, 0.6186f, -0.0220f, 0.3430f, 0.6280f, -0.0813f, 0.4500f, 0.6430f, -0.0621f, 0.4640f, 0.6222f, -0.0390f, 0.3923f, 0.6819f, -0.0202f, 0.3579f, 0.6986f, -0.0523f, 0.4270f, 0.6989f, -0.0376f, 0.4442f, 0.6876f, -0.0204f };
        const float ThumbUpAspect = 0.9409f;
        // mediapipe-tasks_gesture_recognizer_thumbs_down.jpg (427x640): 1 raised
        static readonly float[] ThumbDown = { 0.3776f, 0.4484f, -0.0000f, 0.4566f, 0.5907f, -0.1228f, 0.5610f, 0.7098f, -0.2168f, 0.6087f, 0.8151f, -0.2964f, 0.6379f, 0.9088f, -0.3579f, 0.8905f, 0.6001f, -0.2000f, 0.7954f, 0.6599f, -0.3040f, 0.6657f, 0.6366f, -0.3356f, 0.6672f, 0.6086f, -0.3545f, 0.8987f, 0.5096f, -0.1953f, 0.7528f, 0.5815f, -0.2937f, 0.6130f, 0.5602f, -0.3005f, 0.6547f, 0.5412f, -0.3074f, 0.8555f, 0.4280f, -0.2008f, 0.7270f, 0.4955f, -0.2943f, 0.6095f, 0.4916f, -0.2633f, 0.6465f, 0.4787f, -0.2425f, 0.7973f, 0.3558f, -0.2180f, 0.6930f, 0.4004f, -0.2792f, 0.6116f, 0.4122f, -0.2649f, 0.6312f, 0.4004f, -0.2526f };
        const float ThumbDownAspect = 0.6672f;

        [Test]
        public void AFistIsNoFingers() { Assert.AreEqual(0, FingerCounter.Count(Fist, FistAspect)); }

        [Test]
        public void OneFingerPointingUpIsOne()
        {
            Assert.AreEqual(1, FingerCounter.Count(PointingUp, PointingUpAspect));
            Assert.AreEqual(1, FingerCounter.Count(PointingUpTurned, PointingUpTurnedAspect), "the hand lies on its side in this photo");
        }

        [Test]
        public void TheVictorySignIsTwo() { Assert.AreEqual(2, FingerCounter.Count(Victory, VictoryAspect)); }

        [Test]
        public void AnOpenHandIsFive() { Assert.AreEqual(5, FingerCounter.Count(OpenHand, OpenHandAspect)); }

        [Test]
        public void AThumbAloneIsOne()
        {
            Assert.AreEqual(1, FingerCounter.Count(ThumbUp, ThumbUpAspect));
            Assert.AreEqual(1, FingerCounter.Count(ThumbDown, ThumbDownAspect));
        }

        [Test]
        public void ThreeAndFourFingers()
        {
            // No sample photo shows three or four fingers, so they are made from the real open hand: a finger is "bent" by
            // putting its three outer points where the same finger's points are in the real fist, measured from its knuckle.
            Assert.AreEqual(4, FingerCounter.Count(WithBent(OpenHand, OpenHandAspect, 1), 1f), "thumb tucked in: four");
            Assert.AreEqual(3, FingerCounter.Count(WithBent(OpenHand, OpenHandAspect, 1, 17), 1f), "thumb and little finger tucked in: three");
            Assert.AreEqual(3, FingerCounter.Count(WithBent(OpenHand, OpenHandAspect, 13, 17), 1f), "ring and little finger bent, thumb out: three");
            Assert.AreEqual(2, FingerCounter.Count(WithBent(OpenHand, OpenHandAspect, 1, 13, 17), 1f));
            Assert.AreEqual(1, FingerCounter.Count(WithBent(OpenHand, OpenHandAspect, 1, 9, 13, 17), 1f));
            Assert.AreEqual(0, FingerCounter.Count(WithBent(OpenHand, OpenHandAspect, 1, 5, 9, 13, 17), 1f));
        }

        [Test]
        public void TurningMirroringAndSizeDoNotChangeTheCount()
        {
            foreach (var sample in new[] { (Fist, FistAspect, 0), (PointingUp, PointingUpAspect, 1), (Victory, VictoryAspect, 2), (OpenHand, OpenHandAspect, 5), (ThumbUp, ThumbUpAspect, 1) })
            {
                Vector3[] hand = Points(sample.Item1, sample.Item2);
                foreach (float degrees in new[] { 0f, 37f, 90f, 180f, 270f })
                    foreach (float size in new[] { 0.3f, 1f, 4f })
                        foreach (bool mirrored in new[] { false, true })
                        {
                            var changed = new Vector3[21];
                            Quaternion turn = Quaternion.Euler(0f, 0f, degrees);
                            for (int i = 0; i < 21; i++)
                            {
                                Vector3 p = hand[i];
                                if (mirrored) p.x = -p.x;
                                changed[i] = turn * p * size + new Vector3(5f, -3f, 0f);
                            }
                            Assert.AreEqual(sample.Item3, FingerCounter.Count(changed), $"turned {degrees} degrees, size {size}, mirrored {mirrored}");
                        }
            }
        }

        [Test]
        public void SomethingThatIsNotAHandGivesMinusOne()
        {
            Assert.AreEqual(-1, FingerCounter.Count((float[])null));
            Assert.AreEqual(-1, FingerCounter.Count(new float[10]));
            Assert.AreEqual(-1, FingerCounter.Count(new float[63]), "all 21 points in one place");
        }

        [Test]
        public void TheNumbersTheJavaHalfWritesAreReadBack()
        {
            var text = new System.Text.StringBuilder();
            foreach (float v in Victory) text.Append(v.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            float[] back = HandLandmarkReader.Parse(text.ToString());
            Assert.IsNotNull(back);
            Assert.AreEqual(2, FingerCounter.Count(back, VictoryAspect));
            Assert.IsNull(HandLandmarkReader.Parse(""), "no hand seen");
            Assert.IsNull(HandLandmarkReader.Parse("0.1 0.2 0.3"), "too few numbers");
        }

        // ------------------------------------------------------------------------------------
        static Vector3[] Points(float[] numbers, float aspect)
        {
            var p = new Vector3[21];
            for (int i = 0; i < 21; i++) p[i] = new Vector3(numbers[i * 3] * aspect, numbers[i * 3 + 1], numbers[i * 3 + 2] * aspect);
            return p;
        }

        /// <summary>
        /// The hand with some fingers bent. `fingers` = the first point of each finger to bend (1 thumb, 5 index, 9 middle,
        /// 13 ring, 17 little). The bent shape is taken from the real fist: the finger's outer points keep the place they have
        /// in the fist, measured in the fist's own hand frame (wrist, middle knuckle, palm side) and scaled to this hand.
        /// Returns 63 numbers that are already true distances (aspect 1).
        /// </summary>
        static float[] WithBent(float[] hand, float aspect, params int[] fingers)
        {
            Vector3[] open = Points(hand, aspect), fist = Points(Fist, FistAspect);
            Frame(open, out Vector3 oOrigin, out Vector3 oX, out Vector3 oY, out Vector3 oZ, out float oSize);
            Frame(fist, out Vector3 fOrigin, out Vector3 fX, out Vector3 fY, out Vector3 fZ, out float fSize);
            // the two photos may show a left and a right hand: then the fist's side-to-side direction is the other way round
            float side = Mathf.Sign(Vector3.Dot(open[5] - open[17], oX)) * Mathf.Sign(Vector3.Dot(fist[5] - fist[17], fX));
            foreach (int first in fingers)
                for (int i = first; i < first + 4; i++)
                {
                    Vector3 local = fist[i] - fOrigin;
                    var inFist = new Vector3(Vector3.Dot(local, fX) * side, Vector3.Dot(local, fY), Vector3.Dot(local, fZ) * side) / fSize;
                    open[i] = oOrigin + (oX * inFist.x + oY * inFist.y + oZ * inFist.z) * oSize;
                }
            var numbers = new float[63];
            for (int i = 0; i < 21; i++) { numbers[i * 3] = open[i].x; numbers[i * 3 + 1] = open[i].y; numbers[i * 3 + 2] = open[i].z; }
            return numbers;
        }

        /// <summary>A hand's own directions: from the wrist to the middle knuckle (y), across the knuckles (x), out of the palm (z).</summary>
        static void Frame(Vector3[] p, out Vector3 origin, out Vector3 x, out Vector3 y, out Vector3 z, out float size)
        {
            origin = p[0];
            y = (p[9] - p[0]).normalized;
            Vector3 across = p[5] - p[17];
            x = (across - y * Vector3.Dot(across, y)).normalized;
            z = Vector3.Cross(x, y).normalized;
            size = Vector3.Distance(p[0], p[9]);
        }
    }
}
