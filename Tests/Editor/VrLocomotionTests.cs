using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// The base template branches locomotion on VRMode into two sub-state machines. Each branch
    /// carries its own Standing, Crouching and Prone states so that desktop idle-pose overrides do
    /// not bleed into VR locomotion (in VR the stance is what IK blends tracked head and hands
    /// against, so a replaced pose fights the solver and produces broken proportions).
    ///
    /// The builder scopes replacement to DesktopLocomotionStateMachine, but the split was first
    /// authored by sharing the same BlendTree asset between both branches. That meant any
    /// modification to the desktop tree (e.g. the builder injecting a pose selector) silently
    /// rewrote the VR branch too. These tests pin the separation: the VR branch must keep its own
    /// locomotion trees, and scoped replacement must leave them untouched.
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
        public void VrStancesUseDedicatedVrLocomotionTrees()
        {
            var desktop = LocomotionTemplate.StanceStates(EasyLocoConst.DesktopLocomotionStateMachine);
            var vr = LocomotionTemplate.StanceStates(EasyLocoConst.VrLocomotionStateMachine);

            foreach (var stance in LocomotionTemplate.Stances)
            {
                var vrTree = vr[stance].motion as BlendTree;
                Assert.That(vrTree, Is.Not.Null, $"VR {stance} must play a BlendTree");
                Assert.That(vrTree.name, Does.StartWith("DefaultVR"),
                    $"VR {stance} should use a dedicated VR locomotion tree, found {vrTree.name}");

                var desktopTree = desktop[stance].motion as BlendTree;
                Assert.That(vrTree, Is.Not.SameAs(desktopTree),
                    $"VR {stance} must not share the same BlendTree asset as desktop");
            }
        }

        [Test]
        public void VrIdleUsesBuiltInClip()
        {
            var idleTargetByStance = new Dictionary<string, string>
            {
                { "Standing", EasyLocoConst.StandIdleTarget },
                { "Crouching", EasyLocoConst.CrouchIdleTarget },
                { "Prone", EasyLocoConst.ProneIdleTarget },
            };

            var vr = LocomotionTemplate.StanceStates(EasyLocoConst.VrLocomotionStateMachine);
            foreach (var stance in LocomotionTemplate.Stances)
            {
                var tree = vr[stance].motion as BlendTree;
                var idleChild = tree.children.OrderBy(c => c.threshold).FirstOrDefault();
                Assert.That(idleChild.motion?.name, Is.EqualTo(idleTargetByStance[stance]),
                    $"VR {stance} idle should remain the built-in {idleTargetByStance[stance]} clip");
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
