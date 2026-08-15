using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// The sleep build turns the shared DefaultSleeping* package trees into per-avatar copies whose
    /// leaf clips the avatar owns, and when a tree is a &lt;name&gt;5m height tree it makes the leaf
    /// copy with +5 added to RootT.y. Four times this feature broke by silently re-using or
    /// re-rooting clips - see the fix history around the sleep clone - so it finally gets tests that
    /// pin the four behaviours at once:
    ///
    ///   * an empty replacement set still clones and duplicates a DefaultSleeping* tree,
    ///   * each source clip yields exactly one plain and one 5m duplicate,
    ///   * 5m trees get RootT.y + 5 and non-5m trees do not,
    ///   * a 5m tree nested anywhere under the root is still offset, and
    ///   * duplicate names are never doubled (no ELSleepELSleep...).
    ///
    /// The tests exercise CloneBlendTree rather than CloneBlendTreeInMemory because duplication
    /// deliberately runs on the root clone the way the build calls it, with one per-build cache.
    /// </summary>
    public class SleepCloneTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private string _folder;
        private Dictionary<(AnimationClip, bool), AnimationClip> _clipCache;

        [SetUp]
        public void SetUp()
        {
            _folder = "Assets/__EasyLocoSleepTests_" + System.Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(_folder));
            // One cache per test, mirroring the build's single per-build cache shared by every
            // CloneBlendTree call. A test that wants a fresh build resets this.
            _clipCache = new Dictionary<(AnimationClip, bool), AnimationClip>();
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
            // The whole point of the sleep build's isSleepBuild flag: a user who overrode nothing
            // still has to end up owning the clip, so the tree must clone and duplicate even though
            // the replacement ledger is empty.
            var leaf = Clip("SleepUp", 0.5f);
            var source = Tree("DefaultSleepingFacingUp", Child(leaf));

            var clone = CloneForSleep(source, new Dictionary<string, Motion>());

            Assert.That(clone, Is.Not.SameAs(source), "the shared package tree was not cloned");
            var duplicated = (AnimationClip)clone.children[0].motion;
            Assert.That(duplicated, Is.Not.SameAs(leaf), "the avatar still points at the package clip");
            Assert.That(AssetDatabase.GetAssetPath(duplicated), Is.EqualTo(_folder + "/ELSleepSleepUp.anim"));
            Assert.That(RootY(duplicated), Is.EqualTo(0.5f).Within(0.0001f),
                "a non-5m tree must duplicate the pose, not shift it");
        }

        [Test]
        public void EachSourceYieldsOnePlainAndOne5mClip()
        {
            // The same source clip sits in both a plain tree and its height variant. One build must
            // produce exactly one ELSleep<clip>.anim and one ELSleep<clip>5m.anim - not one per tree
            // it appears in.
            var leaf = Clip("SleepUp", 0.5f);
            var source = Tree("SleepRoot",
                Child(Tree("DefaultSleepingFacingUp", Child(leaf))),
                Child(Tree("DefaultSleepingFacingUp5m", Child(leaf))));

            CloneForSleep(source, new Dictionary<string, Motion>());

            Assert.That(FindClip("ELSleepSleepUp.anim"), Is.Not.Null,
                "the plain duplicate should exist");
            Assert.That(FindClip("ELSleepSleepUp5m.anim"), Is.Not.Null,
                "the height duplicate should exist");
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
            // The height tree buried under an intermediate blender must still be shifted - a 5m
            // tree two levels down is the exact case that regressed once.
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

            Assert.That(FindClip("ELSleepSleepUp.anim"), Is.Not.Null);
            Assert.That(FindClip("ELSleepELSleepSleepUp.anim"), Is.Null,
                "duplication doubled the prefix - the clip was re-duplicated");
        }

        [Test]
        public void NonSleepTreeLeavesAreLeftAlone()
        {
            // A tree that is neither a DefaultSleeping* nor a 5m tree is not part of the sleep
            // build's worry, so its shared clips must survive untouched - no clone, no duplicate.
            var leaf = Clip("JustAGait", 1f);
            var source = Tree("GaitTree", Child(leaf));

            var clone = CloneForSleep(source, new Dictionary<string, Motion>());

            Assert.That(clone.children[0].motion, Is.SameAs(leaf),
                "a non-sleep tree should not have its leaf replaced");
            Assert.That(FindClip("ELSleepJustAGait.anim"), Is.Null);
        }

        [Test]
        public void SiblingSleepTreesSharingOneLeafBothResolve()
        {
            // The sleep template has several DefaultSleeping* assets (FacingUp, FacingUpFeetLock,
            // ...) that share the same on-side leaf GUID. The build clones each one independently
            // through its own CloneBlendTree call, so they must resolve to the same ELSleep asset.
            // With a per-clone cache and delete-then-create, the first clone's reference would be
            // orphaned when the second recreated the file with a fresh GUID.
            var shared = Clip("SleepUp", 0.5f);
            var up = Tree("DefaultSleepingFacingUp", Child(shared));
            var feetLock = Tree("DefaultSleepingFacingUpFeetLock", Child(shared));

            // Two separate CloneBlendTree calls, one shared build cache, one output folder - the
            // shape of the real build.
            var cloneUp = CloneForSleep(up, new Dictionary<string, Motion>());
            var cloneFeetLock = CloneForSleep(feetLock, new Dictionary<string, Motion>());

            var leafUp = (AnimationClip)cloneUp.children[0].motion;
            var leafFeetLock = (AnimationClip)cloneFeetLock.children[0].motion;
            Assert.That(leafUp, Is.Not.Null, "the first clone's leaf was orphaned");
            Assert.That(leafFeetLock, Is.Not.Null, "the second clone's leaf did not resolve");
            Assert.That(AssetDatabase.GetAssetPath(leafUp), Is.EqualTo(_folder + "/ELSleepSleepUp.anim"));
            Assert.That(AssetDatabase.GetAssetPath(leafFeetLock), Is.EqualTo(AssetDatabase.GetAssetPath(leafUp)),
                "two clones of one shared leaf must point at the same generated asset");
            Assert.That(RootY(leafUp), Is.EqualTo(0.5f).Within(0.0001f), "the shared leaf lost its pose");
            Assert.That(RootY(leafFeetLock), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void RebuildOverwritesAChangedSourceClip()
        {
            // Changing a package default or the user's override and rebuilding must update the
            // generated clip, not pin the stale animation. The rebuild uses a fresh cache (a real
            // second build would) and must overwrite the existing ELSleep asset in place, keeping
            // its GUID so earlier references survive.
            var leaf = Clip("SleepUp", 0.5f);
            var source = Tree("DefaultSleepingFacingUp", Child(leaf));

            var firstClone = CloneForSleep(source, new Dictionary<string, Motion>());
            var firstPath = AssetDatabase.GetAssetPath(((AnimationClip)firstClone.children[0].motion));
            Assert.That(RootY((AnimationClip)firstClone.children[0].motion), Is.EqualTo(0.5f).Within(0.0001f));

            // The source changed between builds; a fresh cache simulates the new build.
            SetRootY(leaf, 2.5f);
            _clipCache = new Dictionary<(AnimationClip, bool), AnimationClip>();
            var secondClone = CloneForSleep(source, new Dictionary<string, Motion>());
            var secondLeaf = (AnimationClip)secondClone.children[0].motion;

            Assert.That(RootY(secondLeaf), Is.EqualTo(2.5f).Within(0.0001f),
                "the generated clip still carries the old pose");
            Assert.That(AssetDatabase.GetAssetPath(secondLeaf), Is.EqualTo(firstPath),
                "the rebuild should overwrite the same asset, keeping its GUID");
        }

        private BlendTree CloneForSleep(BlendTree source, IReadOnlyDictionary<string, Motion> replacements)
        {
            // _folder is a real Assets folder so CloneBlendTree can write the clone and duplicates.
            // The returned tree is the object CloneBlendTree saved: its duplicated leaf clips are
            // persisted assets (ELSleep*.anim), so their asset paths are valid in memory too - a
            // non-sleep leaf that stays a shared in-memory clip is exactly what the last test needs
            // to see without a reload that would drop it. The shared build cache is threaded through
            // so CloneBlendTree calls within one test share it, as they do in the real build.
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
