using GlowUp.Common;
using GlowUp.Views;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the dragging of a shaking tile in Recent Scans (HoldToRemoveTile): the tile goes wherever the finger goes
    /// (to the right and up and down too, on a screen of any scale), the point that was touched stays under the finger,
    /// "remove" is decided by the finger being inside the remove area, and a tile that is not removed starts its way
    /// back from where it was let go. The finger is played by the test.
    /// </summary>
    public class DragToRemoveTests
    {
        GameObject root;
        RectTransform tile, visual, area;
        HoldToRemoveTile hold;
        float lastInside = -1f;
        bool? ended;

        /// <summary>A tile of 400 x 400 at (300, 500) on a "screen" whose canvas is `scale` times enlarged, and a remove area over the left half.</summary>
        void Build(float scale)
        {
            root = new GameObject("Canvas", typeof(RectTransform));
            root.transform.localScale = Vector3.one * scale;
            tile = New("Tile", root.transform, new Vector2(300, 500) / scale, new Vector2(400, 400));
            visual = New("Visual", tile, Vector2.zero, new Vector2(400, 400));
            area = New("RemoveZone", root.transform, new Vector2(270, 960) / scale, new Vector2(540, 1920) / scale);
            hold = tile.gameObject.AddComponent<HoldToRemoveTile>();
            var so = new SerializedObject(hold);
            so.FindProperty("visual").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            hold.SetRemoveArea(area);
            hold.SetShaking(true);
            lastInside = -1f; ended = null;
            hold.Dragging += (_, inside) => lastInside = inside;
            hold.DragEnded += (_, remove) => ended = remove;
        }

        static RectTransform New(string name, Transform parent, Vector2 at, Vector2 size)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.localPosition = at;
            return rt;
        }

        [TearDown]
        public void TearDown()
        {
            SwipeSceneNavigator.Suppressed = false;
            if (root != null) Object.DestroyImmediate(root);
        }

        PointerEventData Finger(Vector2 pressedAt, Vector2 now) => new PointerEventData(null) { pressPosition = pressedAt, position = now };

        /// <summary>Where the touched point of the tile is on the screen now.</summary>
        Vector2 TouchedPointOnScreen(Vector2 touchedInVisual) => visual.TransformPoint(touchedInVisual);

        [Test]
        public void TheTileGoesWhereverTheFingerGoes()
        {
            Build(1f);
            var press = new Vector2(350, 520);
            Vector2 touched = visual.InverseTransformPoint(press);          // the spot of the tile under the finger
            hold.OnInitializePotentialDrag(Finger(press, press));
            hold.OnBeginDrag(Finger(press, new Vector2(362, 528)));
            Assert.That(Vector2.Distance(TouchedPointOnScreen(touched), new Vector2(362, 528)), Is.LessThan(0.01f), "it follows from the first movement");

            // far to the RIGHT and far UP: neither is held back any more
            hold.OnDrag(Finger(press, new Vector2(1000, 1700)));
            Assert.That(Vector2.Distance(TouchedPointOnScreen(touched), new Vector2(1000, 1700)), Is.LessThan(0.01f));
            Assert.That(visual.localPosition.x, Is.EqualTo(650f).Within(0.01f), "650 to the right of its place in the grid");
            Assert.That(visual.localPosition.y, Is.EqualTo(1180f).Within(0.01f), "the whole way up, not a part of it");

            // every corner of the screen
            foreach (Vector2 corner in new[] { new Vector2(0, 0), new Vector2(1080, 0), new Vector2(0, 1920), new Vector2(1080, 1920) })
            {
                hold.OnDrag(Finger(press, corner));
                Assert.That(Vector2.Distance(TouchedPointOnScreen(touched), corner), Is.LessThan(0.01f), corner.ToString());
            }
        }

        [Test]
        public void TheTouchedPointStaysUnderTheFingerOnAScaledScreen()
        {
            Build(2.5f);                    // a phone whose screen is 2.5 times the canvas
            var press = new Vector2(420, 610);
            Vector2 touched = visual.InverseTransformPoint(press);
            hold.OnInitializePotentialDrag(Finger(press, press));
            hold.OnBeginDrag(Finger(press, press + new Vector2(8, -6)));
            foreach (Vector2 finger in new[] { new Vector2(900, 300), new Vector2(40, 1800), new Vector2(555, 955) })
            {
                hold.OnDrag(Finger(press, finger));
                Assert.That(Vector2.Distance(TouchedPointOnScreen(touched), finger), Is.LessThan(0.02f), finger.ToString());
            }
        }

        [Test]
        public void RemovingIsDecidedByTheFingerBeingInTheArea()
        {
            Build(1f);
            var press = new Vector2(350, 520);          // the tile itself lies inside the left half
            hold.OnInitializePotentialDrag(Finger(press, press));
            hold.OnBeginDrag(Finger(press, new Vector2(700, 520)));
            Assert.AreEqual(0f, lastInside, "the finger is in the right half");
            hold.OnDrag(Finger(press, new Vector2(530, 900)));
            Assert.AreEqual(1f, lastInside, "the finger entered the area");
            hold.OnDrag(Finger(press, new Vector2(560, 900)));
            Assert.AreEqual(0f, lastInside, "and left it again: back to not removing");
            hold.OnDrag(Finger(press, new Vector2(100, 1500)));
            hold.OnEndDrag(Finger(press, new Vector2(100, 1500)));
            Assert.AreEqual(true, ended, "let go inside the area");
        }

        [Test]
        public void LetGoOutsideTheAreaItStaysWhereItWasLetGoAndIsNotRemoved()
        {
            Build(1f);
            var press = new Vector2(350, 520);
            hold.OnInitializePotentialDrag(Finger(press, press));
            hold.OnBeginDrag(Finger(press, new Vector2(700, 600)));
            hold.OnDrag(Finger(press, new Vector2(950, 1400)));
            hold.OnEndDrag(Finger(press, new Vector2(950, 1400)));
            Assert.AreEqual(false, ended);
            // the way back starts from the exact spot where it was let go (the gliding itself runs frame by frame in the app)
            Assert.That(visual.localPosition.x, Is.EqualTo(600f).Within(0.01f));
            Assert.That(visual.localPosition.y, Is.EqualTo(880f).Within(0.01f));
            Assert.IsFalse(SwipeSceneNavigator.Suppressed, "swiping between screens works again");
        }

        [Test]
        public void ATileThatDoesNotShakeIsNotDragged()
        {
            Build(1f);
            hold.SetShaking(false);
            var press = new Vector2(350, 520);
            hold.OnInitializePotentialDrag(Finger(press, press));
            hold.OnBeginDrag(Finger(press, new Vector2(350, 700)));          // an ordinary scroll of the list
            hold.OnDrag(Finger(press, new Vector2(350, 900)));
            hold.OnEndDrag(Finger(press, new Vector2(350, 900)));
            Assert.AreEqual(Vector3.zero, visual.localPosition, "the tile stayed in its place");
            Assert.IsNull(ended);
        }
    }
}
