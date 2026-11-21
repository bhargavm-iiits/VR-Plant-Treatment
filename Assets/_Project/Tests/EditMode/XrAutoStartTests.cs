using BTP.XR;
using NUnit.Framework;

namespace BTP.Tests
{
    /// <summary>The automatic headset start's retry schedule: 1 s first, then 3 s growing to at most 10 s.</summary>
    public sealed class XrAutoStartTests
    {
        [Test]
        public void FirstRetryComesAfterOneSecond()
        {
            Assert.That(XrAutoStart.RetryDelaySeconds(0), Is.EqualTo(1f));
            Assert.That(XrAutoStart.RetryDelaySeconds(-5), Is.EqualTo(1f));
        }

        [Test]
        public void RetriesStartAtThreeSecondsAndGrow()
        {
            Assert.That(XrAutoStart.RetryDelaySeconds(1), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(XrAutoStart.RetryDelaySeconds(2), Is.EqualTo(4.5f).Within(1e-4f));
            Assert.That(XrAutoStart.RetryDelaySeconds(3), Is.EqualTo(6.75f).Within(1e-4f));
            Assert.That(XrAutoStart.RetryDelaySeconds(4), Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void ScheduleNeverDropsBelowThreeAfterAFailureAndNeverExceedsTen()
        {
            var previous = 0f;
            for (var failures = 1; failures < 10000; failures++)
            {
                var delay = XrAutoStart.RetryDelaySeconds(failures);
                Assert.That(delay, Is.GreaterThanOrEqualTo(XrAutoStart.MinRetrySeconds), $"failures={failures}");
                Assert.That(delay, Is.LessThanOrEqualTo(XrAutoStart.MaxRetrySeconds), $"failures={failures}");
                Assert.That(delay, Is.GreaterThanOrEqualTo(previous), $"failures={failures}");
                Assert.That(float.IsNaN(delay) || float.IsInfinity(delay), Is.False);
                previous = delay;
            }

            Assert.That(XrAutoStart.RetryDelaySeconds(int.MaxValue), Is.EqualTo(XrAutoStart.MaxRetrySeconds));
        }

        [Test]
        public void HintTellsTheUserWhatToDo()
        {
            StringAssert.Contains("Quest Link", XrAutoStart.HintText);
            StringAssert.Contains("automatically", XrAutoStart.HintText);
        }
    }
}
