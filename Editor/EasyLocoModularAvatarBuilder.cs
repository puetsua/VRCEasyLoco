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
        private const string EmoteParameterName = "VRCEmote";
        private const string GeneratedRoot = "Assets/PuetsuaWorkshop/Generated/EasyLoco";

        private const string GeneratedAssetPrefix = "EasyLoco";

        private static LocalizedTextDataset Localized => LocalizedTextDataset.primary;

        private sealed class StanceBuild
        {
            public readonly string Key;
            public readonly string MenuLabel;
            public readonly List<EasyLoco.IdlePose> Poses;
            public readonly string IdleTargetName;
            public readonly string ParamName;

            public List<EasyLoco.IdlePose> Entries;
            public Motion Motion;
            public bool HasMenu;

            public bool Overrides => Entries != null && Entries.Count > 0;

            public StanceBuild(string key, string menuLabel, List<EasyLoco.IdlePose> poses, string idleTargetName, string paramName)
            {
                Key = key;
                MenuLabel = menuLabel;
                Poses = poses;
                IdleTargetName = idleTargetName;
                ParamName = paramName;
            }
        }

        public static string Build(EasyLoco easyLoco)
        {
            var outputFolder = PrepareOutputFolder(easyLoco);
            if (outputFolder == null)
            {
                return null;
            }

            string prefabPath;
            var host = new GameObject(EasyLocoConst.GeneratedObjectName);
            try
            {
                prefabPath = BuildHost(easyLoco, host, outputFolder);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }

            InstallPrefabInstance(easyLoco.transform, EasyLocoConst.GeneratedObjectName, prefabPath, "Build EasyLoco Modular Avatar");
            return prefabPath;
        }

        public static string BuildSleepLocomotion(EasyLoco easyLoco)
        {
            var outputFolder = PrepareOutputFolder(easyLoco);
            if (outputFolder == null)
            {
                return null;
            }

            // MA silently drops a menu installer whose target is not on the avatar. Nest under the
            // host only when the main EasyLoco menu is actually installed there.
            var host = easyLoco.transform.Find(EasyLocoConst.GeneratedObjectName);
            var parent = host != null ? host : easyLoco.transform;

            var controller = BuildSleepController(easyLoco, outputFolder);
            var prefabPath = BuildSleepPrefab(controller, outputFolder, nestUnderMainMenu: host != null);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var misplaced = FindSleepLocomotion(easyLoco);
            if (misplaced != null && misplaced.parent != parent)
            {
                Undo.DestroyObjectImmediate(misplaced.gameObject);
            }

            InstallPrefabInstance(parent, EasyLocoConst.SleepObjectName, prefabPath, "Build EasyLoco Sleep Locomotion");
            return prefabPath;
        }

        private static Transform FindSleepLocomotion(EasyLoco easyLoco)
        {
            var host = easyLoco.transform.Find(EasyLocoConst.GeneratedObjectName);
            var nested = host != null ? host.Find(EasyLocoConst.SleepObjectName) : null;
            return nested != null ? nested : easyLoco.transform.Find(EasyLocoConst.SleepObjectName);
        }

        public static bool HasSleepLocomotion(EasyLoco easyLoco)
        {
            return easyLoco != null && FindSleepLocomotion(easyLoco) != null;
        }

        private static string PrepareOutputFolder(EasyLoco easyLoco)
        {
            if (easyLoco == null)
            {
                return null;
            }

            var avatar = easyLoco.Avatar;
            if (avatar == null)
            {
                throw new System.InvalidOperationException("EasyLoco must be on the same GameObject as the VRCAvatarDescriptor.");
            }

            var outputFolder = GetOutputFolder(avatar);
            EnsureFolder(outputFolder);
            return outputFolder;
        }

        public static bool RemoveSleepLocomotion(EasyLoco easyLoco)
        {
            if (easyLoco == null)
            {
                return false;
            }

            var existing = FindSleepLocomotion(easyLoco);
            if (existing == null)
            {
                return false;
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
            return true;
        }

        private static void InstallPrefabInstance(Transform parent, string objectName, string prefabPath, string undoLabel)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException("EasyLoco prefab was not found after building.", prefabPath);
            }

            var existing = parent.Find(objectName);
            if (existing != null)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) == prefab)
                {
                    return;
                }

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = objectName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(instance, undoLabel);
        }

        private static string BuildHost(EasyLoco easyLoco, GameObject host, string outputFolder)
        {
            var stances = new List<StanceBuild>
            {
                new StanceBuild("Stand", Localized.menuStandPoses, easyLoco.standPoses, EasyLocoConst.StandIdleTarget, EasyLocoConst.IdleStandParam),
                new StanceBuild("Crouch", Localized.menuCrouchPoses, easyLoco.crouchPoses, EasyLocoConst.CrouchIdleTarget, EasyLocoConst.IdleCrouchParam),
                new StanceBuild("Prone", Localized.menuPronePoses, easyLoco.pronePoses, EasyLocoConst.ProneIdleTarget, EasyLocoConst.IdleProneParam),
            };

            foreach (var stance in stances)
            {
                SelectIdleEntries(stance);
            }

            var afkOverrides = BuildAfkOverrides(easyLoco);

            // Fail before writing: BuildController deletes the previous controller (new GUID) and
            // the installed prefab only catches up at SaveAsPrefabAsset.
            EnsureTemplateCarriesMotions(EasyLocoConst.BaseTemplatePath, stances.Where(stance => stance.Overrides).Select(stance => stance.IdleTargetName),
                EasyLocoConst.DesktopLocomotionStateMachine);
            EnsureTemplateCarriesStates(EasyLocoConst.ActionTemplatePath, afkOverrides.Keys);

            var baseReplacements = new Dictionary<string, Motion>();
            foreach (var stance in stances)
            {
                BuildIdleSelector(stance, outputFolder);
                if (stance.Motion != null)
                {
                    baseReplacements[stance.IdleTargetName] = stance.Motion;
                }
            }

            var baseController = (AnimatorController)BuildController(EasyLocoConst.BaseTemplatePath, outputFolder, "EasyLocoBase.controller", baseReplacements,
                EasyLocoConst.DesktopLocomotionStateMachine);
            foreach (var stance in stances)
            {
                if (stance.HasMenu)
                {
                    EnsureFloatParameter(baseController, stance.ParamName);
                }
            }
            EditorUtility.SetDirty(baseController);
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Base, baseController, MergeAnimatorMode.Replace);

            var actionController = (AnimatorController)BuildController(EasyLocoConst.ActionTemplatePath, outputFolder, "EasyLocoAction.controller", new Dictionary<string, Motion>());
            ApplyStateMotionOverrides(actionController, new MotionReplacements(afkOverrides));
            EditorUtility.SetDirty(actionController);
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Action, actionController, MergeAnimatorMode.Replace);

            EnsureEmoteParameter(host);

            var mainMenu = GetOrCreateLocalizedMainMenu(outputFolder);
            var entryMenu = GetOrCreateLocalizedEntry(outputFolder, mainMenu);
            EnsureMenuInstaller(host, entryMenu);

            BuildIdlePoseMenu(host, stances, outputFolder, mainMenu);
            foreach (var stance in stances)
            {
                if (stance.HasMenu)
                {
                    EnsureSyncedFloatParameter(host, stance.ParamName);
                }
            }

            var prefabPath = outputFolder + "/" + EasyLocoConst.GeneratedObjectName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(host, prefabPath, out var saved);
            if (!saved)
            {
                throw new IOException($"Failed to save the EasyLoco prefab to {prefabPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.SetDirty(easyLoco);
            return prefabPath;
        }

        private static AnimatorController BuildSleepController(EasyLoco easyLoco, string outputFolder)
        {
            EnsureTemplateCarriesMotions(EasyLocoConst.SleepTemplatePath, SleepTargets(easyLoco.sleep), null);

            var replacements = new Dictionary<string, Motion>();
            AddSleepReplacements(replacements, easyLoco.sleep, outputFolder);

            var controller = (AnimatorController)BuildController(EasyLocoConst.SleepTemplatePath, outputFolder, "EasyLocoSleep.controller", replacements, isSleepBuild: true);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static string BuildSleepPrefab(AnimatorController controller, string outputFolder, bool nestUnderMainMenu)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(EasyLocoConst.SleepPrefabPath);
            if (source == null)
            {
                throw new FileNotFoundException("EasyLoco sleep prefab was not found.", EasyLocoConst.SleepPrefabPath);
            }

            var prefabPath = outputFolder + "/" + EasyLocoConst.SleepObjectName + ".prefab";
            var host = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                host.name = EasyLocoConst.SleepObjectName;

                EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Base, controller, MergeAnimatorMode.Append);

                var installer = host.GetComponent<ModularAvatarMenuInstaller>();
                if (installer != null)
                {
                    installer.menuToAppend = null;
                    installer.installTargetMenu = nestUnderMainMenu ? GetOrCreateLocalizedMainMenu(outputFolder) : null;
                    EditorUtility.SetDirty(installer);
                }

                LocalizeSleepMenuItems(host);

                PrefabUtility.SaveAsPrefabAsset(host, prefabPath, out var saved);
                if (!saved)
                {
                    throw new IOException($"Failed to save the EasyLoco sleep prefab to {prefabPath}");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }

            return prefabPath;
        }

        private static void LocalizeSleepMenuItems(GameObject host)
        {
            var menuGroup = host.GetComponent<ModularAvatarMenuGroup>();
            if (menuGroup == null || menuGroup.targetObject == null)
            {
                return;
            }

            foreach (var item in menuGroup.targetObject.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if (item.Control == null)
                {
                    continue;
                }

                var parameterName = item.Control.parameter != null ? item.Control.parameter.name : null;

                if (item.Control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                {
                    item.label = Localized.menuSleep;
                }
                else if (parameterName == EasyLocoConst.EnableHeightParam)
                {
                    item.label = Localized.menuEnableHeight;
                }
                else if (parameterName == EasyLocoConst.AdjustHeightParam
                    || HasSubParameter(item.Control, EasyLocoConst.HeightParam))
                {
                    item.label = Localized.menuAdjustHeight;
                }
                else if (parameterName == EasyLocoConst.SleepModeParam)
                {
                    item.label = Localized.menuSleepLoco;
                }
                else if (parameterName == EasyLocoConst.FeetLockParam)
                {
                    item.label = Localized.menuFeetLock;
                }

                EditorUtility.SetDirty(item);
            }
        }

        private static bool HasSubParameter(VRCExpressionsMenu.Control control, string parameterName)
        {
            if (control.subParameters == null)
            {
                return false;
            }

            for (var i = 0; i < control.subParameters.Length; i++)
            {
                if (control.subParameters[i] != null && control.subParameters[i].name == parameterName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SelectIdleEntries(StanceBuild stance)
        {
            stance.Entries = (stance.Poses ?? new List<EasyLoco.IdlePose>())
                .Where(pose => pose != null && pose.clip != null)
                .ToList();

            stance.HasMenu = stance.Entries.Count > 1;
        }

        private static void BuildIdleSelector(StanceBuild stance, string outputFolder)
        {
            if (stance.Entries.Count == 0)
            {
                stance.Motion = null;
                return;
            }

            if (stance.Entries.Count == 1)
            {
                stance.Motion = stance.Entries[0].clip;
                return;
            }

            var tree = new BlendTree
            {
                name = "EasyLocoIdle" + stance.Key,
                blendType = BlendTreeType.Simple1D,
                blendParameter = stance.ParamName,
                useAutomaticThresholds = false,
            };

            var children = new ChildMotion[stance.Entries.Count];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = new ChildMotion
                {
                    motion = stance.Entries[i].clip,
                    threshold = PoseValue(i, children.Length),
                    timeScale = 1f,
                    directBlendParameter = stance.ParamName,
                };
            }
            tree.children = children;

            var path = outputFolder + "/EasyLocoIdle" + stance.Key + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(tree, path);
            EditorUtility.SetDirty(tree);

            stance.Motion = tree;
        }

        private static void EnsureTemplateCarriesMotions(string sourcePath, IEnumerable<string> names, string scopeStateMachineName)
        {
            var expected = ExpectedNames(names);
            if (expected.IsEmpty)
            {
                return;
            }

            var template = LoadTemplate(sourcePath);
            var found = new HashSet<string>();
            foreach (var root in CollectReplacementRoots(template, scopeStateMachineName))
            {
                CollectMotionNames(root, found);
            }

            MarkFound(expected, found);
            expected.ThrowIfUnmatched("motion", Scoped(sourcePath, scopeStateMachineName));
        }

        private static void EnsureTemplateCarriesStates(string sourcePath, IEnumerable<string> names)
        {
            var expected = ExpectedNames(names);
            if (expected.IsEmpty)
            {
                return;
            }

            var found = new HashSet<string>();
            foreach (var layer in LoadTemplate(sourcePath).layers)
            {
                CollectStateNames(layer.stateMachine, found);
            }

            MarkFound(expected, found);
            expected.ThrowIfUnmatched("state", sourcePath);
        }

        private static MotionReplacements ExpectedNames(IEnumerable<string> names)
        {
            return new MotionReplacements(names.Distinct().ToDictionary(name => name, name => (Motion)null));
        }

        private static void MarkFound(MotionReplacements expected, IEnumerable<string> found)
        {
            foreach (var name in found)
            {
                expected.TryGet(name, out _);
            }
        }

        private static void CollectMotionNames(AnimatorStateMachine stateMachine, HashSet<string> into)
        {
            foreach (var childState in stateMachine.states)
            {
                CollectMotionNames(childState.state.motion, into);
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                CollectMotionNames(childStateMachine.stateMachine, into);
            }
        }

        private static void CollectMotionNames(Motion motion, HashSet<string> into)
        {
            if (motion == null)
            {
                return;
            }

            if (motion is BlendTree blendTree)
            {
                foreach (var child in blendTree.children)
                {
                    CollectMotionNames(child.motion, into);
                }

                return;
            }

            into.Add(motion.name);
        }

        private static void CollectStateNames(AnimatorStateMachine stateMachine, HashSet<string> into)
        {
            foreach (var childState in stateMachine.states)
            {
                into.Add(childState.state.name);
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                CollectStateNames(childStateMachine.stateMachine, into);
            }
        }

        private static AnimatorController LoadTemplate(string sourcePath)
        {
            var template = AssetDatabase.LoadAssetAtPath<AnimatorController>(sourcePath);
            if (template == null)
            {
                throw new FileNotFoundException("Project template animator was not found.", sourcePath);
            }

            return template;
        }

        private static RuntimeAnimatorController BuildController(string sourcePath, string outputFolder, string fileName, IReadOnlyDictionary<string, Motion> replacements,
            string scopeStateMachineName = null, bool isSleepBuild = false)
        {
            LoadTemplate(sourcePath);

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
            ReplaceMotions(controller, new MotionReplacements(replacements), outputFolder, scopeStateMachineName, isSleepBuild);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static List<string> SleepTargets(EasyLoco.SleepSet sleep)
        {
            var targets = new List<string>();
            if (IsOverride(sleep?.up, EasyLocoConst.SleepUpClip))
            {
                targets.Add(EasyLocoConst.SleepUpTarget);
            }

            if (IsOverride(sleep?.down, EasyLocoConst.SleepDownClip))
            {
                targets.Add(EasyLocoConst.SleepDownTarget);
            }

            if (!IsOverride(sleep?.side, EasyLocoConst.SleepSideClip))
            {
                return targets;
            }

            foreach (var target in EasyLocoConst.SleepSideTargets)
            {
                var placeholder = SleepSidePlaceholder(target);
                if (placeholder != null && placeholder != sleep.side)
                {
                    targets.Add(target);
                }
            }

            return targets;
        }

        private static void AddSleepReplacements(IDictionary<string, Motion> replacements, EasyLoco.SleepSet sleep, string outputFolder)
        {
            foreach (var target in SleepTargets(sleep))
            {
                if (target == EasyLocoConst.SleepUpTarget)
                {
                    replacements[target] = sleep.up;
                }
                else if (target == EasyLocoConst.SleepDownTarget)
                {
                    replacements[target] = sleep.down;
                }
                else
                {
                    replacements[target] = CreateSideClipForSlot(sleep.side, SleepSidePlaceholder(target), outputFolder);
                }
            }
        }

        private static AnimationClip SleepSidePlaceholder(string target)
        {
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(EasyLocoConst.SleepSidePlaceholderClip(target));
        }

        private static bool IsOverride(AnimationClip clip, string builtInPath)
        {
            return clip != null && clip != AssetDatabase.LoadAssetAtPath<AnimationClip>(builtInPath);
        }

        private static AnimationClip CreateSideClipForSlot(AnimationClip sideClip, AnimationClip placeholder, string outputFolder)
        {
            var clip = Object.Instantiate(sideClip);
            CopyRootTransformSettings(placeholder, clip);

            var path = outputFolder + "/" + EasyLocoConst.GeneratedSleepClipName(placeholder.name, is5m: false) + ".anim";
            var persisted = GetOrCreateClipAssetInPlace(path, clip, is5m: false);
            if (persisted != clip)
            {
                Object.DestroyImmediate(clip);
            }

            return persisted;
        }

        private static void CopyRootTransformSettings(AnimationClip from, AnimationClip to)
        {
            var slot = AnimationUtility.GetAnimationClipSettings(from);
            var settings = AnimationUtility.GetAnimationClipSettings(to);
            settings.orientationOffsetY = slot.orientationOffsetY;
            settings.level = slot.level;
            settings.cycleOffset = slot.cycleOffset;
            settings.keepOriginalOrientation = slot.keepOriginalOrientation;
            settings.keepOriginalPositionY = slot.keepOriginalPositionY;
            settings.keepOriginalPositionXZ = slot.keepOriginalPositionXZ;
            settings.heightFromFeet = slot.heightFromFeet;
            settings.mirror = slot.mirror;
            AnimationUtility.SetAnimationClipSettings(to, settings);
        }

        private static Dictionary<string, Motion> BuildAfkOverrides(EasyLoco easyLoco)
        {
            var overrides = new Dictionary<string, Motion>();
            AddAfkOverrides(overrides, EasyLocoConst.AfkStances[0], easyLoco.standAfk);
            AddAfkOverrides(overrides, EasyLocoConst.AfkStances[1], easyLoco.crouchAfk);
            AddAfkOverrides(overrides, EasyLocoConst.AfkStances[2], easyLoco.proneAfk);
            return overrides;
        }

        internal static void ApplyStateMotionOverrides(AnimatorController controller, MotionReplacements overrides)
        {
            if (controller == null || overrides == null || overrides.IsEmpty)
            {
                return;
            }

            foreach (var layer in controller.layers)
            {
                SetStateMotionsByName(layer.stateMachine, overrides);
            }

            overrides.ThrowIfUnmatched("state", Describe(controller));
        }

        private static void AddAfkOverrides(IDictionary<string, Motion> map, string stance, EasyLoco.AfkSet set)
        {
            if (set == null)
            {
                return;
            }

            AddClipOverride(map, EasyLocoConst.AfkStateName(stance, EasyLocoConst.AfkStages[0]), set.entering);
            AddClipOverride(map, EasyLocoConst.AfkStateName(stance, EasyLocoConst.AfkStages[1]), set.looping);
            AddClipOverride(map, EasyLocoConst.AfkStateName(stance, EasyLocoConst.AfkStages[2]), set.exiting);
        }

        private static void AddClipOverride(IDictionary<string, Motion> map, string stateName, AnimationClip clip)
        {
            if (clip != null)
            {
                map[stateName] = clip;
            }
        }

        private static void SetStateMotionsByName(AnimatorStateMachine stateMachine, MotionReplacements overrides)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (overrides.TryGet(state.name, out var clip) && state.motion != clip)
                {
                    state.motion = clip;
                    EditorUtility.SetDirty(state);
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                SetStateMotionsByName(childStateMachine.stateMachine, overrides);
            }
        }

        // Unity blend trees read the float slot. An Int of the same name stays 0 and freezes child 0.
        private static void EnsureFloatParameter(AnimatorController controller, string name)
        {
            if (controller.parameters.Any(parameter => parameter.name == name))
            {
                return;
            }

            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        }

        internal static void ReplaceMotions(AnimatorController controller, MotionReplacements replacements, string outputFolder, string scopeStateMachineName, bool isSleepBuild = false)
        {
            if (controller == null || replacements == null)
            {
                return;
            }

            if (replacements.IsEmpty && !isSleepBuild)
            {
                return;
            }

            var roots = CollectReplacementRoots(controller, scopeStateMachineName);
            var controllerPath = AssetDatabase.GetAssetPath(controller);
            var clones = new Dictionary<BlendTree, BlendTree>();
            var clipCache = new Dictionary<(string slot, bool is5m), AnimationClip>();
            foreach (var root in roots)
            {
                ReplaceMotions(root, replacements, outputFolder, controllerPath, clones, clipCache, isSleepBuild);
            }

            replacements.ThrowIfUnmatched("motion", Scoped(Describe(controller), scopeStateMachineName));
        }

        private static string Describe(AnimatorController controller)
        {
            var path = AssetDatabase.GetAssetPath(controller);
            return string.IsNullOrEmpty(path) ? controller.name : path;
        }

        private static string Scoped(string where, string scopeStateMachineName)
        {
            return string.IsNullOrEmpty(scopeStateMachineName) ? where : $"\"{scopeStateMachineName}\" in {where}";
        }

        private static List<AnimatorStateMachine> CollectReplacementRoots(AnimatorController controller, string scopeStateMachineName)
        {
            var roots = new List<AnimatorStateMachine>();
            foreach (var layer in controller.layers)
            {
                if (string.IsNullOrEmpty(scopeStateMachineName))
                {
                    roots.Add(layer.stateMachine);
                }
                else
                {
                    CollectStateMachinesNamed(layer.stateMachine, scopeStateMachineName, roots);
                }
            }

            if (!string.IsNullOrEmpty(scopeStateMachineName) && roots.Count == 0)
            {
                throw new System.InvalidOperationException(
                    $"No state machine named \"{scopeStateMachineName}\" in {AssetDatabase.GetAssetPath(controller)}. " +
                    "The template's locomotion branches were renamed - update EasyLocoConst to match.");
            }

            return roots;
        }

        private static void CollectStateMachinesNamed(AnimatorStateMachine stateMachine, string name, List<AnimatorStateMachine> into)
        {
            if (stateMachine == null)
            {
                return;
            }

            if (stateMachine.name == name)
            {
                into.Add(stateMachine);
                return;
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                CollectStateMachinesNamed(childStateMachine.stateMachine, name, into);
            }
        }

        private static void ReplaceMotions(AnimatorStateMachine stateMachine, MotionReplacements replacements, string outputFolder, string controllerPath, Dictionary<BlendTree, BlendTree> clones, Dictionary<(string slot, bool is5m), AnimationClip> clipCache, bool isSleepBuild = false)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                var replaced = ReplaceMotion(state.motion, replacements, outputFolder, controllerPath, clones, clipCache, isSleepBuild);
                if (replaced != state.motion)
                {
                    state.motion = replaced;
                    EditorUtility.SetDirty(state);
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                ReplaceMotions(childStateMachine.stateMachine, replacements, outputFolder, controllerPath, clones, clipCache, isSleepBuild);
            }
        }

        private static Motion ReplaceMotion(Motion motion, MotionReplacements replacements, string outputFolder, string controllerPath, Dictionary<BlendTree, BlendTree> clones, Dictionary<(string slot, bool is5m), AnimationClip> clipCache, bool isSleepBuild = false)
        {
            if (motion == null)
            {
                return null;
            }

            if (motion is BlendTree blendTree)
            {
                var needsReplacement = SubtreeContainsReplacement(blendTree, replacements);
                var isSleepTree = isSleepBuild && IsOrContainsSleepBlendTree(blendTree);
                if (!needsReplacement && !isSleepTree)
                {
                    return blendTree;
                }

                if (IsSharedPackageAsset(blendTree, controllerPath))
                {
                    if (!clones.TryGetValue(blendTree, out var clone))
                    {
                        clone = CloneBlendTree(blendTree, replacements, outputFolder, clipCache, isSleepBuild);
                        clones.Add(blendTree, clone);
                    }

                    return clone;
                }

                ReplaceBlendTreeMotionsInPlace(blendTree, replacements, outputFolder, controllerPath, clones, clipCache, isSleepBuild);

                return blendTree;
            }

            return replacements.TryGet(motion.name, out var replacement) ? replacement : motion;
        }

        private static bool IsSharedPackageAsset(Motion motion, string controllerPath)
        {
            var motionPath = AssetDatabase.GetAssetPath(motion);
            return !string.IsNullOrEmpty(motionPath) && motionPath != controllerPath;
        }

        private static void ReplaceBlendTreeMotionsInPlace(BlendTree blendTree, MotionReplacements replacements, string outputFolder, string controllerPath, Dictionary<BlendTree, BlendTree> clones, Dictionary<(string slot, bool is5m), AnimationClip> clipCache, bool isSleepBuild = false)
        {
            var children = blendTree.children;
            var changed = false;

            for (var i = 0; i < children.Length; i++)
            {
                var original = children[i].motion;
                var replaced = ReplaceMotion(original, replacements, outputFolder, controllerPath, clones, clipCache, isSleepBuild);
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

        private static bool SubtreeContainsReplacement(BlendTree blendTree, MotionReplacements replacements)
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
                else if (motion != null && replacements.IsReplacementTarget(motion.name))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOrContainsSleepBlendTree(BlendTree blendTree)
        {
            if (IsSleepBlendTree(blendTree) || IsHeightBlendTree(blendTree))
            {
                return true;
            }

            foreach (var child in blendTree.children)
            {
                if (child.motion is BlendTree childTree && IsOrContainsSleepBlendTree(childTree))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsHeightBlendTree(BlendTree blendTree)
        {
            return blendTree.name != null && blendTree.name.EndsWith("5m");
        }

        private static bool IsSleepBlendTree(BlendTree blendTree)
        {
            return blendTree.name != null && blendTree.name.StartsWith("DefaultSleeping");
        }

        private static void DuplicateSleepTreeLeaves(BlendTree blendTree, string outputFolder, Dictionary<(string slot, bool is5m), AnimationClip> cache, MotionReplacements replacements)
        {
            var children = blendTree.children;
            var changed = false;
            var is5m = IsHeightBlendTree(blendTree);

            for (var i = 0; i < children.Length; i++)
            {
                var motion = children[i].motion;
                if (motion == null)
                {
                    continue;
                }

                if (motion is AnimationClip clip)
                {
                    var duplicate = GetOrCreateSleepDuplicateClip(clip, is5m, outputFolder, cache, replacements);
                    if (duplicate != clip)
                    {
                        children[i].motion = duplicate;
                        changed = true;
                    }
                }
                else if (motion is BlendTree childTree)
                {
                    DuplicateSleepTreeLeaves(childTree, outputFolder, cache, replacements);
                }
            }

            if (changed)
            {
                blendTree.children = children;
                EditorUtility.SetDirty(blendTree);
            }
        }

        private const float HeightOffsetMeters = EasyLocoConst.EyeHeightMetersMax;

        private static AnimationClip GetOrCreateSleepDuplicateClip(AnimationClip source, bool is5m, string outputFolder, Dictionary<(string slot, bool is5m), AnimationClip> cache, MotionReplacements replacements)
        {
            if (source == null)
            {
                return null;
            }

            var slot = SleepSlotName(source, replacements);
            var key = (slot, is5m);
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var outputPath = outputFolder + "/" + EasyLocoConst.GeneratedSleepClipName(slot, is5m) + ".anim";
            var clip = GetOrCreateClipAssetInPlace(outputPath, source, is5m);
            cache[key] = clip;
            return clip;
        }

        internal static string SleepSlotName(AnimationClip clip, MotionReplacements replacements)
        {
            if (clip != null && replacements != null && replacements.TryGetKey(clip, out var key)
                && IsSleepSlot(key))
            {
                return key;
            }

            if (clip != null && IsSleepSlot(clip.name))
            {
                return clip.name;
            }

            return clip == null || string.IsNullOrEmpty(clip.name)
                ? "SleepUnknown"
                : SanitizeFileName(clip.name);
        }

        private static bool IsSleepSlot(string name)
        {
            foreach (var slot in EasyLocoConst.SleepTargets)
            {
                if (name == slot)
                {
                    return true;
                }
            }

            return false;
        }

        private static AnimationClip GetOrCreateClipAssetInPlace(string outputPath, AnimationClip source, bool is5m)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
            if (clip == null)
            {
                clip = Object.Instantiate(source);
            }
            else
            {
                EditorUtility.CopySerialized(source, clip);
            }

            if (is5m)
            {
                AddHeightOffset(clip);
            }

            if (AssetDatabase.GetAssetPath(clip).Length == 0)
            {
                AssetDatabase.CreateAsset(clip, outputPath);
            }

            clip.name = Path.GetFileNameWithoutExtension(outputPath);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void AddHeightOffset(AnimationClip clip)
        {
            var binding = new EditorCurveBinding
            {
                path = string.Empty,
                type = typeof(Animator),
                propertyName = "RootT.y",
            };

            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.keys == null || curve.keys.Length == 0)
            {
                curve = new AnimationCurve(
                    new Keyframe(0f, HeightOffsetMeters, 0f, 0f),
                    new Keyframe(0.041666668f, HeightOffsetMeters, 0f, 0f));
            }
            else
            {
                var keys = curve.keys;
                for (var i = 0; i < keys.Length; i++)
                {
                    keys[i].value += HeightOffsetMeters;
                    keys[i].inTangent = 0f;
                    keys[i].outTangent = 0f;
                }
                curve.keys = keys;
            }

            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        internal static BlendTree CloneBlendTree(BlendTree source, MotionReplacements replacements, string outputFolder, Dictionary<(string slot, bool is5m), AnimationClip> clipCache = null, bool isSleepBuild = false)
        {
            var nested = new List<BlendTree>();
            clipCache ??= new Dictionary<(string slot, bool is5m), AnimationClip>();
            var root = CloneBlendTreeInMemory(source, replacements, nested);

            if (isSleepBuild && IsOrContainsSleepBlendTree(root))
            {
                DuplicateSleepTreeLeaves(root, outputFolder, clipCache, replacements);
            }

            var clonePath = outputFolder + "/" + GeneratedAssetPrefix + SanitizeFileName(source.name) + ".asset";
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

        // CopySerialized, not Instantiate: Unity asserts on strong child PPtrs, and assigning
        // public properties drops m_NormalizedBlendValues (no setter).
        internal static BlendTree CloneBlendTreeInMemory(BlendTree source, MotionReplacements replacements, List<BlendTree> collected)
        {
            var clone = new BlendTree();
            EditorUtility.CopySerialized(source, clone);
            clone.name = source.name;

            var children = clone.children;
            for (var i = 0; i < children.Length; i++)
            {
                var motion = children[i].motion;
                if (motion is BlendTree childTree)
                {
                    children[i].motion = CloneBlendTreeInMemory(childTree, replacements, collected);
                }
                else if (motion != null && replacements.TryGet(motion.name, out var replacement))
                {
                    children[i].motion = replacement;
                }
            }

            clone.children = children;
            collected.Add(clone);
            return clone;
        }

        private static void BuildIdlePoseMenu(GameObject host, List<StanceBuild> stances, string outputFolder, VRCExpressionsMenu targetMenu)
        {
            var menuStances = stances.Where(stance => stance.HasMenu).ToList();
            if (menuStances.Count == 0)
            {
                return;
            }

            var root = GetOrCreateMenu(outputFolder + "/EasyLocoMainIdlePoses.asset");
            root.controls.Clear();

            foreach (var stance in menuStances)
            {
                var stanceMenu = GetOrCreateMenu(outputFolder + "/EasyLocoIdle" + stance.Key + "Menu.asset");
                stanceMenu.controls.Clear();
                for (var i = 0; i < stance.Entries.Count; i++)
                {
                    var label = string.IsNullOrEmpty(stance.Entries[i].menuName)
                        ? Localized.posePrefix + i
                        : stance.Entries[i].menuName;
                    stanceMenu.controls.Add(MakeToggle(label, stance.ParamName, PoseValue(i, stance.Entries.Count)));
                }
                EditorUtility.SetDirty(stanceMenu);

                root.controls.Add(MakeSubMenu(stance.MenuLabel, stanceMenu));
            }
            EditorUtility.SetDirty(root);

            var entry = GetOrCreateMenu(outputFolder + "/EasyLocoIdlePosesEntry.asset");
            entry.controls.Clear();
            entry.controls.Add(MakeSubMenu(Localized.menuIdlePoses, root));
            EditorUtility.SetDirty(entry);

            EnsureSubMenuInstaller(host, entry, targetMenu);
        }

        // Synced VRChat Floats clamp to -1..1. Raw indices collide (Wide1 and Wide2 both become 1).
        internal static float PoseValue(int index, int count)
        {
            return count <= 1 ? 0f : (float)index / (count - 1);
        }

        private static VRCExpressionsMenu.Control MakeToggle(string name, string parameterName, float value)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameterName },
                value = value,
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            };
        }

        private static VRCExpressionsMenu.Control MakeSubMenu(string name, VRCExpressionsMenu subMenu)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = subMenu,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty },
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            };
        }

        private static VRCExpressionsMenu GetOrCreateMenu(string path)
        {
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
            if (menu == null)
            {
                menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                AssetDatabase.CreateAsset(menu, path);
            }

            if (menu.controls == null)
            {
                menu.controls = new List<VRCExpressionsMenu.Control>();
            }

            return menu;
        }

        private static void EnsureMenuInstaller(GameObject host, VRCExpressionsMenu menu)
        {
            if (menu == null)
            {
                throw new FileNotFoundException("EasyLoco expression menu was not found.", EasyLocoConst.EntryMenuPath);
            }

            var installer = host.GetComponents<ModularAvatarMenuInstaller>()
                .FirstOrDefault(component => component.menuToAppend == menu
                    || (component.menuToAppend == null && component.installTargetMenu == null));

            if (installer == null)
            {
                installer = host.AddComponent<ModularAvatarMenuInstaller>();
            }

            installer.menuToAppend = menu;
            installer.installTargetMenu = null;
            EditorUtility.SetDirty(installer);
        }

        private static void EnsureSubMenuInstaller(GameObject host, VRCExpressionsMenu menuToAppend, VRCExpressionsMenu targetMenu)
        {
            var installer = host.GetComponents<ModularAvatarMenuInstaller>()
                .FirstOrDefault(component => component.menuToAppend == menuToAppend);

            if (installer == null)
            {
                installer = host.AddComponent<ModularAvatarMenuInstaller>();
            }

            installer.menuToAppend = menuToAppend;
            installer.installTargetMenu = targetMenu;
            EditorUtility.SetDirty(installer);
        }

        private static VRCExpressionsMenu.Control CloneControl(VRCExpressionsMenu.Control source)
        {
            return new VRCExpressionsMenu.Control
            {
                name = source.name,
                icon = source.icon,
                type = source.type,
                parameter = source.parameter,
                value = source.value,
                style = source.style,
                subMenu = source.subMenu,
                subParameters = source.subParameters,
                labels = source.labels,
            };
        }

        private static VRCExpressionsMenu LocalizeMenuCopy(string sourcePath, string outputPath, IDictionary<string, string> nameMap, IDictionary<string, VRCExpressionsMenu> subMenuRewires)
        {
            var source = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(sourcePath);
            if (source == null)
            {
                throw new FileNotFoundException("EasyLoco menu was not found.", sourcePath);
            }

            var menu = GetOrCreateMenu(outputPath);
            menu.Parameters = source.Parameters;
            menu.controls = source.controls.Select(control =>
            {
                var clone = CloneControl(control);
                if (nameMap != null && nameMap.TryGetValue(control.name, out var newName))
                {
                    clone.name = newName;
                }
                if (subMenuRewires != null && subMenuRewires.TryGetValue(control.name, out var newSubMenu))
                {
                    clone.subMenu = newSubMenu;
                }
                return clone;
            }).ToList();
            EditorUtility.SetDirty(menu);
            return menu;
        }

        private static VRCExpressionsMenu GetOrCreateLocalizedMainMenu(string outputFolder)
        {
            var actionMenu = LocalizeMenuCopy(
                EasyLocoConst.ActionMenuPath,
                outputFolder + "/EasyLocoActionMenu.asset",
                new Dictionary<string, string>
                {
                    { "Default Standing", Localized.menuDefaultStanding },
                    { "Default Sitting", Localized.menuDefaultSitting },
                },
                null);

            return LocalizeMenuCopy(
                EasyLocoConst.MainMenuPath,
                outputFolder + "/EasyLocoMain.asset",
                new Dictionary<string, string> { { "Action", Localized.menuAction } },
                new Dictionary<string, VRCExpressionsMenu> { { "Action", actionMenu } });
        }

        private static VRCExpressionsMenu GetOrCreateLocalizedEntry(string outputFolder, VRCExpressionsMenu mainMenu)
        {
            return LocalizeMenuCopy(
                EasyLocoConst.EntryMenuPath,
                outputFolder + "/EasyLocoEntry.asset",
                null,
                new Dictionary<string, VRCExpressionsMenu> { { "EasyLoco", mainMenu } });
        }

        private static ModularAvatarParameters GetOrCreateMaParameters(GameObject host)
        {
            var maParameters = host.GetComponent<ModularAvatarParameters>();
            if (maParameters == null)
            {
                maParameters = host.AddComponent<ModularAvatarParameters>();
            }

            if (maParameters.parameters == null)
            {
                maParameters.parameters = new List<ParameterConfig>();
            }

            return maParameters;
        }

        private static void AddMaParameterIfMissing(ModularAvatarParameters maParameters, string name, ParameterSyncType syncType, bool saved, bool localOnly, float defaultValue)
        {
            if (maParameters.parameters.Any(parameter => parameter.nameOrPrefix == name))
            {
                return;
            }

            maParameters.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = name,
                syncType = syncType,
                localOnly = localOnly,
                saved = saved,
                defaultValue = defaultValue,
            });
        }

        private static void EnsureEmoteParameter(GameObject host)
        {
            var maParameters = GetOrCreateMaParameters(host);
            AddMaParameterIfMissing(maParameters, EmoteParameterName, ParameterSyncType.Int, saved: false, localOnly: false, defaultValue: 0);
            EditorUtility.SetDirty(maParameters);
        }

        private static void EnsureSyncedFloatParameter(GameObject host, string name)
        {
            var maParameters = GetOrCreateMaParameters(host);
            AddMaParameterIfMissing(maParameters, name, ParameterSyncType.Float, saved: true, localOnly: false, defaultValue: 0);
            EditorUtility.SetDirty(maParameters);
        }

        private static void EnsureMergeAnimator(GameObject gameObject, VRCAvatarDescriptor.AnimLayerType layerType, RuntimeAnimatorController controller, MergeAnimatorMode mode)
        {
            var mergeAnimator = gameObject.GetComponents<ModularAvatarMergeAnimator>()
                .FirstOrDefault(component => component.layerType == layerType && component.mergeAnimatorMode == mode);

            if (mergeAnimator == null)
            {
                mergeAnimator = gameObject.AddComponent<ModularAvatarMergeAnimator>();
            }

            mergeAnimator.animator = controller;
            mergeAnimator.layerType = layerType;
            mergeAnimator.mergeAnimatorMode = mode;
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
