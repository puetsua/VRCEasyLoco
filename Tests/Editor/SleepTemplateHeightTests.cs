using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using static VRC.SDKBase.VRC_AvatarParameterDriver;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// Height lift is two nested 1D trees: EL/Height blends the normal pose into an
    /// EL/EyeHeightNorm tree that blends that pose into its 5m variant. Lift is then
    /// Height × EyeHeightNorm (linear on the radial; EyeHeightNorm is the 5 m ceiling). A 2D
    /// Cartesian tree is not linear on either axis. The EyeHeightNorm layer writes
    /// EL/EyeHeightNorm every frame from a 1D blend on EyeHeightAsMeters (0 m → 0, 5 m → 1)
    /// so live scale updates without a Parameter Driver. EL/EnableHeight swaps each sleeping
    /// pose into a sibling Height state - a bool cannot drive a 1D blend.
    /// </summary>
    public class SleepTemplateHeightTests
    {
        private const string SleepTemplatePath =
            EasyLocoConst.PackageRoot + "/Animators/EasyLocoSleepTemplate.controller";

        [Test]
        public void HeightBlendTreesAreNested1D()
        {
            var pairs = CollectHeightStatePairs();
            Assert.That(pairs, Has.Count.EqualTo(4));

            foreach (var pair in pairs)
            {
                var height = pair.On.motion as BlendTree;
                Assert.That(height, Is.Not.Null, pair.On.name + " should play a Height 1D tree");
                Assert.That(height.blendType, Is.EqualTo(BlendTreeType.Simple1D));
                Assert.That(height.blendParameter, Is.EqualTo(EasyLocoConst.HeightParam));
                Assert.That(height.children.Length, Is.EqualTo(2));
                Assert.That(height.children[0].threshold, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(height.children[1].threshold, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(height.children[0].motion, Is.SameAs(pair.Off.motion),
                    "Height=0 must be the same normal pose the ungated state plays");

                var max = height.children[1].motion as BlendTree;
                Assert.That(max, Is.Not.Null, "Height=1 must be the EyeHeightNorm 1D tree");
                Assert.That(max.blendType, Is.EqualTo(BlendTreeType.Simple1D));
                Assert.That(max.blendParameter, Is.EqualTo(EasyLocoConst.EyeHeightNormParam));
                Assert.That(max.children.Length, Is.EqualTo(2));
                Assert.That(max.children[0].motion, Is.SameAs(pair.Off.motion),
                    "EyeHeightNorm=0 must still be the normal pose");
                Assert.That(max.children[1].motion, Is.Not.SameAs(pair.Off.motion),
                    "EyeHeightNorm=1 must be the 5m variant");
            }

            Assert.That(CollectBlendTrees().Any(tree =>
                    (tree.blendParameter == EasyLocoConst.HeightParam || tree.blendParameterY == EasyLocoConst.EyeHeightNormParam)
                    && tree.blendType != BlendTreeType.Simple1D
                    && tree.blendType != BlendTreeType.Direct),
                Is.False,
                "no leftover 2D Height × EyeHeightNorm tree should remain");
        }

        [Test]
        public void SleepTemplateHasNoEyeHeightAsPercent()
        {
            var controller = LoadController();

            Assert.That(controller.parameters.Select(parameter => parameter.name),
                Does.Not.Contain("EyeHeightAsPercent"),
                "EyeHeightAsPercent was left on the sleep template after the remap");

            foreach (var tree in CollectBlendTrees())
            {
                Assert.That(tree.blendParameter, Is.Not.EqualTo("EyeHeightAsPercent"));
                Assert.That(tree.blendParameterY, Is.Not.EqualTo("EyeHeightAsPercent"));
            }
        }

        [Test]
        public void SleepTemplateDeclaresTheRemapParameters()
        {
            var controller = LoadController();
            var byName = controller.parameters.ToDictionary(parameter => parameter.name);

            Assert.That(byName.ContainsKey(EasyLocoConst.EyeHeightAsMetersParam), Is.True,
                "EyeHeightAsMeters must be declared so the Normalize 1D tree can read it");
            Assert.That(byName[EasyLocoConst.EyeHeightAsMetersParam].type,
                Is.EqualTo(AnimatorControllerParameterType.Float));

            Assert.That(byName.ContainsKey(EasyLocoConst.EyeHeightNormParam), Is.True,
                "EL/EyeHeightNorm must be declared so the Height trees can blend on it");
            Assert.That(byName[EasyLocoConst.EyeHeightNormParam].type,
                Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(byName[EasyLocoConst.EyeHeightNormParam].defaultFloat,
                Is.EqualTo(EasyLocoConst.EyeHeightNormDefault).Within(0.0001f),
                "the default should match a typical ~1.25 m avatar until Normalize writes the live value");

            Assert.That(byName.ContainsKey("EL/HeightApplied"), Is.False,
                "do not introduce EL/HeightApplied - EnableHeight gates via sibling states");
            Assert.That(byName[EasyLocoConst.HeightParam].defaultFloat, Is.EqualTo(0f),
                "EL/Height defaults to 0 so a fresh install sits on the normal pose");
            Assert.That(byName[EasyLocoConst.EnableHeightParam].defaultBool, Is.False);
        }

        [Test]
        public void NormalizeLayerWritesEyeHeightNormFromMeters()
        {
            var controller = LoadController();
            var layer = controller.layers.FirstOrDefault(item => item.name == EasyLocoConst.EyeHeightNormLayer);
            Assert.That(layer, Is.Not.Null, "EyeHeightNorm layer missing from the sleep template");

            var state = layer.stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(item => item != null && item.name == EasyLocoConst.EyeHeightNormState);
            Assert.That(state, Is.Not.Null, "Normalize state missing from the EyeHeightNorm layer");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(state),
                "Normalize must be the layer default so the 1D writer stays active");
            Assert.That(layer.stateMachine.states.Select(child => child.state.name),
                Is.EquivalentTo(new[] { EasyLocoConst.EyeHeightNormState }));
            Assert.That(state.behaviours.OfType<VRCAvatarParameterDriver>(), Is.Empty,
                "Normalize should write EL/EyeHeightNorm from a clip, not a Parameter Driver");

            var tree = state.motion as BlendTree;
            Assert.That(tree, Is.Not.Null);
            Assert.That(tree.blendType, Is.EqualTo(BlendTreeType.Simple1D));
            Assert.That(tree.blendParameter, Is.EqualTo(EasyLocoConst.EyeHeightAsMetersParam));
            Assert.That(tree.children.Length, Is.EqualTo(2));
            Assert.That(tree.children[0].threshold, Is.EqualTo(EasyLocoConst.EyeHeightMetersMin).Within(0.0001f));
            Assert.That(tree.children[1].threshold, Is.EqualTo(EasyLocoConst.EyeHeightMetersMax).Within(0.0001f));
            Assert.That(AssetDatabase.GetAssetPath(tree.children[0].motion),
                Is.EqualTo(EasyLocoConst.EyeHeightNormZeroClip));
            Assert.That(AssetDatabase.GetAssetPath(tree.children[1].motion),
                Is.EqualTo(EasyLocoConst.EyeHeightNormOneClip));
        }

        [Test]
        public void EnableHeightGatesTheHeightRadial()
        {
            var pairs = CollectHeightStatePairs();
            Assert.That(pairs, Has.Count.EqualTo(4),
                "each of the four sleeping poses needs a sibling Height state");

            foreach (var pair in pairs)
            {
                var offTree = pair.Off.motion as BlendTree;
                var onTree = pair.On.motion as BlendTree;
                Assert.That(offTree, Is.Not.Null, pair.Off.name + " should still play the normal sleep tree");
                Assert.That(onTree, Is.Not.Null, pair.On.name + " should play the nested Height 1D blend");
                Assert.That(onTree.blendType, Is.EqualTo(BlendTreeType.Simple1D));
                Assert.That(onTree.blendParameter, Is.EqualTo(EasyLocoConst.HeightParam));
                Assert.That(offTree.blendParameter, Is.Not.EqualTo(EasyLocoConst.EnableHeightParam),
                    "a bool cannot drive a 1D blend - EnableHeight must be a state transition");

                Assert.That(HasCondition(pair.Off, pair.On, AnimatorConditionMode.If, EasyLocoConst.EnableHeightParam), Is.True,
                    pair.Off.name + " must enter " + pair.On.name + " when EnableHeight turns on");
                Assert.That(HasCondition(pair.On, pair.Off, AnimatorConditionMode.IfNot, EasyLocoConst.EnableHeightParam), Is.True,
                    pair.On.name + " must return to " + pair.Off.name + " when EnableHeight turns off");
            }

            Assert.That(CollectBlendTrees().Any(tree => tree.blendParameter == EasyLocoConst.EnableHeightParam),
                Is.False,
                "no leftover 1D EnableHeight wrapper should remain");
        }

        private static bool HasCondition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, string parameter)
        {
            return from.transitions.Any(transition =>
                transition.destinationState == to
                && transition.conditions.Any(condition => condition.mode == mode && condition.parameter == parameter));
        }

        private struct HeightStatePair
        {
            public AnimatorState Off;
            public AnimatorState On;
        }

        private static List<HeightStatePair> CollectHeightStatePairs()
        {
            var pairs = new List<HeightStatePair>();
            foreach (var layer in LoadController().layers)
            {
                CollectHeightStatePairs(layer.stateMachine, pairs);
            }

            return pairs;
        }

        private static void CollectHeightStatePairs(AnimatorStateMachine stateMachine, List<HeightStatePair> into)
        {
            var byName = stateMachine.states
                .Select(child => child.state)
                .Where(state => state != null)
                .ToDictionary(state => state.name);

            foreach (var off in byName.Values)
            {
                var heightName = off.name + EasyLocoConst.HeightStateSuffix;
                if (off.name.EndsWith(EasyLocoConst.HeightStateSuffix) || !byName.TryGetValue(heightName, out var on))
                {
                    continue;
                }

                into.Add(new HeightStatePair { Off = off, On = on });
            }

            foreach (var child in stateMachine.stateMachines)
            {
                if (child.stateMachine != null)
                {
                    CollectHeightStatePairs(child.stateMachine, into);
                }
            }
        }

        private static AnimatorController LoadController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(SleepTemplatePath);
            Assume.That(controller, Is.Not.Null, $"Sleep template not found at {SleepTemplatePath}");
            return controller;
        }

        private static List<BlendTree> CollectBlendTrees()
        {
            var trees = new List<BlendTree>();
            var seen = new HashSet<BlendTree>();
            foreach (var layer in LoadController().layers)
            {
                CollectBlendTrees(layer.stateMachine, trees, seen);
            }

            return trees;
        }

        private static void CollectBlendTrees(AnimatorStateMachine stateMachine, List<BlendTree> into, HashSet<BlendTree> seen)
        {
            foreach (var child in stateMachine.states)
            {
                CollectBlendTrees(child.state != null ? child.state.motion : null, into, seen);
            }

            foreach (var child in stateMachine.stateMachines)
            {
                if (child.stateMachine != null)
                {
                    CollectBlendTrees(child.stateMachine, into, seen);
                }
            }
        }

        private static void CollectBlendTrees(Motion motion, List<BlendTree> into, HashSet<BlendTree> seen)
        {
            if (!(motion is BlendTree tree) || !seen.Add(tree))
            {
                return;
            }

            into.Add(tree);
            foreach (var child in tree.children)
            {
                CollectBlendTrees(child.motion, into, seen);
            }
        }
    }
}
