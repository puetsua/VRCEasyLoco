using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// The stance transitions in the base locomotion template. These regressed once when sleeping
    /// was extracted into its own controller: the Prone &lt;-&gt; Crouching transitions were re-created
    /// with the comparison direction swapped, so crouching snapped into the prone pose (and got
    /// stuck there) whenever the legs were animator-driven - i.e. in 3-point/desktop, while FBT
    /// masked it. This pins the four Upright transitions to the SDK default directions so a
    /// hand-recreated transition can never silently invert again.
    /// </summary>
    public class LocomotionTransitionTests
    {
        private const string BaseTemplatePath =
            EasyLocoConst.PackageRoot + "/Animators/EasyLocoBaseTemplate.controller";

        // SDK default locomotion uses these exact thresholds, so the template must match: standing
        // is Upright ~1, crouching ~0.5, prone ~0.1, and the boundaries sit at 0.41 / 0.43 / 0.68 / 0.7.
        private const float StandCrouchThreshold = 0.68f;
        private const float CrouchStandThreshold = 0.7f;
        private const float CrouchProneThreshold = 0.41f;
        private const float ProneCrouchThreshold = 0.43f;

        [Test]
        public void StandingDropsToCrouchingBelowZeroPointSixEight()
        {
            var t = UprightTransition("Standing");

            Assert.That(t.Mode, Is.EqualTo(AnimatorConditionMode.Less),
                "Standing -> Crouching must be Upright < 0.68, not greater-than");
            Assert.That(t.Threshold, Is.EqualTo(StandCrouchThreshold).Within(1e-4f));
            Assert.That(t.Destination, Is.EqualTo("Crouching"));
        }

        [Test]
        public void CrouchingClimbsToStandingAboveZeroPointSeven()
        {
            var t = UprightTransition("Crouching", AnimatorConditionMode.Greater);

            Assert.That(t.Threshold, Is.EqualTo(CrouchStandThreshold).Within(1e-4f));
            Assert.That(t.Destination, Is.EqualTo("Standing"));
        }

        [Test]
        public void CrouchingDropsToProneBelowZeroPointFourOne()
        {
            var t = UprightTransition("Crouching", AnimatorConditionMode.Less);

            Assert.That(t.Threshold, Is.EqualTo(CrouchProneThreshold).Within(1e-4f),
                "Crouching -> Prone must trigger while Upright is still falling, not while it climbs");
            Assert.That(t.Destination, Is.EqualTo("Prone"));
        }

        [Test]
        public void ProneClimbsToCrouchingAboveZeroPointFourThree()
        {
            var t = UprightTransition("Prone");

            Assert.That(t.Mode, Is.EqualTo(AnimatorConditionMode.Greater),
                "Prone -> Crouching must be Upright > 0.43, not less-than");
            Assert.That(t.Threshold, Is.EqualTo(ProneCrouchThreshold).Within(1e-4f));
            Assert.That(t.Destination, Is.EqualTo("Crouching"));
        }

        [Test]
        public void NoExtraUprightTransitionsExist()
        {
            var states = LoadStanceStates();

            foreach (var name in new[] { "Standing", "Crouching", "Prone" })
            {
                var upright = UprightTransitions(states[name]).ToList();
                Assert.That(upright.Count, Is.EqualTo(name == "Crouching" ? 2 : 1),
                    $"{name} should carry only its SDK stance transition(s) on Upright");
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

        private static TransitionExpectation UprightTransition(string stateName, AnimatorConditionMode? mode = null)
        {
            var states = LoadStanceStates();
            var matches = UprightTransitions(states[stateName], mode).ToList();

            Assert.That(matches, Has.Count.EqualTo(1),
                mode == null
                    ? $"{stateName} should have exactly one Upright transition"
                    : $"{stateName} should have exactly one Upright transition with mode {mode}");

            var transition = matches[0];
            return new TransitionExpectation(transition.conditions[0].mode, transition.conditions[0].threshold,
                DestinationName(transition));
        }

        private static Dictionary<string, AnimatorState> LoadStanceStates()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseTemplatePath);
            Assume.That(controller, Is.Not.Null, $"Base template not found at {BaseTemplatePath}");

            var layer = controller.layers.FirstOrDefault(l => l.name == "Locomotion");
            Assume.That(layer, Is.Not.Null, "Locomotion layer missing from base template");

            var byName = new Dictionary<string, AnimatorState>();
            CollectStates(layer.stateMachine, byName);

            foreach (var name in new[] { "Standing", "Crouching", "Prone" })
            {
                Assert.That(byName.ContainsKey(name), Is.True, $"{name} state missing from base template");
            }

            return byName;
        }

        private static void CollectStates(AnimatorStateMachine stateMachine, Dictionary<string, AnimatorState> into)
        {
            foreach (var child in stateMachine.states)
            {
                // First write wins: the locomotion states are unique, so a later duplicate (none
                // expected here) would not silently overwrite the stance we want to test.
                if (child.state != null && !into.ContainsKey(child.state.name))
                {
                    into[child.state.name] = child.state;
                }
            }

            foreach (var child in stateMachine.stateMachines)
            {
                if (child.stateMachine != null)
                {
                    CollectStates(child.stateMachine, into);
                }
            }
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