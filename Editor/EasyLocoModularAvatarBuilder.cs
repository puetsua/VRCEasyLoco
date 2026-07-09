using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using Puetsua.VRCEasyLoco;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Puetsua.VRCEasyLoco.Editor
{
    internal static class EasyLocoModularAvatarBuilder
    {
        private const string TemplateControllerFolder = "Packages/vrchat.puetsuaworkshop.easyloco/Animators";
        private const string BaseTemplatePath = TemplateControllerFolder + "/EasyLocoBaseTemplate.controller";
        private const string ActionTemplatePath = TemplateControllerFolder + "/EasyLocoActionTemplate.controller";
        private const string EntryMenuPath = "Packages/vrchat.puetsuaworkshop.easyloco/Menus/EasyLocoEntry.asset";
        private const string EmoteParameterName = "VRCEmote";
        private const string GeneratedRoot = "Assets/PuetsuaWorkshop/Generated/EasyLoco";

        public static void Build(EasyLoco easyLoco)
        {
            if (easyLoco == null)
            {
                return;
            }

            var avatar = easyLoco.Avatar;
            if (avatar == null)
            {
                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName, "EasyLoco must be placed on an avatar with a VRCAvatarDescriptor.", "OK");
                return;
            }

            var outputFolder = GetOutputFolder(avatar);
            EnsureFolder(outputFolder);

            var host = GetOrCreateGeneratedObject(easyLoco);

            // Always generate copies of both templates so the avatar merges the generated
            // controllers, never the shared template assets (which a user could edit by accident).
            var baseController = BuildController(BaseTemplatePath, outputFolder, "EasyLocoBase.controller", CreateBaseReplacements(easyLoco));
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Base, baseController);

            var actionController = BuildController(ActionTemplatePath, outputFolder, "EasyLocoAction.controller", CreateActionReplacements(easyLoco));
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Action, actionController);

            // The Action layer is driven by VRCEmote, exposed through the EasyLoco expression menu.
            EnsureEmoteParameter(host);
            EnsureMenuInstaller(host);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.SetDirty(easyLoco);
            EditorUtility.DisplayDialog(EasyLocoConst.DisplayName, "Built Modular Avatar controllers and expression menu.", "OK");
        }

        private static RuntimeAnimatorController BuildController(string sourcePath, string outputFolder, string fileName, IReadOnlyDictionary<string, Motion> replacements)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(sourcePath) == null)
            {
                throw new FileNotFoundException("Project template animator was not found.", sourcePath);
            }

            var outputPath = outputFolder + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null)
            {
                AssetDatabase.DeleteAsset(outputPath);
            }

            if (!AssetDatabase.CopyAsset(sourcePath, outputPath))
            {
                throw new IOException($"Failed to copy template animator to {outputPath}");
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(outputPath);
            ReplaceMotions(controller, replacements, outputFolder);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static IReadOnlyDictionary<string, Motion> CreateBaseReplacements(EasyLoco easyLoco)
        {
            var replacements = new Dictionary<string, Motion>();
            if (!easyLoco.useCustomBaseLocomotion)
            {
                return replacements;
            }

            // These names are the idle (velocity-zero) clips embedded at the centre of the
            // Default* locomotion blend trees; swapping them changes the still pose only.
            AddReplacement(replacements, "IdleDefaultStand", easyLoco.baseStandStill);
            AddReplacement(replacements, "IdleDefaultCrouch", easyLoco.baseCrouchStill);
            AddReplacement(replacements, "IdleDefaultProne", easyLoco.baseLowCrawlStill);
            return replacements;
        }

        private static IReadOnlyDictionary<string, Motion> CreateActionReplacements(EasyLoco easyLoco)
        {
            var replacements = new Dictionary<string, Motion>();
            if (!easyLoco.useCustomAction)
            {
                return replacements;
            }

            AddReplacement(replacements, "proxy_afk", easyLoco.actionAfk);
            return replacements;
        }

        private static void AddReplacement(IDictionary<string, Motion> replacements, string targetMotionName, AnimationClip animation)
        {
            if (animation != null)
            {
                replacements[targetMotionName] = animation;
            }
        }

        private static void ReplaceMotions(AnimatorController controller, IReadOnlyDictionary<string, Motion> replacements, string outputFolder)
        {
            if (controller == null || replacements == null || replacements.Count == 0)
            {
                return;
            }

            var controllerPath = AssetDatabase.GetAssetPath(controller);
            foreach (var layer in controller.layers)
            {
                ReplaceMotions(layer.stateMachine, replacements, outputFolder, controllerPath);
            }
        }

        private static void ReplaceMotions(AnimatorStateMachine stateMachine, IReadOnlyDictionary<string, Motion> replacements, string outputFolder, string controllerPath)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                var replaced = ReplaceMotion(state.motion, replacements, outputFolder, controllerPath);
                if (replaced != state.motion)
                {
                    state.motion = replaced;
                    EditorUtility.SetDirty(state);
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                ReplaceMotions(childStateMachine.stateMachine, replacements, outputFolder, controllerPath);
            }
        }

        private static Motion ReplaceMotion(Motion motion, IReadOnlyDictionary<string, Motion> replacements, string outputFolder, string controllerPath)
        {
            if (motion == null)
            {
                return null;
            }

            if (motion is BlendTree blendTree)
            {
                if (!SubtreeContainsReplacement(blendTree, replacements))
                {
                    return blendTree;
                }

                // The Default* locomotion blend trees live as shared package assets. Mutating them
                // in place would corrupt the package for every avatar, so clone the whole tree into
                // this avatar's generated folder and swap the idle clip inside the copy instead.
                var motionPath = AssetDatabase.GetAssetPath(blendTree);
                var isSharedAsset = !string.IsNullOrEmpty(motionPath) && motionPath != controllerPath;
                if (isSharedAsset)
                {
                    return CloneBlendTree(blendTree, replacements, outputFolder);
                }

                // Blend trees embedded inside the copied controller are owned by it and safe to edit.
                ReplaceBlendTreeMotionsInPlace(blendTree, replacements, outputFolder, controllerPath);
                return blendTree;
            }

            return replacements.TryGetValue(motion.name, out var replacement) ? replacement : motion;
        }

        private static void ReplaceBlendTreeMotionsInPlace(BlendTree blendTree, IReadOnlyDictionary<string, Motion> replacements, string outputFolder, string controllerPath)
        {
            var children = blendTree.children;
            var changed = false;

            for (var i = 0; i < children.Length; i++)
            {
                var original = children[i].motion;
                var replaced = ReplaceMotion(original, replacements, outputFolder, controllerPath);
                if (replaced != original)
                {
                    children[i].motion = replaced;
                    changed = true;
                }
            }

            if (changed)
            {
                blendTree.children = children;
                EditorUtility.SetDirty(blendTree);
            }
        }

        private static bool SubtreeContainsReplacement(BlendTree blendTree, IReadOnlyDictionary<string, Motion> replacements)
        {
            foreach (var child in blendTree.children)
            {
                var motion = child.motion;
                if (motion is BlendTree childTree)
                {
                    if (SubtreeContainsReplacement(childTree, replacements))
                    {
                        return true;
                    }
                }
                else if (motion != null && replacements.ContainsKey(motion.name))
                {
                    return true;
                }
            }

            return false;
        }

        private static BlendTree CloneBlendTree(BlendTree source, IReadOnlyDictionary<string, Motion> replacements, string outputFolder)
        {
            var nested = new List<BlendTree>();
            var root = CloneBlendTreeInMemory(source, replacements, nested);

            // Deterministic name so rebuilding overwrites the previous clone instead of piling up copies.
            var clonePath = outputFolder + "/EasyLoco" + SanitizeFileName(source.name) + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(clonePath) != null)
            {
                AssetDatabase.DeleteAsset(clonePath);
            }

            AssetDatabase.CreateAsset(root, clonePath);
            foreach (var childTree in nested)
            {
                if (childTree == root)
                {
                    continue;
                }

                childTree.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(childTree, root);
            }

            EditorUtility.SetDirty(root);
            return root;
        }

        private static BlendTree CloneBlendTreeInMemory(BlendTree source, IReadOnlyDictionary<string, Motion> replacements, List<BlendTree> collected)
        {
            var clone = Object.Instantiate(source);
            clone.name = source.name;

            var children = clone.children;
            for (var i = 0; i < children.Length; i++)
            {
                var motion = children[i].motion;
                if (motion is BlendTree childTree)
                {
                    children[i].motion = CloneBlendTreeInMemory(childTree, replacements, collected);
                }
                else if (motion != null && replacements.TryGetValue(motion.name, out var replacement))
                {
                    children[i].motion = replacement;
                }
            }

            clone.children = children;
            collected.Add(clone);
            return clone;
        }

        private static void EnsureMenuInstaller(GameObject host)
        {
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(EntryMenuPath);
            if (menu == null)
            {
                throw new FileNotFoundException("EasyLoco expression menu was not found.", EntryMenuPath);
            }

            var installer = host.GetComponents<ModularAvatarMenuInstaller>()
                .FirstOrDefault(component => component.menuToAppend == menu || component.menuToAppend == null);

            if (installer == null)
            {
                installer = Undo.AddComponent<ModularAvatarMenuInstaller>(host);
            }

            Undo.RecordObject(installer, "Build EasyLoco Modular Avatar");
            installer.menuToAppend = menu;
            installer.installTargetMenu = null; // append to the avatar's root expression menu
            EditorUtility.SetDirty(installer);
        }

        private static void EnsureEmoteParameter(GameObject host)
        {
            var maParameters = host.GetComponent<ModularAvatarParameters>();
            if (maParameters == null)
            {
                maParameters = Undo.AddComponent<ModularAvatarParameters>(host);
            }

            Undo.RecordObject(maParameters, "Build EasyLoco Modular Avatar");
            if (maParameters.parameters == null)
            {
                maParameters.parameters = new List<ParameterConfig>();
            }

            if (!maParameters.parameters.Any(parameter => parameter.nameOrPrefix == EmoteParameterName))
            {
                maParameters.parameters.Add(new ParameterConfig
                {
                    nameOrPrefix = EmoteParameterName,
                    syncType = ParameterSyncType.Int,
                    localOnly = false,
                    saved = false,
                    defaultValue = 0,
                });
            }

            EditorUtility.SetDirty(maParameters);
        }

        private static GameObject GetOrCreateGeneratedObject(EasyLoco easyLoco)
        {
            var parent = easyLoco.transform;
            var existing = parent.Find(EasyLocoConst.GeneratedObjectName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var generated = new GameObject(EasyLocoConst.GeneratedObjectName);
            Undo.RegisterCreatedObjectUndo(generated, "Build EasyLoco Modular Avatar");
            Undo.SetTransformParent(generated.transform, parent, "Build EasyLoco Modular Avatar");
            generated.transform.localPosition = Vector3.zero;
            generated.transform.localRotation = Quaternion.identity;
            generated.transform.localScale = Vector3.one;
            return generated;
        }

        private static void EnsureMergeAnimator(GameObject gameObject, VRCAvatarDescriptor.AnimLayerType layerType, RuntimeAnimatorController controller)
        {
            var mergeAnimator = gameObject.GetComponents<ModularAvatarMergeAnimator>()
                .FirstOrDefault(component => component.layerType == layerType && component.mergeAnimatorMode == MergeAnimatorMode.Replace);

            if (mergeAnimator == null)
            {
                mergeAnimator = Undo.AddComponent<ModularAvatarMergeAnimator>(gameObject);
            }

            Undo.RecordObject(mergeAnimator, "Build EasyLoco Modular Avatar");
            mergeAnimator.animator = controller;
            mergeAnimator.layerType = layerType;
            mergeAnimator.mergeAnimatorMode = MergeAnimatorMode.Replace;
            mergeAnimator.pathMode = MergeAnimatorPathMode.Absolute;
            mergeAnimator.matchAvatarWriteDefaults = true;
            mergeAnimator.deleteAttachedAnimator = false;
            EditorUtility.SetDirty(mergeAnimator);
        }

        private static string GetOutputFolder(VRCAvatarDescriptor avatar)
        {
            var avatarName = SanitizeFileName(avatar.gameObject.name);
            return GeneratedRoot + "/" + avatarName;
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(value.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        }

        private static void EnsureFolder(string folderPath)
        {
            var parts = folderPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}


