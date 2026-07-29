using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// The rule under test: a language switch re-labels the poses EasyLoco itself wrote, and leaves
    /// the user's alone. Row 0 always follows the language because its name field is locked in the
    /// inspector, and "has the user touched this?" is decided per stance, so customising the stand
    /// poses must not freeze crouch and prone.
    /// </summary>
    public class IdlePoseSyncTests
    {
        private SupportedLanguage previousLanguage;
        private readonly List<Object> spawned = new List<Object>();

        private LocalizedTextDataset English;
        private LocalizedTextDataset Chinese;

        [SetUp]
        public void SetUp()
        {
            previousLanguage = LocalizedTextDataset.Current;
            English = DatasetFor(SupportedLanguage.English);
            Chinese = DatasetFor(SupportedLanguage.ChineseTraditional);
        }

        [TearDown]
        public void TearDown()
        {
            LocalizedTextDataset.SetLanguage(previousLanguage);

            foreach (var clip in spawned)
            {
                Object.DestroyImmediate(clip);
            }
            spawned.Clear();
        }

        // Reaches a dataset without leaving the globals changed, and without writing EditorPrefs.
        private static LocalizedTextDataset DatasetFor(SupportedLanguage language)
        {
            var saved = LocalizedTextDataset.Current;
            LocalizedTextDataset.SetLanguage(language);
            var dataset = LocalizedTextDataset.primary;
            LocalizedTextDataset.SetLanguage(saved);
            return dataset;
        }

        private AnimationClip StrangerClip()
        {
            var clip = new AnimationClip { name = "NotAnEasyLocoClip" };
            spawned.Add(clip);
            return clip;
        }

        private static List<string> NamesOf(List<EasyLoco.IdlePose> poses)
        {
            return poses.Select(pose => pose.menuName).ToList();
        }

        private static List<EasyLoco.IdlePose> StandIn(LocalizedTextDataset text)
        {
            return EasyLocoEditor.BuildDefaults(EasyLocoEditor.StandDefaults, text);
        }

        [Test]
        public void FreshlyBuiltStanceIsPristine()
        {
            Assert.That(EasyLocoEditor.IsPristine(StandIn(English), EasyLocoEditor.StandDefaults), Is.True);
            Assert.That(EasyLocoEditor.IsPristine(StandIn(Chinese), EasyLocoEditor.StandDefaults), Is.True);
        }

        [Test]
        public void NamesFromAnotherLanguageStillCountAsPristine()
        {
            // The point of LocalizedTextDataset.All: a component authored in English must not look
            // customised just because the user has since switched language.
            var poses = StandIn(English);

            Assert.That(EasyLocoEditor.IsPristine(poses, EasyLocoEditor.StandDefaults), Is.True);
            Assert.That(NamesOf(poses), Is.EqualTo(new[]
            {
                English.poseDefault, English.poseStandWide1, English.poseStandWide2,
            }));
        }

        [Test]
        public void PristineStanceUpdatesEveryRow()
        {
            var poses = StandIn(English);

            var changed = EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, Chinese);

            Assert.That(changed, Is.True);
            Assert.That(NamesOf(poses), Is.EqualTo(new[]
            {
                Chinese.poseDefault, Chinese.poseStandWide1, Chinese.poseStandWide2,
            }));
        }

        [Test]
        public void CustomisedStanceKeepsItsNamesButStillUpdatesRowZero()
        {
            var poses = StandIn(English);
            poses[1].menuName = "MyPose";

            var changed = EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, Chinese);

            Assert.That(changed, Is.True, "row 0 should still have been re-labelled");
            Assert.That(NamesOf(poses), Is.EqualTo(new[]
            {
                Chinese.poseDefault, "MyPose", English.poseStandWide2,
            }), "only row 0 follows the language once the stance has been touched");
        }

        [Test]
        public void StancesAreJudgedIndependently()
        {
            var stand = StandIn(English);
            var crouch = EasyLocoEditor.BuildDefaults(EasyLocoEditor.CrouchDefaults, English);
            var prone = EasyLocoEditor.BuildDefaults(EasyLocoEditor.ProneDefaults, English);
            stand[1].menuName = "MyPose";

            EasyLocoEditor.SyncPoseNames(stand, EasyLocoEditor.StandDefaults, Chinese);
            EasyLocoEditor.SyncPoseNames(crouch, EasyLocoEditor.CrouchDefaults, Chinese);
            EasyLocoEditor.SyncPoseNames(prone, EasyLocoEditor.ProneDefaults, Chinese);

            Assert.That(NamesOf(stand)[1], Is.EqualTo("MyPose"), "the edited stance kept its name");
            Assert.That(NamesOf(crouch), Is.EqualTo(new[] { Chinese.poseDefault, Chinese.poseCrouchSquatting }),
                "crouch was untouched and should have followed the language");
            Assert.That(NamesOf(prone), Is.EqualTo(new[] { Chinese.poseDefault, Chinese.poseProneLyingDown }),
                "prone was untouched and should have followed the language");
        }

        [Test]
        public void RowZeroNameIsNotPartOfThePristineTest()
        {
            // That field is disabled in the inspector, so a stray value there cannot be a user edit
            // and must not be read as one.
            var poses = StandIn(English);
            poses[0].menuName = "something else entirely";

            Assert.That(EasyLocoEditor.IsPristine(poses, EasyLocoEditor.StandDefaults), Is.True);

            EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, Chinese);

            Assert.That(NamesOf(poses), Is.EqualTo(new[]
            {
                Chinese.poseDefault, Chinese.poseStandWide1, Chinese.poseStandWide2,
            }));
        }

        [Test]
        public void AddedRowMakesTheStanceCustomised()
        {
            var poses = StandIn(English);
            poses.Add(new EasyLoco.IdlePose("Extra", null));

            Assert.That(EasyLocoEditor.IsPristine(poses, EasyLocoEditor.StandDefaults), Is.False);

            EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, Chinese);

            Assert.That(NamesOf(poses), Is.EqualTo(new[]
            {
                Chinese.poseDefault, English.poseStandWide1, English.poseStandWide2, "Extra",
            }));
        }

        [Test]
        public void RemovedRowMakesTheStanceCustomised()
        {
            var poses = StandIn(English);
            poses.RemoveAt(2);

            Assert.That(EasyLocoEditor.IsPristine(poses, EasyLocoEditor.StandDefaults), Is.False);
        }

        [Test]
        public void SwappedClipMakesTheStanceCustomised()
        {
            var poses = StandIn(English);
            poses[1].clip = StrangerClip();

            Assert.That(EasyLocoEditor.IsPristine(poses, EasyLocoEditor.StandDefaults), Is.False);

            EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, Chinese);

            Assert.That(NamesOf(poses)[1], Is.EqualTo(English.poseStandWide1),
                "a slot the user re-pointed at their own clip keeps its name");
        }

        [Test]
        public void SyncingTwiceReportsNoSecondChange()
        {
            var poses = StandIn(English);

            Assert.That(EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, Chinese), Is.True);
            Assert.That(EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, Chinese), Is.False,
                "a no-op sync must not report a change - it would dirty the component on every repaint");
        }

        [Test]
        public void SyncingBackRestoresTheOriginalNames()
        {
            var poses = StandIn(English);
            var before = NamesOf(poses);

            EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, Chinese);
            EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, English);

            Assert.That(NamesOf(poses), Is.EqualTo(before));
        }

        [Test]
        public void EmptyAndNullListsAreHandled()
        {
            Assert.DoesNotThrow(() =>
            {
                Assert.That(EasyLocoEditor.SyncPoseNames(null, EasyLocoEditor.StandDefaults, Chinese), Is.False);
                Assert.That(EasyLocoEditor.SyncPoseNames(new List<EasyLoco.IdlePose>(),
                    EasyLocoEditor.StandDefaults, Chinese), Is.False);
                Assert.That(EasyLocoEditor.IsPristine(null, EasyLocoEditor.StandDefaults), Is.False);
            });
        }

        [Test]
        public void ShorterListThanTheSpecDoesNotOverrun()
        {
            // rows collapses to 1 when the stance is not pristine, so a single-row list must still be
            // safe to index.
            var poses = new List<EasyLoco.IdlePose> { new EasyLoco.IdlePose("whatever", null) };

            Assert.DoesNotThrow(() =>
                EasyLocoEditor.SyncPoseNames(poses, EasyLocoEditor.StandDefaults, Chinese));
            Assert.That(poses[0].menuName, Is.EqualTo(Chinese.poseDefault));
        }
    }
}
