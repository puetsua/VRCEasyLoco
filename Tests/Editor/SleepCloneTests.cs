using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>Sleep clones own their leaves. 5m trees offset RootT.y; names come from the slot.</summary>
    public class SleepCloneTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private string _folder;
        private Dictionary<(string, bool), AnimationClip> _clipCache;

        [SetUp]
        public void SetUp()
        {
            _folder = "Assets/__EasyLocoSleepTests_" + System.Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(_folder));
            _clipCache = new Dictionary<(string, bool), AnimationClip>();
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(_folder))
            {
                AssetDatabase.DeleteAsset(_folder);
            }

            foreach (var spawn in spawned)
            {
                Object.DestroyImmediate(spawn);
            }
            spawned.Clear();
        }

        [Test]
        public void EmptyReplacementsStillCloneAndDuplicateASleepTree()
        {
            var leaf = Clip("SleepUp", 0.5f);
            var source = Tree("DefaultSleepingFacingUp", Child(leaf));

            var clone = CloneForSleep(source, new Dictionary<string, Motion>());

            Assert.That(clone, Is.Not.SameAs(source), "the shared package tree was not cloned");
            var duplicated = (AnimationClip)clone.children[0].motion;
            Assert.That(duplicated, Is.Not.SameAs(leaf), "the avatar still points at the package clip");
            Assert.That(AssetDatabase.GetAssetPath(duplicated), Is.EqualTo(_folder + "/ELSleepUp.anim"));
            Assert.That(duplicated.name, Is.EqualTo("ELSleepUp"),
                "the clip object name must match the file or Unity warns on import");
            Assert.That(RootY(duplicated), Is.EqualTo(0.5f).Within(0.0001f),
                "a non-5m tree must duplicate the pose, not shift it");
        }

        [Test]
        public void EachSourceYieldsOnePlainAndOne5mClip()
        {
            var leaf = Clip("SleepUp", 0.5f);
            var source = Tree("SleepRoot",
                Child(Tree("DefaultSleepingFacingUp", Child(leaf))),
                Child(Tree("DefaultSleepingFacingUp5m", Child(leaf))));

            CloneForSleep(source, new Dictionary<string, Motion>());

            Assert.That(FindClip("ELSleepUp.anim"), Is.Not.Null,
                "the plain duplicate should exist");
            Assert.That(FindClip("ELSleepUp5m.anim"), Is.Not.Null,
                "the height duplicate should exist");
            Assert.That(FindClip("ELSleepUp.anim").name, Is.EqualTo("ELSleepUp"));
            Assert.That(FindClip("ELSleepUp5m.anim").name, Is.EqualTo("ELSleepUp5m"));
        }

        [Test]
        public void FiveMetreTreeShiftsRootTYByFiveButPlainTreeDoesNot()
        {
            var leaf = Clip("SleepUp", 0.5f);
            var source = Tree("SleepRoot",
                Child(Tree("DefaultSleepingFacingUp", Child(leaf))),
                Child(Tree("DefaultSleepingFacingUp5m", Child(leaf))));

            var clone = CloneForSleep(source, new Dictionary<string, Motion>());

            var plainTree = (BlendTree)clone.children[0].motion;
            var fiveMetreTree = (BlendTree)clone.children[1].motion;
            Assert.That(RootY((AnimationClip)plainTree.children[0].motion), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(RootY((AnimationClip)fiveMetreTree.children[0].motion), Is.EqualTo(5.5f).Within(0.0001f),
                "the 5m duplicate should sit 5 units higher on RootT.y");
        }

        [Test]
        public void Nested5mTreeIsStillOffset()
        {
            var leaf = Clip("SleepUp", 0.5f);
            var source = Tree("SleepRoot",
                Child(Tree("Mid", Child(Tree("DefaultSleepingFacingUp5m", Child(leaf))))));

            var clone = CloneForSleep(source, new Dictionary<string, Motion>());

            var mid = (BlendTree)clone.children[0].motion;
            var fiveMetre = (BlendTree)mid.children[0].motion;
            Assert.That(RootY((AnimationClip)fiveMetre.children[0].motion), Is.EqualTo(5.5f).Within(0.0001f),
                "a nested 5m tree was not offset");
        }

        [Test]
        public void DuplicateNamesAreNeverDoubled()
        {
            var leaf = Clip("SleepUp", 0.5f);
            var source = Tree("DefaultSleepingFacingUp", Child(leaf));

            CloneForSleep(source, new Dictionary<string, Motion>());

            Assert.That(FindClip("ELSleepUp.anim"), Is.Not.Null);
            Assert.That(FindClip("ELELSleepUp.anim"), Is.Null,
                "duplication doubled the prefix - the clip was re-duplicated");
        }

        [Test]
        public void NonSleepTreeLeavesAreLeftAlone()
        {
            var leaf = Clip("JustAGait", 1f);
            var source = Tree("GaitTree", Child(leaf));

            var clone = CloneForSleep(source, new Dictionary<string, Motion>());

            Assert.That(clone.children[0].motion, Is.SameAs(leaf),
                "a non-sleep tree should not have its leaf replaced");
            Assert.That(FindClip("ELJustAGait.anim"), Is.Null);
        }

        [Test]
        public void SiblingSleepTreesSharingOneLeafBothResolve()
        {
            var shared = Clip("SleepUp", 0.5f);
            var up = Tree("DefaultSleepingFacingUp", Child(shared));
            var feetLock = Tree("DefaultSleepingFacingUpFeetLock", Child(shared));

            var cloneUp = CloneForSleep(up, new Dictionary<string, Motion>());
            var cloneFeetLock = CloneForSleep(feetLock, new Dictionary<string, Motion>());

            var leafUp = (AnimationClip)cloneUp.children[0].motion;
            var leafFeetLock = (AnimationClip)cloneFeetLock.children[0].motion;
            Assert.That(leafUp, Is.Not.Null, "the first clone's leaf was orphaned");
            Assert.That(leafFeetLock, Is.Not.Null, "the second clone's leaf did not resolve");
            Assert.That(AssetDatabase.GetAssetPath(leafUp), Is.EqualTo(_folder + "/ELSleepUp.anim"));
            Assert.That(AssetDatabase.GetAssetPath(leafFeetLock), Is.EqualTo(AssetDatabase.GetAssetPath(leafUp)),
                "two clones of one shared leaf must point at the same generated asset");
            Assert.That(RootY(leafUp), Is.EqualTo(0.5f).Within(0.0001f), "the shared leaf lost its pose");
            Assert.That(RootY(leafFeetLock), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void RebuildOverwritesAChangedSourceClip()
        {
            var leaf = Clip("SleepUp", 0.5f);
            var source = Tree("DefaultSleepingFacingUp", Child(leaf));

            var firstClone = CloneForSleep(source, new Dictionary<string, Motion>());
            var firstPath = AssetDatabase.GetAssetPath(((AnimationClip)firstClone.children[0].motion));
            Assert.That(RootY((AnimationClip)firstClone.children[0].motion), Is.EqualTo(0.5f).Within(0.0001f));

            SetRootY(leaf, 2.5f);
            _clipCache = new Dictionary<(string, bool), AnimationClip>();
            var secondClone = CloneForSleep(source, new Dictionary<string, Motion>());
            var secondLeaf = (AnimationClip)secondClone.children[0].motion;

            Assert.That(RootY(secondLeaf), Is.EqualTo(2.5f).Within(0.0001f),
                "the generated clip still carries the old pose");
            Assert.That(AssetDatabase.GetAssetPath(secondLeaf), Is.EqualTo(firstPath),
                "the rebuild should overwrite the same asset, keeping its GUID");
        }

        [Test]
        public void UserOverrideIsNamedFromTheEasyLocoSlotNotTheClipFile()
        {
            var usersClip = Clip("MyNap", 0.5f);
            var source = Tree("DefaultSleepingFacingUp", Child(Clip("SleepUp", 0.5f)));
            var replacements = new Dictionary<string, Motion> { { EasyLocoConst.SleepUpTarget, usersClip } };

            var clone = CloneForSleep(source, replacements);

            var generated = (AnimationClip)clone.children[0].motion;
            Assert.That(AssetDatabase.GetAssetPath(generated), Is.EqualTo(_folder + "/ELSleepUp.anim"));
            Assert.That(generated.name, Is.EqualTo("ELSleepUp"));
            Assert.That(FindClip("ELMyNap.anim"), Is.Null);
            Assert.That(RootY(generated), Is.EqualTo(0.5f).Within(0.0001f));
        }

        private BlendTree CloneForSleep(BlendTree source, IReadOnlyDictionary<string, Motion> replacements)
        {
            return EasyLocoModularAvatarBuilder.CloneBlendTree(
                source, new MotionReplacements(replacements), _folder, _clipCache, isSleepBuild: true);
        }

        private AnimationClip FindClip(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(_folder + "/" + fileName);
        }

        private AnimationClip Clip(string name, float rootY)
        {
            var clip = new AnimationClip { name = name };
            var binding = new EditorCurveBinding { path = string.Empty, type = typeof(Animator), propertyName = "RootT.y" };
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f, rootY));
            spawned.Add(clip);
            return clip;
        }

        private static float RootY(AnimationClip clip)
        {
            var binding = new EditorCurveBinding { path = string.Empty, type = typeof(Animator), propertyName = "RootT.y" };
            return AnimationUtility.GetEditorCurve(clip, binding).keys[0].value;
        }

        private static void SetRootY(AnimationClip clip, float rootY)
        {
            var binding = new EditorCurveBinding { path = string.Empty, type = typeof(Animator), propertyName = "RootT.y" };
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f, rootY));
        }

        private BlendTree Tree(string name, params ChildMotion[] children)
        {
            // Unity redistributes child thresholds if this is still true when children are assigned.
            var tree = new BlendTree { name = name, useAutomaticThresholds = false };
            spawned.Add(tree);
            tree.children = children;
            return tree;
        }

        private static ChildMotion Child(Motion motion)
        {
            return new ChildMotion { motion = motion, timeScale = 1f };
        }
    }
}
