using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>Clones must not mutate the source tree, and every serialized BlendTree field must survive.</summary>
    public class BlendTreeCloneTests
    {
        private const string StandingTreePath =
            EasyLocoConst.PackageRoot + "/Animations/Idle/DefaultStanding.asset";

        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var spawn in spawned)
            {
                Object.DestroyImmediate(spawn);
            }
            spawned.Clear();
        }

        [Test]
        public void CloningLeavesTheSourceTreeUntouched()
        {
            var keep = Clip("SomeoneElsesClip");
            var target = Clip("StandIdle");
            var replacement = Clip("UsersOwnIdle");

            var source = Tree("Source", Child(keep), Child(target));
            var replacements = new Dictionary<string, Motion> { { "StandIdle", replacement } };

            var clone = CloneOf(source, replacements);

            Assert.That(clone.children[1].motion, Is.SameAs(replacement),
                "the clone should carry the user's clip");
            Assert.That(source.children[1].motion, Is.SameAs(target),
                "the shared package asset was rewritten - every later build inherits this");
            Assert.That(source.children[0].motion, Is.SameAs(keep));
            Assert.That(source.children.Length, Is.EqualTo(2));
        }

        [Test]
        public void CloneMatchesTheSourceOnEverySerializedField()
        {
            var source = AssetDatabase.LoadAssetAtPath<BlendTree>(StandingTreePath);
            Assume.That(source, Is.Not.Null, $"Standing idle tree not found at {StandingTreePath}");

            var clone = CloneOf(source, new Dictionary<string, Motion>());

            var mismatched = new List<string>();
            var sourceProperty = new SerializedObject(source).GetIterator();
            var cloneProperties = new SerializedObject(clone);
            while (sourceProperty.NextVisible(true))
            {
                var cloneProperty = cloneProperties.FindProperty(sourceProperty.propertyPath);
                if (cloneProperty == null || !SerializedProperty.DataEquals(sourceProperty, cloneProperty))
                {
                    mismatched.Add(sourceProperty.propertyPath);
                }
            }

            Assert.That(mismatched, Is.Empty,
                "these serialized fields did not survive the copy: " + string.Join(", ", mismatched));
        }

        [Test]
        public void NormalizedBlendValuesSurvivesTheClone()
        {
            var source = Tree("Direct", Child(Clip("A")));
            SetNormalizedBlendValues(source, true);

            var clone = CloneOf(source, new Dictionary<string, Motion>());

            Assert.That(NormalizedBlendValues(clone), Is.True,
                "m_NormalizedBlendValues was dropped - a Direct tree would blend wrongly and silently");
        }

        [Test]
        public void EveryChildMotionFieldSurvivesTheClone()
        {
            var clip = Clip("Walk");
            var source = Tree("Source", new ChildMotion
            {
                motion = clip,
                threshold = 0.37f,
                position = new Vector2(1.5f, -2.5f),
                timeScale = -2f,
                cycleOffset = 0.5f,
                directBlendParameter = "VelocityX",
                mirror = true,
            });

            var child = CloneOf(source, new Dictionary<string, Motion>()).children[0];

            Assert.That(child.threshold, Is.EqualTo(0.37f).Within(0.0001f));
            Assert.That(child.position, Is.EqualTo(new Vector2(1.5f, -2.5f)));
            Assert.That(child.timeScale, Is.EqualTo(-2f).Within(0.0001f), "a dropped timeScale plays the walk backwards");
            Assert.That(child.cycleOffset, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(child.directBlendParameter, Is.EqualTo("VelocityX"));
            Assert.That(child.mirror, Is.True);
        }

        [Test]
        public void NestedSubTreesAreClonedRatherThanShared()
        {
            var nested = Tree("Nested", Child(Clip("Leaf")));
            var source = Tree("Root", Child(Clip("Top")), new ChildMotion { motion = nested, timeScale = 1f });

            var collected = new List<BlendTree>();
            var clone = EasyLocoModularAvatarBuilder.CloneBlendTreeInMemory(
                source, new MotionReplacements(new Dictionary<string, Motion>()), collected);
            Track(collected);

            var clonedNested = clone.children[1].motion as BlendTree;
            Assert.That(clonedNested, Is.Not.Null, "the sub-tree stopped being a BlendTree");
            Assert.That(clonedNested, Is.Not.SameAs(nested),
                "the copy still points at the package's own sub-tree, so edits inside it escape into the package");

            Assert.That(collected, Has.Count.EqualTo(2));
            Assert.That(collected, Does.Contain(clone));
            Assert.That(collected, Does.Contain(clonedNested));
        }

        [Test]
        public void ReplacementsApplyInsideNestedSubTrees()
        {
            var target = Clip("StandIdle");
            var replacement = Clip("UsersOwnIdle");
            var nested = Tree("Nested", Child(target));
            var source = Tree("Root", new ChildMotion { motion = nested, timeScale = 1f });

            var clone = CloneOf(source, new Dictionary<string, Motion> { { "StandIdle", replacement } });

            var clonedNested = (BlendTree)clone.children[0].motion;
            Assert.That(clonedNested.children[0].motion, Is.SameAs(replacement));
            Assert.That(nested.children[0].motion, Is.SameAs(target), "the nested source asset was rewritten");
        }

        [Test]
        public void UnmatchedAndMissingChildMotionsAreLeftAlone()
        {
            var stranger = Clip("NotInTheMap");
            var source = Tree("Source", Child(stranger), new ChildMotion { motion = null, timeScale = 1f });

            var clone = CloneOf(source, new Dictionary<string, Motion> { { "StandIdle", Clip("Unused") } });

            Assert.That(clone.children[0].motion, Is.SameAs(stranger), "an unmatched child should keep its own motion");
            Assert.That(clone.children[1].motion, Is.Null);
        }

        [Test]
        public void AnEmptyTreeClonesWithoutChildren()
        {
            var clone = CloneOf(Tree("Empty"), new Dictionary<string, Motion>());

            Assert.That(clone.children, Is.Empty);
        }

        private BlendTree CloneOf(BlendTree source, IReadOnlyDictionary<string, Motion> replacements)
        {
            var collected = new List<BlendTree>();
            var clone = EasyLocoModularAvatarBuilder.CloneBlendTreeInMemory(
                source, new MotionReplacements(replacements), collected);
            Track(collected);
            return clone;
        }

        private void Track(IEnumerable<BlendTree> trees)
        {
            spawned.AddRange(trees.Cast<Object>());
        }

        private AnimationClip Clip(string name)
        {
            var clip = new AnimationClip { name = name };
            spawned.Add(clip);
            return clip;
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

        private static bool NormalizedBlendValues(BlendTree tree)
        {
            return new SerializedObject(tree).FindProperty("m_NormalizedBlendValues").boolValue;
        }

        private static void SetNormalizedBlendValues(BlendTree tree, bool value)
        {
            var serialized = new SerializedObject(tree);
            serialized.FindProperty("m_NormalizedBlendValues").boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
