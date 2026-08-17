using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>VR locomotion trees are separate assets. Desktop idle replacement must not touch them.</summary>
    public class VrLocomotionTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
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
                { "Crouching", EasyLocoConst.VrCrouchIdleTarget },
                { "Prone", EasyLocoConst.ProneIdleTarget },
            };

            var vr = LocomotionTemplate.StanceStates(EasyLocoConst.VrLocomotionStateMachine);
            foreach (var stance in LocomotionTemplate.Stances)
            {
                var tree = vr[stance].motion as BlendTree;
                var idleChild = tree.children.FirstOrDefault(c =>
                    Mathf.Approximately(c.position.x, 0f) && Mathf.Approximately(c.position.y, 0f));
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
