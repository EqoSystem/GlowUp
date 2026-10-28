using System.Collections.Generic;
using GlowUp.AR;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the pure logic of the guided try-on that follows the face (GuidedFaceShapes, GuidedFacePace, FaceMeshData): the face mesh's data,
    /// where each mask paints and where it must not (inside the mouth, on the eyes), the mapping onto the camera view, and the pacing.
    /// </summary>
    public class GuidedFaceTests
    {
        const int N = 256;
        static float[] Uvs => FaceMeshData.Uvs;

        static float At(float[] mask, Vector2 uv) => mask[Mathf.Clamp((int)(uv.y * N), 0, N - 1) * N + Mathf.Clamp((int)(uv.x * N), 0, N - 1)];
        static Vector2 Mid(params int[] points)
        {
            Vector2 sum = Vector2.zero;
            foreach (int p in points) sum += GuidedFaceShapes.Uv(Uvs, p);
            return sum / points.Length;
        }

        [Test]
        public void TheFaceMeshDataIsWhole()
        {
            Assert.AreEqual(468, FaceMeshData.PointCount);
            Assert.AreEqual(468 * 2, FaceMeshData.Uvs.Length);
            Assert.AreEqual(FaceMeshData.TriangleCount * 3, FaceMeshData.Triangles.Length);
            foreach (int i in FaceMeshData.Triangles) Assert.That(i, Is.InRange(0, 467));
            foreach (float v in FaceMeshData.Uvs) Assert.That(v, Is.InRange(0f, 1f));
            // upright: the chin is low, the forehead high, the mouth corners level
            Assert.Less(GuidedFaceShapes.Uv(Uvs, 152).y, GuidedFaceShapes.Uv(Uvs, 1).y);
            Assert.Less(GuidedFaceShapes.Uv(Uvs, 1).y, GuidedFaceShapes.Uv(Uvs, 10).y);
            Assert.AreEqual(GuidedFaceShapes.Uv(Uvs, 61).y, GuidedFaceShapes.Uv(Uvs, 291).y, 0.01f);
        }

        [Test]
        public void EveryRingUsesRealPoints()
        {
            var rings = new List<int[]> { GuidedFaceShapes.LipsOuter, GuidedFaceShapes.LipsInner, GuidedFaceShapes.FaceOval, GuidedFaceShapes.EyeOne, GuidedFaceShapes.EyeTwo,
                GuidedFaceShapes.UpperLidOne, GuidedFaceShapes.UpperLidTwo, GuidedFaceShapes.BrowLowerOne, GuidedFaceShapes.BrowLowerTwo, GuidedFaceShapes.BrowOne, GuidedFaceShapes.BrowTwo,
                GuidedFaceShapes.SkinPoints };
            foreach (int[] ring in rings) foreach (int p in ring) Assert.That(p, Is.InRange(0, 467));
        }

        [Test]
        public void TheLipsArePaintedButNotTheInsideOfTheMouth()
        {
            float[] lips = GuidedFaceShapes.ColourMask(GuidedFaceShapes.Lips, N, Uvs);
            Assert.Greater(At(lips, Vector2.Lerp(GuidedFaceShapes.Uv(Uvs, 14), GuidedFaceShapes.Uv(Uvs, 17), 0.5f)), 0.5f, "the middle of the lower lip");
            Assert.Greater(At(lips, Vector2.Lerp(GuidedFaceShapes.Uv(Uvs, 13), GuidedFaceShapes.Uv(Uvs, 0), 0.5f)), 0.5f, "the middle of the upper lip");
            Assert.Less(At(lips, GuidedFaceShapes.Uv(Uvs, 1)), 0.01f, "not the nose");
            Assert.Less(At(lips, GuidedFaceShapes.Uv(Uvs, 152)), 0.01f, "not the chin");
        }

        [Test]
        public void TheTrianglesInsideTheMouthAndTheEyesAreLeftOut()
        {
            int[] kept = GuidedFaceShapes.TrianglesWithoutHoles(FaceMeshData.Triangles);
            Assert.Less(kept.Length, FaceMeshData.Triangles.Length, "some triangles go");
            Assert.Greater(kept.Length, FaceMeshData.Triangles.Length * 9 / 10, "most of the face stays");
            var mouth = new HashSet<int>(GuidedFaceShapes.LipsInner);
            for (int t = 0; t < kept.Length; t += 3)
                Assert.IsFalse(mouth.Contains(kept[t]) && mouth.Contains(kept[t + 1]) && mouth.Contains(kept[t + 2]), "a triangle across the mouth was kept");
        }

        [Test]
        public void EyeColourIsOnTheLidNotOnTheEye()
        {
            float[] eyes = GuidedFaceShapes.ColourMask(GuidedFaceShapes.Eyes, N, Uvs);
            Assert.Less(At(eyes, Mid(159, 145)), 0.01f, "not on the eye itself");
            Assert.Greater(At(eyes, Vector2.Lerp(GuidedFaceShapes.Uv(Uvs, 159), GuidedFaceShapes.Uv(Uvs, 52), 0.25f)), 0.3f, "on the lid above it");
        }

        [Test]
        public void FoundationCoversTheSkinButNotEyesLipsOrBackground()
        {
            float[] face = GuidedFaceShapes.ColourMask(GuidedFaceShapes.Face, N, Uvs);
            Assert.Greater(At(face, GuidedFaceShapes.Uv(Uvs, 205)), 0.8f, "the cheek");
            Assert.Greater(At(face, GuidedFaceShapes.Uv(Uvs, 151)), 0.8f, "the forehead");
            Assert.Less(At(face, Mid(159, 145)), 0.05f, "not the eye");
            Assert.Less(At(face, Vector2.Lerp(GuidedFaceShapes.Uv(Uvs, 14), GuidedFaceShapes.Uv(Uvs, 17), 0.5f)), 0.3f, "not the lips");
            Assert.Less(At(face, new Vector2(0.02f, 0.98f)), 0.01f, "not outside the face");
        }

        [Test]
        public void CheeksAndBrowsLandOnTheirParts()
        {
            Assert.Greater(At(GuidedFaceShapes.ColourMask(GuidedFaceShapes.Cheeks, N, Uvs), Vector2.Lerp(GuidedFaceShapes.Uv(Uvs, 205), GuidedFaceShapes.Uv(Uvs, 50), 0.35f)), 0.9f);
            Assert.Less(At(GuidedFaceShapes.ColourMask(GuidedFaceShapes.Cheeks, N, Uvs), GuidedFaceShapes.Uv(Uvs, 1)), 0.05f, "not the nose");
            Assert.Greater(At(GuidedFaceShapes.ColourMask(GuidedFaceShapes.Brows, N, Uvs), Mid(105, 52)), 0.5f, "the brow");
        }

        [Test]
        public void APointLandsOnTheViewAndTurnsRoundInAMirror()
        {
            var view = new Rect(-540, -720, 1080, 1440);
            Assert.AreEqual(new Vector2(-540, 720), GuidedFaceShapes.ToView(0, 0, view, false));
            Assert.AreEqual(new Vector2(540, 720), GuidedFaceShapes.ToView(0, 0, view, true));
            Assert.AreEqual(new Vector2(0, 0), GuidedFaceShapes.ToView(0.5f, 0.5f, view, true));
            Assert.AreEqual(new Vector2(540, -720), GuidedFaceShapes.ToView(1, 1, view, false));
            Assert.IsFalse(GuidedFaceShapes.IsFace(new float[0]));
            Assert.IsTrue(GuidedFaceShapes.IsFace(new float[468 * 3]));
        }

        [Test]
        public void ThePaceFollowsThePhone()
        {
            var pace = new GuidedFacePace();
            pace.Begin(false);
            Assert.AreEqual(384, pace.MaxSide, "a strong phone starts on the largest picture");
            Assert.AreEqual(0f, pace.MinInterval, "and looks at every camera picture");
            for (int i = 0; i < 100; i++) pace.Report(80f);
            Assert.AreEqual(192, pace.MaxSide, "a slow finder ends on the smallest picture");
            Assert.Greater(pace.MinInterval, 0f, "and fewer looks a second");
            for (int i = 0; i < 100; i++) pace.Report(8f);
            Assert.AreEqual(384, pace.MaxSide, "a quick one climbs back");
            pace.Begin(true);
            Assert.AreEqual(256, pace.MaxSide, "the light pipeline starts smaller");
            for (int i = 0; i < 100; i++) pace.Report(5f);
            Assert.AreEqual(320, pace.MaxSide, "and never goes above its cap");
            Assert.IsFalse(pace.Report(0f), "a time of 0 is ignored");
        }
    }
}
