using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// The sleeping locomotion is layered over the avatar's base locomotion as a separate Override
    /// controller, so its layers sit at weight 1 whenever the avatar is awake. Their default states
    /// must therefore be true pass-throughs: a state with a null motion in an Override layer at
    /// weight 1 stomps the locomotion below it (in VRChat the legs lock into a wrong pose and the
    /// walk animation effectively disappears), whereas a 0-curve clip such as EasyLocoEmpty does
    /// not. This regressed once when the passthrough states were changed from EasyLocoEmpty to a
    /// null motion, so this test pins them to the clip.
    /// </summary>
    public class SleepTemplateMotionTests
    {
        private const string SleepTemplatePath =
            EasyLocoConst.PackageRoot + "/Animators/EasyLocoSleepTemplate.controller";

        // The 0-curve clip the passthrough states are expected to play. A null motion here is the
        // bug, not a simplification.
        private const string EasyLocoEmptyGuid = "30e0006b5260f8649a77be2f3c38595a";

        // Crouching (Empty) is the SleepLocomotion layer's default state - always active while
        // awake - and Tracking/Locked are the FeetLock layer's defaults. A null motion on any of
        // these stomps the base locomotion every frame.
        private static readonly string[] PassthroughStates =
        {
            "Crouching (Empty)",
            "Tracking",
            "Locked",
        };

        [Test]
        public void PassthroughStatesUseEasyLocoEmptyNotNoMotion()
        {
            var states = LoadStatesByName();
            var expectedClip = LoadEasyLocoEmptyClip();

            foreach (var name in PassthroughStates)
            {
                Assert.That(states, Contains.Key(name), $"{name} state missing from sleep template");

                var motion = states[name].motion;
                Assert.That(motion, Is.Not.Null,
                    $"{name} has no motion (null). A null-motion Override layer at weight 1 stomps the "
                    + "base locomotion - the avatar's legs lock and the walk animation is lost. "
                    + "Use the EasyLocoEmpty clip instead.");
                Assert.That(AssetDatabase.GetAssetPath(motion),
                    Is.EqualTo(AssetDatabase.GetAssetPath(expectedClip)),
                    $"{name} should play EasyLocoEmpty so the layer passes through while awake");
            }
        }

        private static Dictionary<string, AnimatorState> LoadStatesByName()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(SleepTemplatePath);
            Assume.That(controller, Is.Not.Null, $"Sleep template not found at {SleepTemplatePath}");

            var byName = new Dictionary<string, AnimatorState>();
            foreach (var layer in controller.layers)
            {
                CollectStates(layer.stateMachine, byName);
            }

            return byName;
        }

        private static AnimationClip LoadEasyLocoEmptyClip()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(EasyLocoConst.AnimationsFolder + "/EasyLocoEmpty.anim");
            Assume.That(clip, Is.Not.Null, "EasyLocoEmpty clip not found under Animations/");
            return clip;
        }

        private static void CollectStates(AnimatorStateMachine stateMachine, Dictionary<string, AnimatorState> into)
        {
            foreach (var child in stateMachine.states)
            {
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
    }
}