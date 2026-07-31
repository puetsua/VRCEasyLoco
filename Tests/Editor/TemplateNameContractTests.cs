using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// The names in <see cref="EasyLocoConst"/> are a contract with the shipped templates: the build
    /// finds the user's clip a home by matching a motion's name in the locomotion and sleeping
    /// trees, and a state's name in the Action controller. A template edit that renames one of them
    /// breaks that contract, and the build now says so instead of dropping the animation - but only
    /// at build time, in front of whoever pressed the button. These move that failure here, where
    /// renaming a state or a clip in a template fails in the Test Runner before it ships.
    /// </summary>
    public class TemplateNameContractTests
    {
        private const string SleepTemplatePath =
            EasyLocoConst.PackageRoot + "/Animators/EasyLocoSleepTemplate.controller";

        private const string ActionTemplatePath =
            EasyLocoConst.PackageRoot + "/Animators/EasyLocoActionTemplate.controller";

        [Test]
        public void TheDesktopBranchPlaysEveryIdleClipTheBuildReplaces()
        {
            var played = MotionNames(LocomotionTemplate.Branch(EasyLocoConst.DesktopLocomotionStateMachine));

            Assert.That(played, Is.SupersetOf(new[]
            {
                EasyLocoConst.StandIdleTarget,
                EasyLocoConst.CrouchIdleTarget,
                EasyLocoConst.ProneIdleTarget,
            }), "an idle pose set in the inspector is written over the clip of this name");
        }

        [Test]
        public void TheVrBranchPlaysTheSameIdleClips()
        {
            // Not replaced - VR keeps the built-in poses - but the branch is a copy of the desktop
            // one, and a copy that drifted this far apart is worth knowing about.
            var played = MotionNames(LocomotionTemplate.Branch(EasyLocoConst.VrLocomotionStateMachine));

            Assert.That(played, Is.SupersetOf(new[]
            {
                EasyLocoConst.StandIdleTarget,
                EasyLocoConst.CrouchIdleTarget,
                EasyLocoConst.ProneIdleTarget,
            }));
        }

        [Test]
        public void TheSleepTemplatePlaysEverySleepClipTheBuildReplaces()
        {
            var played = MotionNames(SleepTemplatePath);

            var expected = new List<string> { EasyLocoConst.SleepUpTarget, EasyLocoConst.SleepDownTarget };
            expected.AddRange(EasyLocoConst.SleepSideTargets);

            Assert.That(played, Is.SupersetOf(expected),
                "each on-side slot has its own placeholder clip, and the build fills them by name");
        }

        [Test]
        public void TheActionTemplateCarriesEveryAfkState()
        {
            var states = StateNames(ActionTemplatePath);

            var expected = EasyLocoConst.AfkStances
                .SelectMany(stance => EasyLocoConst.AfkStages.Select(stage => EasyLocoConst.AfkStateName(stance, stage)))
                .ToList();

            Assert.That(states, Is.SupersetOf(expected),
                "the AFK clips are written onto the states of these names, one per stance and phase");
        }

        // Leaf motions only, matching what the build's replacement walk can actually match: blend
        // trees are recursed into, never swapped by their own name.
        private static HashSet<string> MotionNames(string controllerPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assume.That(controller, Is.Not.Null, $"Template not found at {controllerPath}");

            var names = new HashSet<string>();
            foreach (var layer in controller.layers)
            {
                CollectMotionNames(layer.stateMachine, names);
            }

            return names;
        }

        private static HashSet<string> MotionNames(AnimatorStateMachine stateMachine)
        {
            var names = new HashSet<string>();
            CollectMotionNames(stateMachine, names);
            return names;
        }

        private static void CollectMotionNames(AnimatorStateMachine stateMachine, HashSet<string> into)
        {
            foreach (var child in stateMachine.states)
            {
                CollectMotionNames(child.state.motion, into);
            }

            foreach (var child in stateMachine.stateMachines)
            {
                CollectMotionNames(child.stateMachine, into);
            }
        }

        private static void CollectMotionNames(Motion motion, HashSet<string> into)
        {
            switch (motion)
            {
                case null:
                    return;
                case BlendTree tree:
                    foreach (var child in tree.children)
                    {
                        CollectMotionNames(child.motion, into);
                    }
                    return;
                default:
                    into.Add(motion.name);
                    return;
            }
        }

        private static HashSet<string> StateNames(string controllerPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assume.That(controller, Is.Not.Null, $"Template not found at {controllerPath}");

            var names = new HashSet<string>();
            foreach (var layer in controller.layers)
            {
                CollectStateNames(layer.stateMachine, names);
            }

            return names;
        }

        private static void CollectStateNames(AnimatorStateMachine stateMachine, HashSet<string> into)
        {
            foreach (var child in stateMachine.states)
            {
                into.Add(child.state.name);
            }

            foreach (var child in stateMachine.stateMachines)
            {
                CollectStateNames(child.stateMachine, into);
            }
        }
    }
}
