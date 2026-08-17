using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>Upright stance transitions, both VRMode branches. Directions once shipped inverted.</summary>
    public class LocomotionTransitionTests
    {
        private static BranchThresholds Thresholds(string branch)
        {
            return branch == EasyLocoConst.VrLocomotionStateMachine
                ? new BranchThresholds(0.63f, 0.8f, 0.47f, 0.5f)
                : new BranchThresholds(0.68f, 0.7f, 0.41f, 0.43f);
        }

        [Test]
        public void StandingDropsToCrouchingBelowZeroPointSixEight(
            [ValueSource(typeof(LocomotionTemplate), nameof(LocomotionTemplate.Branches))] string branch)
        {
            var t = UprightTransition(branch, "Standing");

            Assert.That(t.Mode, Is.EqualTo(AnimatorConditionMode.Less),
                "Standing -> Crouching must be Upright dropping below the threshold, not greater-than");
            Assert.That(t.Threshold, Is.EqualTo(Thresholds(branch).StandCrouch).Within(1e-4f));
            Assert.That(t.Destination, Is.EqualTo("Crouching"));
        }

        [Test]
        public void CrouchingClimbsToStandingAboveZeroPointSeven(
            [ValueSource(typeof(LocomotionTemplate), nameof(LocomotionTemplate.Branches))] string branch)
        {
            var t = UprightTransition(branch, "Crouching", AnimatorConditionMode.Greater);

            Assert.That(t.Threshold, Is.EqualTo(Thresholds(branch).CrouchStand).Within(1e-4f));
            Assert.That(t.Destination, Is.EqualTo("Standing"));
        }

        [Test]
        public void CrouchingDropsToProneBelowZeroPointFourOne(
            [ValueSource(typeof(LocomotionTemplate), nameof(LocomotionTemplate.Branches))] string branch)
        {
            var t = UprightTransition(branch, "Crouching", AnimatorConditionMode.Less);

            Assert.That(t.Threshold, Is.EqualTo(Thresholds(branch).CrouchProne).Within(1e-4f),
                "Crouching -> Prone must trigger while Upright is still falling, not while it climbs");
            Assert.That(t.Destination, Is.EqualTo("Prone"));
        }

        [Test]
        public void ProneClimbsToCrouchingAboveZeroPointFourThree(
            [ValueSource(typeof(LocomotionTemplate), nameof(LocomotionTemplate.Branches))] string branch)
        {
            var t = UprightTransition(branch, "Prone");

            Assert.That(t.Mode, Is.EqualTo(AnimatorConditionMode.Greater),
                "Prone -> Crouching must be Upright climbing above the threshold, not less-than");
            Assert.That(t.Threshold, Is.EqualTo(Thresholds(branch).ProneCrouch).Within(1e-4f));
            Assert.That(t.Destination, Is.EqualTo("Crouching"));
        }

        [Test]
        public void NoExtraUprightTransitionsExist(
            [ValueSource(typeof(LocomotionTemplate), nameof(LocomotionTemplate.Branches))] string branch)
        {
            var states = LocomotionTemplate.StanceStates(branch);

            foreach (var name in LocomotionTemplate.Stances)
            {
                Assert.That(states, Contains.Key(name), $"{name} state missing from {branch}");

                var upright = UprightTransitions(states[name]).ToList();
                Assert.That(upright.Count, Is.EqualTo(name == "Crouching" ? 2 : 1),
                    $"{branch}/{name} should carry only its SDK stance transition(s) on Upright");
            }
        }

        private readonly struct BranchThresholds
        {
            public readonly float StandCrouch; // Standing -> Crouching (Upright below this)
            public readonly float CrouchStand; // Crouching -> Standing (Upright above this)
            public readonly float CrouchProne; // Crouching -> Prone (Upright below this)
            public readonly float ProneCrouch; // Prone -> Crouching (Upright above this)

            public BranchThresholds(float standCrouch, float crouchStand, float crouchProne, float proneCrouch)
            {
                StandCrouch = standCrouch;
                CrouchStand = crouchStand;
                CrouchProne = crouchProne;
                ProneCrouch = proneCrouch;
            }
        }

        private readonly struct TransitionExpectation
        {
            public readonly AnimatorConditionMode Mode;
            public readonly float Threshold;
            public readonly string Destination;

            public TransitionExpectation(AnimatorConditionMode mode, float threshold, string destination)
            {
                Mode = mode;
                Threshold = threshold;
                Destination = destination;
            }
        }

        private static TransitionExpectation UprightTransition(string branch, string stateName, AnimatorConditionMode? mode = null)
        {
            var states = LocomotionTemplate.StanceStates(branch);
            Assert.That(states, Contains.Key(stateName), $"{stateName} state missing from {branch}");

            var matches = UprightTransitions(states[stateName], mode).ToList();

            Assert.That(matches, Has.Count.EqualTo(1),
                mode == null
                    ? $"{branch}/{stateName} should have exactly one Upright transition"
                    : $"{branch}/{stateName} should have exactly one Upright transition with mode {mode}");

            var transition = matches[0];
            return new TransitionExpectation(transition.conditions[0].mode, transition.conditions[0].threshold,
                DestinationName(transition));
        }

        private static IEnumerable<AnimatorStateTransition> UprightTransitions(AnimatorState state, AnimatorConditionMode? mode = null)
        {
            return state.transitions.Where(t =>
                t.conditions.Length == 1
                && t.conditions[0].parameter == "Upright"
                && (!mode.HasValue || t.conditions[0].mode == mode.Value));
        }

        private static string DestinationName(AnimatorStateTransition transition)
        {
            if (transition.isExit) return "(exit)";
            if (transition.destinationState != null) return transition.destinationState.name;
            if (transition.destinationStateMachine != null) return transition.destinationStateMachine.name;
            return "(none)";
        }
    }
}
