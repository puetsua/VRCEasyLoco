using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// The base template branches locomotion on VRMode, and both branches carry a Standing,
    /// Crouching and Prone state playing motions of the same names. Two things went wrong the first
    /// time that split was authored, and both are pinned here.
    ///
    /// The VR states were pointed straight at the idle clips instead of the locomotion trees those
    /// clips sit inside, so a VR player had a stance pose and no walk animation at all. And because
    /// the builder replaces motions by name, the idle-pose menu rewrote the VR stance too - the one
    /// branch that must keep the built-in poses, since in VR the stance is what IK blends the
    /// tracked head and hands against.
    /// </summary>
    public class VrLocomotionTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            // Sub-state machines and states are reachable from the controller, so an earlier
            // DestroyImmediate can have taken one out already - hence the null check.
            foreach (var spawn in spawned.Where(spawn => spawn != null))
            {
                Object.DestroyImmediate(spawn);
            }
            spawned.Clear();
        }

        [Test]
        public void BothBranchesExistUnderTheLocomotionLayer()
        {
            Assert.That(LocomotionTemplate.StanceStates(EasyLocoConst.DesktopLocomotionStateMachine).Keys,
                Is.EquivalentTo(LocomotionTemplate.Stances),
                $"the builder scopes idle replacement to \"{EasyLocoConst.DesktopLocomotionStateMachine}\" - "
                + "renaming that state machine makes every build throw");
            Assert.That(LocomotionTemplate.StanceStates(EasyLocoConst.VrLocomotionStateMachine).Keys,
                Is.EquivalentTo(LocomotionTemplate.Stances));
        }

        [Test]
        public void VrStancesPlayTheSameLocomotionTreesAsDesktop()
        {
            var desktop = LocomotionTemplate.StanceStates(EasyLocoConst.DesktopLocomotionStateMachine);
            var vr = LocomotionTemplate.StanceStates(EasyLocoConst.VrLocomotionStateMachine);

            foreach (var stance in LocomotionTemplate.Stances)
            {
                Assert.That(vr[stance].motion, Is.SameAs(desktop[stance].motion),
                    $"VR {stance} should play the same locomotion tree as desktop. Pointing it at the "
                    + "bare idle clip leaves a VR player standing still while walking.");
            }
        }

        [Test]
        public void VrStancesKeepTheirWalkMotions()
        {
            foreach (var state in LocomotionTemplate.StanceStates(EasyLocoConst.VrLocomotionStateMachine).Values)
            {
                var tree = state.motion as BlendTree;
                Assert.That(tree, Is.Not.Null, $"VR {state.name} plays {state.motion} - a single clip has no walk in it");
                Assert.That(tree.children.Length, Is.GreaterThan(1),
                    $"VR {state.name} has only the idle child left, so walking would play nothing");
            }
        }

        [Test]
        public void ReplacementSkipsBranchesOutsideTheScope()
        {
            var idle = Clip(EasyLocoConst.StandIdleTarget);
            var replacement = Clip("UsersOwnIdle");
            var controller = TwoBranchController(idle, out var desktopState, out var vrState);

            EasyLocoModularAvatarBuilder.ReplaceMotions(controller,
                Replacements(EasyLocoConst.StandIdleTarget, replacement),
                "Assets", EasyLocoConst.DesktopLocomotionStateMachine);

            Assert.That(desktopState.motion, Is.SameAs(replacement), "the desktop stance should take the user's pose");
            Assert.That(vrState.motion, Is.SameAs(idle),
                "the VR stance was rewritten - the idle menu would move the pose IK blends against");
        }

        [Test]
        public void ReplacementWithoutAScopeCoversEveryBranch()
        {
            var idle = Clip(EasyLocoConst.StandIdleTarget);
            var replacement = Clip("UsersOwnIdle");
            var controller = TwoBranchController(idle, out var desktopState, out var vrState);

            EasyLocoModularAvatarBuilder.ReplaceMotions(controller,
                Replacements(EasyLocoConst.StandIdleTarget, replacement),
                "Assets", null);

            Assert.That(desktopState.motion, Is.SameAs(replacement));
            Assert.That(vrState.motion, Is.SameAs(replacement),
                "the unscoped path is what the Action and Sleep controllers use - it must still walk everything");
        }

        [Test]
        public void AScopeThatMatchesNothingThrows()
        {
            var idle = Clip(EasyLocoConst.StandIdleTarget);
            var controller = TwoBranchController(idle, out _, out var vrState);

            Assert.That(() => EasyLocoModularAvatarBuilder.ReplaceMotions(controller,
                    Replacements(EasyLocoConst.StandIdleTarget, Clip("UsersOwnIdle")),
                    "Assets", "Renamed Locomotion"),
                Throws.InvalidOperationException,
                "a scope nobody matches must fail the build, not quietly replace everywhere");
            Assert.That(vrState.motion, Is.SameAs(idle));
        }

        // A stand-in for the template's shape: one Locomotion layer holding a desktop and a VR
        // branch whose stances play the very same motion.
        private AnimatorController TwoBranchController(Motion idle, out AnimatorState desktopState, out AnimatorState vrState)
        {
            var controller = new AnimatorController();
            spawned.Add(controller);
            controller.AddLayer("Locomotion");

            var root = controller.layers[0].stateMachine;
            spawned.Add(root);

            desktopState = AddStance(root, EasyLocoConst.DesktopLocomotionStateMachine, idle);
            vrState = AddStance(root, EasyLocoConst.VrLocomotionStateMachine, idle);
            return controller;
        }

        private AnimatorState AddStance(AnimatorStateMachine root, string branchName, Motion idle)
        {
            var branch = root.AddStateMachine(branchName);
            spawned.Add(branch);

            var state = branch.AddState("Standing");
            spawned.Add(state);
            state.motion = idle;
            return state;
        }

        private static MotionReplacements Replacements(string name, Motion replacement)
        {
            return new MotionReplacements(new Dictionary<string, Motion> { { name, replacement } });
        }

        private AnimationClip Clip(string name)
        {
            var clip = new AnimationClip { name = name };
            spawned.Add(clip);
            return clip;
        }
    }
}
