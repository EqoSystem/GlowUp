using System.Collections.Generic;

namespace GlowUp.Scan
{
    /// <summary>
    /// THE RULE OF THE AUTOMATIC FOCUS LOCK of hold-to-scan.
    ///
    /// A phone camera left alone looks for focus all the time. On small print held close it often "hunts": the text
    /// goes sharp, soft, sharp, soft, and every soft picture is one the text reader cannot use. This rule watches the
    /// pictures of the scan (several a second) and says when the camera should instead focus ONCE and HOLD:
    ///
    ///  - FREE (the camera focuses by itself): when the focus was lost several times while the phone was held still,
    ///    or the picture stays soft for a row of looks, it says "focus now and hold".
    ///  - HOLDING: nothing happens while the picture is sharp. Only when it has been soft for a row of looks (the
    ///    product was moved nearer, further, or turned) it says "focus again and hold". After the phone or the product
    ///    was plainly moved, fewer soft looks are enough.
    ///  - After every "focus now" the lens needs a moment: the looks of that moment are not judged, so the rule never
    ///    makes the camera hunt by its own orders.
    ///
    /// Pure logic: no camera, no Unity scene. ScanFocusAssistant feeds it and carries out what it says.
    /// </summary>
    public class FocusKeeper
    {
        /// <summary>May it lock by itself while the camera is free? (Holding a lock that someone else made always works.)</summary>
        public bool Automatic = true;
        /// <summary>FREE: this many times "sharp, then soft" among the remembered looks, with the phone held still, count as hunting.</summary>
        public int LossesToLock = 2;
        /// <summary>FREE: this many soft looks in a row mean the camera does not find the focus by itself.</summary>
        public int SoftLooksToLock = 5;
        /// <summary>How many looks are remembered for counting the losses.</summary>
        public int LooksRemembered = 12;
        /// <summary>HOLDING: this many soft looks in a row = the distance changed: focus again.</summary>
        public int SoftLooksToRefocus = 4;
        /// <summary>HOLDING, after the phone or the product was moved: this many soft looks are enough.</summary>
        public int SoftLooksAfterAMove = 2;
        /// <summary>This many "moving" looks in a row count as a move.</summary>
        public int MovingLooksForAMove = 2;
        /// <summary>Seconds the lens gets after an order to focus. Looks in this time are not judged.</summary>
        public float SettleSeconds = 1.2f;
        /// <summary>A gap this long between two looks (the button was let go) counts as a move.</summary>
        public float GapSeconds = 1.5f;

        /// <summary>True while the focus is held (by this rule, the padlock button or a tap).</summary>
        public bool Holding { get; private set; }
        /// <summary>How often this rule has said "focus now" since the last Reset (for the tests and the log).</summary>
        public int Orders { get; private set; }

        readonly Queue<bool> remembered = new Queue<bool>();     // true = that look was soft
        int softRun, movingRun;
        bool moved, lastWasSharp;
        float settledAt = float.MinValue, lastLook = float.MinValue;

        /// <summary>
        /// One look of the scan. `moving` = the picture changed a lot since the look before. `judged` = the picture has
        /// edges to judge the focus by (not dark, not a plain surface). `soft` = those edges are out of focus.
        /// Returns true when the camera should focus now and hold.
        /// </summary>
        public bool Look(bool moving, bool judged, bool soft, float now)
        {
            if (lastLook > float.MinValue && now - lastLook > GapSeconds) NoteMove();
            lastLook = now;
            if (now < settledAt) return false;               // the lens is still on its way from the last order

            if (moving)
            {
                softRun = 0;
                if (++movingRun >= MovingLooksForAMove) NoteMove();
                return false;
            }
            movingRun = 0;
            if (!judged) return false;                       // nothing to tell sharp from soft by

            if (!soft)
            {
                softRun = 0; moved = false; lastWasSharp = true;
                Remember(false);
                return false;
            }
            softRun++;
            Remember(true);
            bool lost = lastWasSharp;                        // sharp a moment ago, soft now
            lastWasSharp = false;

            if (Holding)
                return softRun >= (moved ? SoftLooksAfterAMove : SoftLooksToRefocus) && Order(now);
            if (!Automatic) return false;
            if (softRun >= SoftLooksToLock) return Order(now);
            return lost && Losses() >= LossesToLock && Order(now);
        }

        /// <summary>The focus was locked from outside just now (the padlock button, or a tap on the picture).</summary>
        public void HeldFromOutside(float now)
        {
            Holding = true;
            settledAt = now + SettleSeconds;
            Forget();
        }

        /// <summary>The lock was ended (the padlock button), or the camera was closed.</summary>
        public void Released()
        {
            Holding = false;
            settledAt = float.MinValue;
            Forget();
        }

        /// <summary>A new scan: nothing is remembered and nothing is held.</summary>
        public void Reset()
        {
            Released();
            lastLook = float.MinValue; Orders = 0;
        }

        bool Order(float now)
        {
            Holding = true; Orders++;
            settledAt = now + SettleSeconds;
            Forget();
            return true;
        }

        // the phone or the product was moved: what was seen before says nothing about the focus now
        void NoteMove()
        {
            remembered.Clear();
            softRun = 0; lastWasSharp = false;
            moved = Holding;
        }

        void Forget()
        {
            remembered.Clear();
            softRun = movingRun = 0; moved = false; lastWasSharp = false;
        }

        void Remember(bool soft)
        {
            remembered.Enqueue(soft);
            while (remembered.Count > System.Math.Max(2, LooksRemembered)) remembered.Dequeue();
        }

        // how many times the remembered looks go from sharp to soft
        int Losses()
        {
            int losses = 0; bool first = true, before = false;
            foreach (bool soft in remembered)
            {
                if (!first && soft && !before) losses++;
                before = soft; first = false;
            }
            return losses;
        }
    }
}
