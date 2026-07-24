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
        private const string TemplateControllerFolder = EasyLocoConst.PackageRoot + "/Animators";
        private const string BaseTemplatePath = TemplateControllerFolder + "/EasyLocoBaseTemplate.controller";
        private const string ActionTemplatePath = TemplateControllerFolder + "/EasyLocoActionTemplate.controller";
        private const string EntryMenuPath = EasyLocoConst.MenusFolder + "/EasyLocoEntry.asset";
        private const string EmoteParameterName = "VRCEmote";
        private const string GeneratedRoot = "Assets/PuetsuaWorkshop/Generated/EasyLoco";

        /// <summary>Per-stance idle build state gathered up front and reused for params + menu.</summary>
        private sealed class StanceBuild
        {
            public readonly string Key;
            public readonly List<EasyLoco.IdlePose> Poses;
            public readonly string IdleTargetName;
            public readonly string ParamName;

            public List<EasyLoco.IdlePose> Entries;
            public Motion Motion;
            public bool HasMenu;

            public StanceBuild(string key, List<EasyLoco.IdlePose> poses, string idleTargetName, string paramName)
            {
                Key = key;
                Poses = poses;
                IdleTargetName = idleTargetName;
                ParamName = paramName;
            }
        }

        /// <summary>
        /// Builds the generated assets, bakes the Modular Avatar setup into a prefab, and installs an
        /// instance of that prefab onto the avatar. Returns the prefab's asset path. The prefab is
        /// also reusable by hand - dropping it under another avatar installs the same setup there.
        /// </summary>
        public static string Build(EasyLoco easyLoco)
        {
            if (easyLoco == null)
            {
                return null;
            }

            var avatar = easyLoco.Avatar;
            if (avatar == null)
            {
                throw new System.InvalidOperationException("EasyLoco must be placed on an avatar with a VRCAvatarDescriptor.");
            }

            var outputFolder = GetOutputFolder(avatar);
            EnsureFolder(outputFolder);

            // Built detached from the hierarchy, so a build that throws part way through cannot
            // leave a half-configured object parented to the avatar.
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

            InstallPrefabInstance(easyLoco, prefabPath);
            return prefabPath;
        }

        // Re-instancing on every build would discard the user's placement of an existing instance for
        // no reason: saving the prefab already updated it in place. Anything else living under the
        // expected name - a plain object from an older build, or an instance built for a different
        // avatar - is replaced.
        private static void InstallPrefabInstance(EasyLoco easyLoco, string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException("EasyLoco prefab was not found after building.", prefabPath);
            }

            var existing = easyLoco.transform.Find(EasyLocoConst.GeneratedObjectName);
            if (existing != null)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) == prefab)
                {
                    return;
                }

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, easyLoco.transform);
            instance.name = EasyLocoConst.GeneratedObjectName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(instance, "Build EasyLoco Modular Avatar");
        }

        private static string BuildHost(EasyLoco easyLoco, GameObject host, string outputFolder)
        {
            var stances = new List<StanceBuild>
            {
                new StanceBuild("Stand", easyLoco.standPoses, EasyLocoConst.StandIdleTarget, EasyLocoConst.IdleStandParam),
                new StanceBuild("Crouch", easyLoco.crouchPoses, EasyLocoConst.CrouchIdleTarget, EasyLocoConst.IdleCrouchParam),
                new StanceBuild("Prone", easyLoco.pronePoses, EasyLocoConst.ProneIdleTarget, EasyLocoConst.IdleProneParam),
            };

            // Build each stance's idle motion: null (keep built-in), a single override clip, or a
            // selector blend tree when more than one pose is registered.
            var baseReplacements = new Dictionary<string, Motion>();
            foreach (var stance in stances)
            {
                BuildIdleSelector(stance, outputFolder);
                if (stance.Motion != null)
                {
                    baseReplacements[stance.IdleTargetName] = stance.Motion;
                }
            }

            // Sleep clips live at the leaves of the nested sleeping trees. Registering them here
            // lets ReplaceMotion's existing clone path rebuild DefaultProneSleeping for this avatar.
            AddSleepReplacements(baseReplacements, easyLoco.sleep);

            // Always generate copies of both templates so the avatar merges the generated
            // controllers, never the shared template assets (which a user could edit by accident).
            var baseController = (AnimatorController)BuildController(BaseTemplatePath, outputFolder, "EasyLocoBase.controller", baseReplacements);
            foreach (var stance in stances)
            {
                if (stance.HasMenu)
                {
                    EnsureFloatParameter(baseController, stance.ParamName);
                }
            }
            EditorUtility.SetDirty(baseController);
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Base, baseController);

            var actionController = (AnimatorController)BuildController(ActionTemplatePath, outputFolder, "EasyLocoAction.controller", new Dictionary<string, Motion>());
            ApplyAfkOverrides(actionController, easyLoco);
            EditorUtility.SetDirty(actionController);
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Action, actionController);

            // The Action layer is driven by VRCEmote, exposed through the EasyLoco expression menu.
            EnsureEmoteParameter(host);
            EnsureSleepModeParameter(host);
            EnsureFeetLockParameter(host);
            EnsureSleepSensors(host);
            EnsureMenuInstaller(host);

            // Idle-pose selection menu + its synced parameters (only for stances with >1 pose).
            BuildIdlePoseMenu(host, stances, outputFolder);
            foreach (var stance in stances)
            {
                if (stance.HasMenu)
                {
                    EnsureSyncedFloatParameter(host, stance.ParamName);
                }
            }

            var prefabPath = outputFolder + "/" + EasyLocoConst.GeneratedObjectName + ".prefab";
            // Overwrites in place so the asset GUID survives. Avatars already holding an instance of
            // this prefab pick the rebuild up automatically instead of losing the reference.
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

        private static void BuildIdleSelector(StanceBuild stance, string outputFolder)
        {
            stance.Entries = (stance.Poses ?? new List<EasyLoco.IdlePose>())
                .Where(pose => pose != null && pose.clip != null)
                .ToList();

            if (stance.Entries.Count == 0)
            {
                stance.Motion = null; // keep the template's built-in idle
                stance.HasMenu = false;
                return;
            }

            if (stance.Entries.Count == 1)
            {
                stance.Motion = stance.Entries[0].clip; // single override, no selector/menu
                stance.HasMenu = false;
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
                    threshold = i,
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
            stance.HasMenu = true;
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

        // A clip equal to the built-in is skipped: registering it would force a needless clone of
        // the whole sleeping tree for an avatar that never customised anything.
        private static void AddSleepReplacements(IDictionary<string, Motion> replacements, EasyLoco.SleepSet sleep)
        {
            if (sleep == null)
            {
                return;
            }

            AddSleepReplacement(replacements, EasyLocoConst.SleepUpTarget, EasyLocoConst.SleepUpClip, sleep.up);
            AddSleepReplacement(replacements, EasyLocoConst.SleepDownTarget, EasyLocoConst.SleepDownClip, sleep.down);
            AddSleepReplacement(replacements, EasyLocoConst.SleepSideTarget, EasyLocoConst.SleepSideClip, sleep.side);
        }

        private static void AddSleepReplacement(IDictionary<string, Motion> replacements, string targetName, string builtInPath, AnimationClip clip)
        {
            if (clip == null)
            {
                return;
            }

            var builtIn = AssetDatabase.LoadAssetAtPath<AnimationClip>(builtInPath);
            if (clip == builtIn)
            {
                return;
            }

            replacements[targetName] = clip;
        }

        private static void ApplyAfkOverrides(AnimatorController controller, EasyLoco easyLoco)
        {
            var overrides = new Dictionary<string, AnimationClip>();
            AddAfkOverrides(overrides, "Stand", easyLoco.standAfk);
            AddAfkOverrides(overrides, "Crouch", easyLoco.crouchAfk);
            AddAfkOverrides(overrides, "Prone", easyLoco.proneAfk);
            if (overrides.Count == 0)
            {
                return;
            }

            foreach (var layer in controller.layers)
            {
                SetStateMotionsByName(layer.stateMachine, overrides);
            }
        }

        private static void AddAfkOverrides(IDictionary<string, AnimationClip> map, string stance, EasyLoco.AfkSet set)
        {
            if (set == null)
            {
                return;
            }

            AddClipOverride(map, EasyLocoConst.AfkStatePrefix + stance + " Entering", set.entering);
            AddClipOverride(map, EasyLocoConst.AfkStatePrefix + stance + " Looping", set.looping);
            AddClipOverride(map, EasyLocoConst.AfkStatePrefix + stance + " Exiting", set.exiting);
        }

        private static void AddClipOverride(IDictionary<string, AnimationClip> map, string stateName, AnimationClip clip)
        {
            if (clip != null)
            {
                map[stateName] = clip;
            }
        }

        private static void SetStateMotionsByName(AnimatorStateMachine stateMachine, IReadOnlyDictionary<string, AnimationClip> overrides)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (overrides.TryGetValue(state.name, out var clip) && state.motion != clip)
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

        // Must be Float, not Int: the idle selectors are Simple1D blend trees, and Unity blend trees
        // read their blend parameter as a float. An Int parameter's float value is always 0
        // (int/float storage is separate), so an Int here would freeze every selector on child 0 and
        // the menu toggles would appear to do nothing.
        private static void EnsureFloatParameter(AnimatorController controller, string name)
        {
            if (controller.parameters.Any(parameter => parameter.name == name))
            {
                return;
            }

            controller.AddParameter(name, AnimatorControllerParameterType.Float);
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

        private static void BuildIdlePoseMenu(GameObject host, List<StanceBuild> stances, string outputFolder)
        {
            var mainMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(EasyLocoConst.MainMenuPath);
            if (mainMenu == null)
            {
                throw new FileNotFoundException("EasyLoco main menu was not found.", EasyLocoConst.MainMenuPath);
            }

            var menuStances = stances.Where(stance => stance.HasMenu).ToList();
            if (menuStances.Count == 0)
            {
                RemoveIdleMenuInstaller(host, mainMenu);
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
                    var label = string.IsNullOrEmpty(stance.Entries[i].menuName) ? "Pose " + i : stance.Entries[i].menuName;
                    stanceMenu.controls.Add(MakeToggle(label, stance.ParamName, i));
                }
                EditorUtility.SetDirty(stanceMenu);

                root.controls.Add(MakeSubMenu(stance.Key, stanceMenu));
            }
            EditorUtility.SetDirty(root);

            var entry = GetOrCreateMenu(outputFolder + "/EasyLocoIdlePosesEntry.asset");
            entry.controls.Clear();
            entry.controls.Add(MakeSubMenu("Idle Poses", root));
            EditorUtility.SetDirty(entry);

            EnsureIdleMenuInstaller(host, entry, mainMenu);
        }

        private static VRCExpressionsMenu.Control MakeToggle(string name, string parameterName, int value)
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

        private static void EnsureMenuInstaller(GameObject host)
        {
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(EntryMenuPath);
            if (menu == null)
            {
                throw new FileNotFoundException("EasyLoco expression menu was not found.", EntryMenuPath);
            }

            var installer = host.GetComponents<ModularAvatarMenuInstaller>()
                .FirstOrDefault(component => component.menuToAppend == menu
                    || (component.menuToAppend == null && component.installTargetMenu == null));

            if (installer == null)
            {
                installer = host.AddComponent<ModularAvatarMenuInstaller>();
            }

            installer.menuToAppend = menu;
            installer.installTargetMenu = null; // append to the avatar's root expression menu
            EditorUtility.SetDirty(installer);
        }

        private static void EnsureIdleMenuInstaller(GameObject host, VRCExpressionsMenu menuToAppend, VRCExpressionsMenu targetMenu)
        {
            var installer = host.GetComponents<ModularAvatarMenuInstaller>()
                .FirstOrDefault(component => component.installTargetMenu == targetMenu);

            if (installer == null)
            {
                installer = host.AddComponent<ModularAvatarMenuInstaller>();
            }

            installer.menuToAppend = menuToAppend;
            installer.installTargetMenu = targetMenu; // nest the idle poses under EasyLocoMain
            EditorUtility.SetDirty(installer);
        }

        private static void RemoveIdleMenuInstaller(GameObject host, VRCExpressionsMenu targetMenu)
        {
            var installer = host.GetComponents<ModularAvatarMenuInstaller>()
                .FirstOrDefault(component => component.installTargetMenu == targetMenu);
            if (installer != null)
            {
                Object.DestroyImmediate(installer);
            }
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

        // Sleep mode is synced so remote viewers see the sleeping pose, but deliberately not saved:
        // an avatar should never load back in already asleep.
        private static void EnsureSleepModeParameter(GameObject host)
        {
            var maParameters = GetOrCreateMaParameters(host);
            AddMaParameterIfMissing(maParameters, EasyLocoConst.SleepModeParam, ParameterSyncType.Bool, saved: false, localOnly: false, defaultValue: 0);
            EditorUtility.SetDirty(maParameters);
        }

        // Feet lock is synced so remote viewers see the locked feet, but not saved: the FeetLock
        // layer's parameter driver clears it whenever the avatar is upright, so it should always load
        // back in the unlocked (standing) state.
        private static void EnsureFeetLockParameter(GameObject host)
        {
            var maParameters = GetOrCreateMaParameters(host);
            AddMaParameterIfMissing(maParameters, EasyLocoConst.FeetLockParam, ParameterSyncType.Bool, saved: false, localOnly: false, defaultValue: 0);
            EditorUtility.SetDirty(maParameters);
        }

        // Float to match the animator parameter the idle blend trees read (see EnsureFloatParameter).
        private static void EnsureSyncedFloatParameter(GameObject host, string name)
        {
            var maParameters = GetOrCreateMaParameters(host);
            AddMaParameterIfMissing(maParameters, name, ParameterSyncType.Float, saved: true, localOnly: false, defaultValue: 0);
            EditorUtility.SetDirty(maParameters);
        }

        // The sleep sensors are an authored prefab rather than something we build from code, so the
        // radii and offsets stay hand-tunable. Kept as a live prefab instance so package-side
        // tweaks reach avatars that were built earlier.
        private static void EnsureSleepSensors(GameObject host)
        {
            var existing = host.transform.Find(EasyLocoConst.SleepSensorsObjectName);
            if (existing != null)
            {
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EasyLocoConst.SleepSensorsPrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException("EasyLoco sleep sensor prefab was not found.", EasyLocoConst.SleepSensorsPrefabPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = EasyLocoConst.SleepSensorsObjectName;
            instance.transform.SetParent(host.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }


        private static void EnsureMergeAnimator(GameObject gameObject, VRCAvatarDescriptor.AnimLayerType layerType, RuntimeAnimatorController controller)
        {
            var mergeAnimator = gameObject.GetComponents<ModularAvatarMergeAnimator>()
                .FirstOrDefault(component => component.layerType == layerType && component.mergeAnimatorMode == MergeAnimatorMode.Replace);

            if (mergeAnimator == null)
            {
                mergeAnimator = gameObject.AddComponent<ModularAvatarMergeAnimator>();
            }

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
