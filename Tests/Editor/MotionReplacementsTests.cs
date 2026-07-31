using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// Every key a build hands to a template walk is a claim that the template carries that motion
    /// or that state. Nothing used to check the claim: rename a clip inside a Default* tree, or an
    /// AFK state, and the key matched nothing at all - the build wrote a controller that looked
    /// right and left the user's animation out of it. These pin the ledger that now fails the build
    /// instead, and the two lookups it draws the line between: taking a replacement counts as
    /// finding one, looking at a subtree to decide whether to clone it does not.
    /// </summary>
    public class MotionReplacementsTests
    {
        // Only the shared-tree case needs the disk: a blend tree has to be an asset of its own for
        // the builder to treat it as shared and clone it, and the clone is written next to it.
        private const string TempFolderName = "EasyLocoReplacementTests";
        private const string TempFolder = "Assets/" + TempFolderName;

        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var spawn in spawned.Where(spawn => spawn != null))
            {
                Object.DestroyImmediate(spawn);
            }
            spawned.Clear();

            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        [Test]
        public void TakingAReplacementCountsAsFindingIt()
        {
            var replacements = Replacements(("StandIdle", Clip("UsersOwnIdle")));

            Assert.That(replacements.TryGet("StandIdle", out _), Is.True);
            Assert.That(replacements.Unmatched(), Is.Empty);
        }

        [Test]
        public void LookingWithoutTakingDoesNotCount()
        {
            var replacements = Replacements(("StandIdle", Clip("UsersOwnIdle")));

            Assert.That(replacements.Contains("StandIdle"), Is.True);
            Assert.That(replacements.Unmatched(), Is.EqualTo(new[] { "StandIdle" }),
                "Contains only decides whether a subtree is worth cloning - counting it would let a "
                + "tree that was looked at and skipped stand in for a swap that never happened");
        }

        [Test]
        public void UnmatchedNamesComeBackSorted()
        {
            var replacements = Replacements(
                ("ProneIdle", Clip("A")), ("CrouchIdle", Clip("B")), ("StandIdle", Clip("C")));

            Assert.That(replacements.Unmatched(), Is.EqualTo(new[] { "CrouchIdle", "ProneIdle", "StandIdle" }),
                "the build's message should read the same twice over");
        }

        [Test]
        public void AMotionNameNothingPlaysFailsTheBuild()
        {
            var controller = ControllerWith("Standing", Clip("IdleStandDefault"), out _);

            var thrown = Assert.Throws<System.InvalidOperationException>(() =>
                EasyLocoModularAvatarBuilder.ReplaceMotions(controller,
                    Replacements(("IdleStandRenamed", Clip("UsersOwnIdle"))), "Assets", null));

            Assert.That(thrown.Message, Does.Contain("IdleStandRenamed"),
                "the message has to name the key, or there is nothing to go and look for");
        }

        [Test]
        public void AMotionFoundOnlyOutsideTheScopeFailsTheBuild()
        {
            // The nastiest shape of the two bugs combined: the clip is in the controller, just not
            // in the branch the build is allowed to write to. Walking it would be wrong and doing
            // nothing would be silent, so the build stops.
            var idle = Clip("IdleStandDefault");
            var controller = new AnimatorController();
            spawned.Add(controller);
            controller.AddLayer("Locomotion");
            var root = controller.layers[0].stateMachine;
            spawned.Add(root);

            AddStance(root, EasyLocoConst.DesktopLocomotionStateMachine, Clip("SomethingElse"));
            var vrState = AddStance(root, EasyLocoConst.VrLocomotionStateMachine, idle);

            Assert.Throws<System.InvalidOperationException>(() =>
                EasyLocoModularAvatarBuilder.ReplaceMotions(controller,
                    Replacements(("IdleStandDefault", Clip("UsersOwnIdle"))),
                    "Assets", EasyLocoConst.DesktopLocomotionStateMachine));
            Assert.That(vrState.motion, Is.SameAs(idle), "the out-of-scope branch is still not written to");
        }

        [Test]
        public void NothingToReplaceIsNotAFailure()
        {
            var controller = ControllerWith("Standing", Clip("IdleStandDefault"), out var state);

            Assert.DoesNotThrow(() => EasyLocoModularAvatarBuilder.ReplaceMotions(controller,
                new MotionReplacements(new Dictionary<string, Motion>()), "Assets", null));
            Assert.DoesNotThrow(() => EasyLocoModularAvatarBuilder.ApplyStateMotionOverrides(controller,
                new MotionReplacements(new Dictionary<string, Motion>())));
            Assert.That(state.motion, Is.Not.Null);
        }

        [Test]
        public void AnAfkStateNameNothingCarriesFailsTheBuild()
        {
            var controller = ControllerWith("Afk Stand Looping", Clip("AfkLoopingDefault"), out _);

            var thrown = Assert.Throws<System.InvalidOperationException>(() =>
                EasyLocoModularAvatarBuilder.ApplyStateMotionOverrides(controller,
                    Replacements(("Afk Stand Snoozing", Clip("UsersOwnAfk")))));

            Assert.That(thrown.Message, Does.Contain("Afk Stand Snoozing"));
        }

        [Test]
        public void AnAfkStateThatMatchesTakesTheClip()
        {
            var controller = ControllerWith("Afk Stand Looping", Clip("AfkLoopingDefault"), out var state);
            var users = Clip("UsersOwnAfk");

            EasyLocoModularAvatarBuilder.ApplyStateMotionOverrides(controller,
                Replacements(("Afk Stand Looping", users)));

            Assert.That(state.motion, Is.SameAs(users));
        }

        [Test]
        public void AStateAlreadyPlayingTheUsersClipStillCounts()
        {
            // Someone can point an AFK slot at the very clip the template already plays. Nothing is
            // written, but the name was there - reading that as "not found" would fail a build that
            // is perfectly correct.
            var users = Clip("AfkLoopingDefault");
            var controller = ControllerWith("Afk Stand Looping", users, out _);

            Assert.DoesNotThrow(() => EasyLocoModularAvatarBuilder.ApplyStateMotionOverrides(controller,
                Replacements(("Afk Stand Looping", users))));
        }

        [Test]
        public void ASharedTreeCountsForTheClipInsideIt()
        {
            // The build's dominant shape, and the only one where the two lookups both matter: a
            // blend tree that lives in its own asset, played by more than one state. Contains
            // decides the tree is worth cloning, the clone does the swap and the counting, and the
            // second state takes the cached clone without walking it again. If those two walks ever
            // stop agreeing, a correct build starts failing with "has no motion named ...".
            var tree = TreeAsset("Shared", Clip("IdleStandDefault"), Clip("proxy_walk_forward"));
            var controller = ControllerWith("Standing", tree, out var standing);
            var crouching = controller.layers[0].stateMachine.AddState("Crouching");
            spawned.Add(crouching);
            crouching.motion = tree;

            var replacement = Clip("UsersOwnIdle");
            Assert.DoesNotThrow(() => EasyLocoModularAvatarBuilder.ReplaceMotions(controller,
                Replacements(("IdleStandDefault", replacement)), TempFolder, null));

            var standingTree = (BlendTree)standing.motion;
            Assert.That(standingTree, Is.Not.SameAs(tree), "the shared asset should have been cloned, not written into");
            Assert.That(standingTree.children[0].motion, Is.SameAs(replacement));
            Assert.That(crouching.motion, Is.SameAs(standingTree), "both states should land on the one clone");
            Assert.That(tree.children[0].motion.name, Is.EqualTo("IdleStandDefault"), "the shared asset was rewritten");
        }

        private BlendTree TreeAsset(string name, params Motion[] children)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", TempFolderName);
            }

            var tree = new BlendTree { name = name, useAutomaticThresholds = false };
            tree.children = children.Select(child => new ChildMotion { motion = child, timeScale = 1f }).ToArray();
            AssetDatabase.CreateAsset(tree, TempFolder + "/" + name + ".asset");
            return AssetDatabase.LoadAssetAtPath<BlendTree>(TempFolder + "/" + name + ".asset");
        }

        private AnimatorController ControllerWith(string stateName, Motion motion, out AnimatorState state)
        {
            var controller = new AnimatorController();
            spawned.Add(controller);
            controller.AddLayer("Layer");

            var root = controller.layers[0].stateMachine;
            spawned.Add(root);

            state = root.AddState(stateName);
            spawned.Add(state);
            state.motion = motion;
            return controller;
        }

        private AnimatorState AddStance(AnimatorStateMachine root, string branchName, Motion motion)
        {
            var branch = root.AddStateMachine(branchName);
            spawned.Add(branch);

            var state = branch.AddState("Standing");
            spawned.Add(state);
            state.motion = motion;
            return state;
        }

        private static MotionReplacements Replacements(params (string Name, Motion Replacement)[] entries)
        {
            return new MotionReplacements(entries.ToDictionary(entry => entry.Name, entry => entry.Replacement));
        }

        private AnimationClip Clip(string name)
        {
            var clip = new AnimationClip { name = name };
            spawned.Add(clip);
            return clip;
        }
    }
}
