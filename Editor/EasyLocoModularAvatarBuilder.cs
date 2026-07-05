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
        private const string BaseTemplatePath = TemplateControllerFolder + "/EasyLoco_BaseTemplate.controller";
        private const string ActionTemplatePath = TemplateControllerFolder + "/EasyLoco_ActionTemplate.controller";
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
            var baseController = BuildController(BaseTemplatePath, outputFolder, "EasyLoco_Base.controller", CreateBaseReplacements(easyLoco));
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Base, baseController);

            var actionController = BuildController(ActionTemplatePath, outputFolder, "EasyLoco_Action.controller", CreateActionReplacements(easyLoco));
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
            ReplaceMotions(controller, replacements);
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

            AddReplacement(replacements, "proxy_stand_still", easyLoco.baseStandStill);
            AddReplacement(replacements, "proxy_crouch_still", easyLoco.baseCrouchStill);
            AddReplacement(replacements, "proxy_low_crawl_still", easyLoco.baseLowCrawlStill);
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

        private static void ReplaceMotions(AnimatorController controller, IReadOnlyDictionary<string, Motion> replacements)
        {
            if (controller == null || replacements == null || replacements.Count == 0)
            {
                return;
            }

            foreach (var layer in controller.layers)
            {
                ReplaceMotions(layer.stateMachine, replacements);
            }
        }

        private static void ReplaceMotions(AnimatorStateMachine stateMachine, IReadOnlyDictionary<string, Motion> replacements)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                state.motion = ReplaceMotion(state.motion, replacements);
                EditorUtility.SetDirty(state);
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                ReplaceMotions(childStateMachine.stateMachine, replacements);
            }
        }

        private static Motion ReplaceMotion(Motion motion, IReadOnlyDictionary<string, Motion> replacements)
        {
            if (motion == null)
            {
                return null;
            }

            if (motion is BlendTree blendTree)
            {
                ReplaceBlendTreeMotions(blendTree, replacements);
                return blendTree;
            }

            return replacements.TryGetValue(motion.name, out var replacement) ? replacement : motion;
        }

        private static void ReplaceBlendTreeMotions(BlendTree blendTree, IReadOnlyDictionary<string, Motion> replacements)
        {
            var children = blendTree.children;
            var changed = false;

            for (var i = 0; i < children.Length; i++)
            {
                var original = children[i].motion;
                var replaced = ReplaceMotion(original, replacements);
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


