using System;
using GlowUp.Common;
using GlowUp.Onboarding;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the age that sign-up Step 1 and Step 5 show next to the birthday (InputRules.CalculateAge), and the day the
    /// calendar keeps when only the month or the year is changed (DatePickerPopup.DayIn).
    /// </summary>
    public class BirthdayTests
    {
        static readonly DateTime Today = new DateTime(2026, 10, 12);

        [Test]
        public void TheAgeCountsOnlyBirthdaysThatHavePassed()
        {
            Assert.AreEqual(19, InputRules.CalculateAge(new DateTime(2006, 10, 15), Today), "the birthday is in three days: still 19");
            Assert.AreEqual(20, InputRules.CalculateAge(new DateTime(2006, 10, 12), Today), "the birthday is today: 20");
            Assert.AreEqual(20, InputRules.CalculateAge(new DateTime(2006, 10, 11), Today), "the birthday was yesterday");
            Assert.AreEqual(13, InputRules.CalculateAge(new DateTime(2013, 1, 1), Today));
            Assert.AreEqual(12, InputRules.CalculateAge(new DateTime(2013, 12, 31), Today), "too young to sign up until the end of December");
            Assert.AreEqual(0, InputRules.CalculateAge(new DateTime(2026, 10, 12), Today));
            Assert.AreEqual(0, InputRules.CalculateAge(new DateTime(2027, 1, 1), Today), "a date that has not come is never a negative age");
        }

        [Test]
        public void SomebodyBornOnALeapDayHasTheirBirthdayOnTheLastDayOfFebruary()
        {
            var born = new DateTime(2004, 2, 29);
            Assert.AreEqual(20, InputRules.CalculateAge(born, new DateTime(2025, 2, 27)));
            Assert.AreEqual(21, InputRules.CalculateAge(born, new DateTime(2025, 2, 28)));
            Assert.AreEqual(24, InputRules.CalculateAge(born, new DateTime(2028, 2, 29)));
        }

        [Test]
        public void ChangingTheMonthOrTheYearKeepsTheDay()
        {
            Assert.AreEqual(new DateTime(2004, 10, 15), DatePickerPopup.DayIn(new DateTime(2004, 10, 1), 15, Today), "the year was changed: the 15th stays the 15th");
            Assert.AreEqual(new DateTime(2006, 3, 15), DatePickerPopup.DayIn(new DateTime(2006, 3, 1), 15, Today));
            Assert.AreEqual(new DateTime(2006, 2, 28), DatePickerPopup.DayIn(new DateTime(2006, 2, 1), 31, Today), "February has no 31st: its last day");
            Assert.AreEqual(new DateTime(2008, 2, 29), DatePickerPopup.DayIn(new DateTime(2008, 2, 1), 31, Today), "a leap year");
            Assert.AreEqual(new DateTime(2006, 4, 30), DatePickerPopup.DayIn(new DateTime(2006, 4, 1), 31, Today));
            Assert.AreEqual(Today, DatePickerPopup.DayIn(new DateTime(2026, 10, 1), 25, Today), "never a day that has not come");
            Assert.AreEqual(new DateTime(2026, 10, 5), DatePickerPopup.DayIn(new DateTime(2026, 10, 1), 5, Today));
        }
    }
}
