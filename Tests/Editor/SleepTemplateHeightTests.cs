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
    /// Height lift is nested 1D trees (Height × EyeHeightNorm). EnableHeight is a sibling-state gate,
    /// not a blend parameter — a bool cannot drive a 1D tree.
    /// </summary>
    public class SleepTemplateHeightTests
    {
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
            Assert.That(byName.ContainsKey(EasyLocoConst.AdjustHeightParam), Is.True,
                "EL/AdjustHeight must be declared so PoseSpace can see the radial is open");
            Assert.That(byName[EasyLocoConst.AdjustHeightParam].type,
                Is.EqualTo(AnimatorControllerParameterType.Bool));
            Assert.That(byName.ContainsKey("EL/AdjustingHeight"), Is.False,
                "EL/AdjustingHeight is a leftover typo - use EL/AdjustHeight");
            Assert.That(byName.ContainsKey("IsLocal"), Is.True,
                "IsLocal must be declared so PoseSpace can loop locally");
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
        public void PoseSpaceLayerEntersWhileAdjustingHeight()
        {
            var controller = LoadController();
            var layer = controller.layers.FirstOrDefault(item => item.name == EasyLocoConst.PoseSpaceLayer);
            Assert.That(layer, Is.Not.Null, "PoseSpace layer missing from the sleep template");
            Assert.That(layer.defaultWeight, Is.EqualTo(1f));

            var byName = layer.stateMachine.states
                .Select(child => child.state)
                .Where(state => state != null)
                .ToDictionary(state => state.name);
            Assert.That(byName.Keys, Is.EquivalentTo(new[]
            {
                EasyLocoConst.PoseSpaceIdleState,
                EasyLocoConst.PoseSpaceSleepIdleState,
                EasyLocoConst.PoseSpaceEnableHeightState,
                EasyLocoConst.PoseSpaceState,
                EasyLocoConst.PoseSpaceRepeatState,
            }));
            Assert.That(layer.stateMachine.defaultState.name, Is.EqualTo(EasyLocoConst.PoseSpaceIdleState));

            var idle = byName[EasyLocoConst.PoseSpaceIdleState];
            var sleepIdle = byName[EasyLocoConst.PoseSpaceSleepIdleState];
            var enableHeight = byName[EasyLocoConst.PoseSpaceEnableHeightState];
            var poseSpace = byName[EasyLocoConst.PoseSpaceState];
            var repeat = byName[EasyLocoConst.PoseSpaceRepeatState];

            Assert.That(idle.transitions.Any(transition =>
                    transition.destinationState == sleepIdle
                    && transition.conditions.Any(condition =>
                        condition.parameter == EasyLocoConst.SleepModeParam && condition.mode == AnimatorConditionMode.If)
                    && transition.conditions.Any(condition =>
                        condition.parameter == "Upright" && condition.mode == AnimatorConditionMode.Less)),
                Is.True,
                "Idle must enter SleepModeIdle when SleepMode is on and Upright is low");

            Assert.That(sleepIdle.transitions.Any(transition =>
                    transition.destinationState == enableHeight
                    && transition.conditions.Any(condition =>
                        condition.parameter == EasyLocoConst.EnableHeightParam && condition.mode == AnimatorConditionMode.If)),
                Is.True,
                "SleepModeIdle must enter EnableHeight when EnableHeight is on");
            Assert.That(sleepIdle.transitions.Where(transition =>
                    transition.destinationState == idle
                    && transition.conditions.Any(condition =>
                        condition.parameter == "Upright" && condition.mode == AnimatorConditionMode.Greater))
                .All(transition => transition.conditions.Any(condition =>
                    condition.parameter == EasyLocoConst.EnableHeightParam && condition.mode == AnimatorConditionMode.IfNot)),
                Is.True,
                "SleepModeIdle must not return to Idle on Upright while EnableHeight is on");

            Assert.That(enableHeight.transitions.Any(transition =>
                    transition.destinationState == poseSpace
                    && transition.conditions.Any(condition =>
                        condition.parameter == EasyLocoConst.AdjustHeightParam && condition.mode == AnimatorConditionMode.If)),
                Is.True,
                "EnableHeight must enter PoseSpace when AdjustHeight is on");
            Assert.That(enableHeight.transitions.Any(transition =>
                    transition.destinationState == sleepIdle
                    && transition.conditions.Any(condition =>
                        condition.parameter == EasyLocoConst.EnableHeightParam && condition.mode == AnimatorConditionMode.IfNot)),
                Is.True,
                "EnableHeight must return to SleepModeIdle when EnableHeight turns off");

            Assert.That(poseSpace.transitions.Any(transition =>
                    transition.destinationState == enableHeight
                    && transition.conditions.Any(condition =>
                        condition.parameter == EasyLocoConst.AdjustHeightParam && condition.mode == AnimatorConditionMode.IfNot)),
                Is.True,
                "PoseSpace must return to EnableHeight when AdjustHeight turns off");
            Assert.That(poseSpace.transitions.Any(transition =>
                    transition.destinationState == repeat
                    && transition.conditions.Any(condition =>
                        condition.parameter == "IsLocal" && condition.mode == AnimatorConditionMode.If)),
                Is.True,
                "PoseSpace must loop locally through PoseSpaceRepeat");
            Assert.That(repeat.transitions.Any(transition =>
                    transition.destinationState == poseSpace
                    && transition.conditions.Any(condition =>
                        condition.parameter == "IsLocal" && condition.mode == AnimatorConditionMode.If)),
                Is.True);

            Assert.That(idle.behaviours, Is.Not.Empty, "Idle must exit VRC pose space");
            Assert.That(sleepIdle.behaviours, Is.Not.Empty, "SleepModeIdle must enter VRC pose space");
            Assert.That(enableHeight.behaviours, Is.Not.Empty, "EnableHeight must stay in VRC pose space");
            Assert.That(poseSpace.behaviours, Is.Not.Empty, "PoseSpace must enter VRC pose space");
            Assert.That(repeat.behaviours, Is.Not.Empty, "PoseSpaceRepeat must re-enter VRC pose space");
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

        [Test]
        public void UprightWakeDoesNotFireWhileEnableHeightIsOn()
        {
            const float wakeThreshold = 0.43f;
            var wakes = CollectConditionSets()
                .Where(conditions => conditions.Any(condition =>
                    condition.parameter == "Upright"
                    && condition.mode == AnimatorConditionMode.Greater
                    && Mathf.Approximately(condition.threshold, wakeThreshold)))
                .ToList();

            Assert.That(wakes, Is.Not.Empty, "expected Upright > 0.43 wake / unlock transitions");
            foreach (var conditions in wakes)
            {
                Assert.That(conditions.Any(condition =>
                        condition.parameter == EasyLocoConst.EnableHeightParam
                        && condition.mode == AnimatorConditionMode.IfNot),
                    Is.True,
                    "Upright > 0.43 must also require EnableHeight off");
            }
        }

        [Test]
        public void FeetLockCanSwitchWhileSleepingWithEnableHeightOn()
        {
            var sleeping = FindStateMachine("Sleeping");
            Assert.That(sleeping, Is.Not.Null, "Sleeping state machine missing");

            var feetLock = sleeping.stateMachines
                .Select(child => child.stateMachine)
                .FirstOrDefault(machine => machine != null && machine.name == "Sleeping FeetLock");
            var feetUnlock = sleeping.stateMachines
                .Select(child => child.stateMachine)
                .FirstOrDefault(machine => machine != null && machine.name == "Sleeping FeetUnlock");
            Assert.That(feetLock, Is.Not.Null);
            Assert.That(feetUnlock, Is.Not.Null);

            Assert.That(
                HasStateMachineRoute(sleeping, feetUnlock, feetLock, lockOn: true),
                Is.True,
                "FeetUnlock must re-enter FeetLock without requiring Upright");
            Assert.That(
                HasStateMachineRoute(sleeping, feetLock, feetUnlock, lockOn: false),
                Is.True,
                "FeetLock must re-enter FeetUnlock without requiring Upright");
        }

        [Test]
        public void EnableHeightOnEntryLandsOnTheHeightSibling()
        {
            foreach (var machineName in new[] { "Sleeping FeetLock", "Sleeping FeetUnlock" })
            {
                var machine = FindStateMachine(machineName);
                Assert.That(machine, Is.Not.Null, machineName + " missing");

                var entries = machine.entryTransitions;
                Assert.That(
                    HasEntry(entries, "Sleeping Up Height", enableHeight: true, facingDown: false),
                    Is.True,
                    machineName + " must enter Sleeping Up Height when EnableHeight is on");
                Assert.That(
                    HasEntry(entries, "Sleeping Down Height", enableHeight: true, facingDown: true),
                    Is.True,
                    machineName + " must enter Sleeping Down Height when EnableHeight is on and facing down");
                Assert.That(
                    HasEntry(entries, "Sleeping Down", enableHeight: false, facingDown: true),
                    Is.True,
                    machineName + " must still enter Sleeping Down when EnableHeight is off and facing down");
            }
        }

        [Test]
        public void TrackingCanLockFeetWhileEnableHeightIsOn()
        {
            var feetLock = FindStateMachine("FeetLock");
            Assert.That(feetLock, Is.Not.Null);
            var tracking = feetLock.states.Select(child => child.state).First(state => state.name == "Tracking");
            var locked = feetLock.states.Select(child => child.state).First(state => state.name == "Locked");

            var liftLock = tracking.transitions.FirstOrDefault(transition =>
                transition.destinationState == locked
                && transition.conditions.Any(condition =>
                    condition.parameter == EasyLocoConst.EnableHeightParam && condition.mode == AnimatorConditionMode.If));
            Assert.That(liftLock, Is.Not.Null, "Tracking must enter Locked while EnableHeight is on");
            Assert.That(liftLock.conditions.Any(condition =>
                    condition.parameter == EasyLocoConst.FeetLockParam && condition.mode == AnimatorConditionMode.If),
                Is.True);
            Assert.That(liftLock.conditions.Any(condition =>
                    condition.parameter == EasyLocoConst.SleepModeParam && condition.mode == AnimatorConditionMode.If),
                Is.True);
            Assert.That(liftLock.conditions.All(condition => condition.parameter != "Upright"), Is.True,
                "the lift lock path must not require Upright < 0.43");
            Assert.That(liftLock.conditions.Where(condition => condition.parameter == EasyLocoConst.EnableHeightParam),
                Has.All.Matches<AnimatorCondition>(condition => Mathf.Approximately(condition.threshold, 0f)),
                "EnableHeight is a bool - leave the threshold at 0, not 0.43");
        }

        private static bool HasEntry(
            AnimatorTransition[] entries,
            string destination,
            bool enableHeight,
            bool facingDown)
        {
            var expectedEnable = enableHeight ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
            return entries.Any(entry =>
                entry.destinationState != null
                && entry.destinationState.name == destination
                && entry.conditions.Any(condition =>
                    condition.parameter == EasyLocoConst.EnableHeightParam && condition.mode == expectedEnable)
                && (!facingDown || (
                    entry.conditions.Any(condition => condition.parameter == "EL/FacingUp" && condition.mode == AnimatorConditionMode.Less)
                    && entry.conditions.Any(condition => condition.parameter == "EL/FacingDown" && condition.mode == AnimatorConditionMode.Greater))));
        }

        private static bool HasStateMachineRoute(
            AnimatorStateMachine parent,
            AnimatorStateMachine from,
            AnimatorStateMachine to,
            bool lockOn)
        {
            var expectedFeet = lockOn ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
            return parent.GetStateMachineTransitions(from).Any(transition =>
                transition.destinationStateMachine == to
                && transition.conditions.Any(condition =>
                    condition.parameter == EasyLocoConst.FeetLockParam && condition.mode == expectedFeet)
                && transition.conditions.All(condition => condition.parameter != "Upright"));
        }

        private static AnimatorStateMachine FindStateMachine(string name)
        {
            foreach (var layer in LoadController().layers)
            {
                var found = FindStateMachine(layer.stateMachine, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static AnimatorStateMachine FindStateMachine(AnimatorStateMachine stateMachine, string name)
        {
            if (stateMachine == null)
            {
                return null;
            }

            if (stateMachine.name == name)
            {
                return stateMachine;
            }

            foreach (var child in stateMachine.stateMachines)
            {
                var found = FindStateMachine(child.stateMachine, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
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

        private static List<AnimatorCondition[]> CollectConditionSets()
        {
            var sets = new List<AnimatorCondition[]>();
            foreach (var layer in LoadController().layers)
            {
                CollectConditionSets(layer.stateMachine, sets);
            }

            return sets;
        }

        private static void CollectConditionSets(AnimatorStateMachine stateMachine, List<AnimatorCondition[]> into)
        {
            foreach (var child in stateMachine.states)
            {
                if (child.state == null)
                {
                    continue;
                }

                foreach (var transition in child.state.transitions)
                {
                    into.Add(transition.conditions);
                }
            }

            foreach (var child in stateMachine.stateMachines)
            {
                if (child.stateMachine == null)
                {
                    continue;
                }

                foreach (var transition in stateMachine.GetStateMachineTransitions(child.stateMachine))
                {
                    into.Add(transition.conditions);
                }

                CollectConditionSets(child.stateMachine, into);
            }
        }

        private static AnimatorController LoadController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(EasyLocoConst.SleepTemplatePath);
            Assume.That(controller, Is.Not.Null, $"Sleep template not found at {EasyLocoConst.SleepTemplatePath}");
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
