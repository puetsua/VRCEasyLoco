using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// The stance transitions in the base locomotion template. These regressed once when sleeping
    /// was extracted into its own controller: the Prone &lt;-&gt; Crouching transitions were re-created
    /// with the comparison direction swapped, so crouching snapped into the prone pose (and got
    /// stuck there) whenever the legs were animator-driven - i.e. in 3-point/desktop, while FBT
    /// masked it. This pins the four Upright transitions to the SDK default directions so a
    /// hand-recreated transition can never silently invert again.
    ///
    /// Every case runs over both VRMode branches. They are separate copies of the same stance
    /// machine, so a transition can be inverted in one and not the other - and a VR-only inversion
    /// is exactly the kind that reaches a headset unnoticed.
    /// </summary>
    public class LocomotionTransitionTests
    {
        // SDK default locomotion uses these exact thresholds, so the template must match: standing
        // is Upright ~1, crouching ~0.5, prone ~0.1, and the boundaries sit at 0.41 / 0.43 / 0.68 / 0.7.
        private const float StandCrouchThreshold = 0.68f;
        private const float CrouchStandThreshold = 0.7f;
        private const float CrouchProneThreshold = 0.41f;
        private const float ProneCrouchThreshold = 0.43f;

        [Test]
        public void StandingDropsToCrouchingBelowZeroPointSixEight(
            [ValueSource(typeof(LocomotionTemplate), nameof(LocomotionTemplate.Branches))] string branch)
        {
            var t = UprightTransition(branch, "Standing");

            Assert.That(t.Mode, Is.EqualTo(AnimatorConditionMode.Less),
                "Standing -> Crouching must be Upright < 0.68, not greater-than");
            Assert.That(t.Threshold, Is.EqualTo(StandCrouchThreshold).Within(1e-4f));
            Assert.That(t.Destination, Is.EqualTo("Crouching"));
        }

        [Test]
        public void CrouchingClimbsToStandingAboveZeroPointSeven(
            [ValueSource(typeof(LocomotionTemplate), nameof(LocomotionTemplate.Branches))] string branch)
        {
            var t = UprightTransition(branch, "Crouching", AnimatorConditionMode.Greater);

            Assert.That(t.Threshold, Is.EqualTo(CrouchStandThreshold).Within(1e-4f));
            Assert.That(t.Destination, Is.EqualTo("Standing"));
        }

        [Test]
        public void CrouchingDropsToProneBelowZeroPointFourOne(
            [ValueSource(typeof(LocomotionTemplate), nameof(LocomotionTemplate.Branches))] string branch)
        {
            var t = UprightTransition(branch, "Crouching", AnimatorConditionMode.Less);

            Assert.That(t.Threshold, Is.EqualTo(CrouchProneThreshold).Within(1e-4f),
                "Crouching -> Prone must trigger while Upright is still falling, not while it climbs");
            Assert.That(t.Destination, Is.EqualTo("Prone"));
        }

        [Test]
        public void ProneClimbsToCrouchingAboveZeroPointFourThree(
            [ValueSource(typeof(LocomotionTemplate), nameof(LocomotionTemplate.Branches))] string branch)
        {
            var t = UprightTransition(branch, "Prone");

            Assert.That(t.Mode, Is.EqualTo(AnimatorConditionMode.Greater),
                "Prone -> Crouching must be Upright > 0.43, not less-than");
            Assert.That(t.Threshold, Is.EqualTo(ProneCrouchThreshold).Within(1e-4f));
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
