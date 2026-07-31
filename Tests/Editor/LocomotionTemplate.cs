using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// Reading the shipped base template, shared by the tests that pin it. The template branches
    /// locomotion on VRMode into two sub-state machines that each carry their own Standing,
    /// Crouching and Prone - so every lookup here goes through a branch name. Flattening the layer
    /// instead would silently test whichever branch Unity serialized first.
    /// </summary>
    internal static class LocomotionTemplate
    {
        public const string Path = EasyLocoConst.PackageRoot + "/Animators/EasyLocoBaseTemplate.controller";

        public static readonly string[] Branches =
        {
            EasyLocoConst.DesktopLocomotionStateMachine,
            EasyLocoConst.VrLocomotionStateMachine,
        };

        public static readonly string[] Stances = { "Standing", "Crouching", "Prone" };

        /// <summary>The named branch's own stance states, by state name.</summary>
        public static Dictionary<string, AnimatorState> StanceStates(string branchName)
        {
            var branch = Branch(branchName);

            return branch.states
                .Select(child => child.state)
                .Where(state => state != null && Stances.Contains(state.name))
                .ToDictionary(state => state.name);
        }

        public static AnimatorStateMachine Branch(string branchName)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Path);
            Assume.That(controller, Is.Not.Null, $"Base template not found at {Path}");

            var layer = controller.layers.FirstOrDefault(l => l.name == "Locomotion");
            Assume.That(layer, Is.Not.Null, "Locomotion layer missing from base template");

            var branch = Find(layer.stateMachine, branchName);
            Assert.That(branch, Is.Not.Null, $"{branchName} state machine missing from base template");
            return branch;
        }

        private static AnimatorStateMachine Find(AnimatorStateMachine stateMachine, string name)
        {
            if (stateMachine == null || stateMachine.name == name)
            {
                return stateMachine;
            }

            return stateMachine.stateMachines
                .Select(child => Find(child.stateMachine, name))
                .FirstOrDefault(match => match != null);
        }
    }
}
