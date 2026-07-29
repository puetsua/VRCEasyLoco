using System.Linq;
using NUnit.Framework;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// Menu values for the idle pose selector. A synced VRChat Float only carries -1..1, and this
    /// regressed once already: raw indices put "Wide2" at the clamped value 1 alongside "Wide1", so
    /// the menu drew Wide1 as active and clicking it read as switching it off.
    /// </summary>
    public class PoseValueTests
    {
        [Test]
        public void SinglePoseSitsAtZero()
        {
            Assert.That(EasyLocoModularAvatarBuilder.PoseValue(0, 1), Is.EqualTo(0f));
            Assert.That(EasyLocoModularAvatarBuilder.PoseValue(0, 0), Is.EqualTo(0f));
        }

        [Test]
        public void PosesSpreadEvenlyAcrossTheRange()
        {
            Assert.That(EasyLocoModularAvatarBuilder.PoseValue(0, 3), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(EasyLocoModularAvatarBuilder.PoseValue(1, 3), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(EasyLocoModularAvatarBuilder.PoseValue(2, 3), Is.EqualTo(1f).Within(1e-6f));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(8)]
        public void EveryPoseIsSyncableAndDistinct(int count)
        {
            var values = Enumerable.Range(0, count)
                .Select(index => EasyLocoModularAvatarBuilder.PoseValue(index, count))
                .ToArray();

            Assert.That(values, Is.All.InRange(-1f, 1f), "a value outside -1..1 clamps when synced");
            Assert.That(values.Distinct().Count(), Is.EqualTo(count),
                "two poses share a value, so selecting one would light up the other");
        }

        [Test]
        public void ValuesIncreaseWithIndex()
        {
            var values = Enumerable.Range(0, 6)
                .Select(index => EasyLocoModularAvatarBuilder.PoseValue(index, 6))
                .ToArray();

            Assert.That(values, Is.Ordered.Ascending);
        }
    }
}
