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

        // Sleeping lives in its own controller layered over the base one rather than inside it, so
        // switching the feature off is just "do not merge this" - the base locomotion is untouched
        // either way. Its states play an empty clip whenever the avatar is not asleep, letting the
        // base layer show through.
        private const string SleepTemplatePath = TemplateControllerFolder + "/EasyLocoSleepTemplate.controller";
        private const string EntryMenuPath = EasyLocoConst.MenusFolder + "/EasyLocoEntry.asset";
        private const string EmoteParameterName = "VRCEmote";
        private const string GeneratedRoot = "Assets/PuetsuaWorkshop/Generated/EasyLoco";

        // Prefixes every asset the build writes. CreateAsset renames the object after its file, so a
        // generated copy is called "<prefix><template name>" - anything matching a generated asset
        // by its template's name has to strip this first.
        private const string GeneratedAssetPrefix = "EasyLoco";

        private static LocalizedTextDataset Localized => LocalizedTextDataset.primary;

        /// <summary>Per-stance idle build state gathered up front and reused for params + menu.</summary>
        private sealed class StanceBuild
        {
            // Key names generated assets and blend trees, so it stays ASCII and language-independent
            // - rebuilding under another language must not orphan the previous run's files.
            // MenuLabel is the localized text the player reads in the expression menu.
            public readonly string Key;
            public readonly string MenuLabel;
            public readonly List<EasyLoco.IdlePose> Poses;
            public readonly string IdleTargetName;
            public readonly string ParamName;

            public List<EasyLoco.IdlePose> Entries;
            public Motion Motion;
            public bool HasMenu;

            public StanceBuild(string key, string menuLabel, List<EasyLoco.IdlePose> poses, string idleTargetName, string paramName)
            {
                Key = key;
                MenuLabel = menuLabel;
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
                throw new System.InvalidOperationException("EasyLoco must be on the same GameObject as the VRCAvatarDescriptor.");
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

            InstallPrefabInstance(easyLoco.transform, EasyLocoConst.GeneratedObjectName, prefabPath, "Build EasyLoco Modular Avatar");
            return prefabPath;
        }

        /// <summary>
        /// Builds the sleeping locomotion - the generated controller carrying whatever clips the
        /// component overrides, and the prefab that appends it over the avatar's base layer - and
        /// puts an instance on the avatar next to the descriptor. Returns the prefab's asset path.
        ///
        /// Deliberately separate from <see cref="Build"/>: sleeping is the one part that installs as
        /// a self-contained prefab, so it is appended on demand rather than baked into every build.
        /// The prefab can equally be dragged onto another avatar.
        /// </summary>
        public static string BuildSleepLocomotion(EasyLoco easyLoco)
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

            // One condition decides both where the object goes and where its menu entry goes, which
            // is what keeps the two consistent: inside the host means the EasyLoco menu is installed
            // and the Sleep entry can nest under it; loose on the avatar means it is not, and the
            // entry has to go to the root menu instead. Installed against a target that is not in
            // the avatar's menu, Modular Avatar drops the installer without a word and sleeping ends
            // up with no toggles at all.
            var host = easyLoco.transform.Find(EasyLocoConst.GeneratedObjectName);
            var parent = host != null ? host : easyLoco.transform;

            var controller = BuildSleepController(easyLoco, outputFolder);
            var prefabPath = BuildSleepPrefab(controller, outputFolder, nestUnderMainMenu: host != null);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Running the main build after installing sleeping moves where it belongs, so a copy
            // left in the other place has to go or the avatar would carry two.
            var misplaced = FindSleepLocomotion(easyLoco);
            if (misplaced != null && misplaced.parent != parent)
            {
                Undo.DestroyObjectImmediate(misplaced.gameObject);
            }

            InstallPrefabInstance(parent, EasyLocoConst.SleepObjectName, prefabPath, "Build EasyLoco Sleep Locomotion");
            return prefabPath;
        }

        // Looked for in both places: sleeping sits inside the generated host when there is one and
        // beside the descriptor when there is not, and the host can appear or disappear between
        // installing sleeping and taking it off again.
        private static Transform FindSleepLocomotion(EasyLoco easyLoco)
        {
            var host = easyLoco.transform.Find(EasyLocoConst.GeneratedObjectName);
            var nested = host != null ? host.Find(EasyLocoConst.SleepObjectName) : null;
            return nested != null ? nested : easyLoco.transform.Find(EasyLocoConst.SleepObjectName);
        }

        /// <summary>Whether the sleeping module is currently installed on this avatar.</summary>
        public static bool HasSleepLocomotion(EasyLoco easyLoco)
        {
            return easyLoco != null && FindSleepLocomotion(easyLoco) != null;
        }

        /// <summary>
        /// Takes the sleeping module back off the avatar. The generated assets stay where they are:
        /// the folder is disposable, nothing else points at them, and leaving them means putting
        /// sleeping back costs no rebuild if the clips have not changed.
        /// </summary>
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

        // Re-instancing on every build would discard the user's placement of an existing instance for
        // no reason: saving the prefab already updated it in place. Anything else living under the
        // expected name - a plain object from an older build, or an instance built for a different
        // avatar - is replaced.
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

            // Always generate copies of the templates so the avatar merges the generated
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
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Base, baseController, MergeAnimatorMode.Replace);

            var actionController = (AnimatorController)BuildController(ActionTemplatePath, outputFolder, "EasyLocoAction.controller", new Dictionary<string, Motion>());
            ApplyAfkOverrides(actionController, easyLoco);
            EditorUtility.SetDirty(actionController);
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Action, actionController, MergeAnimatorMode.Replace);

            // The Action layer is driven by VRCEmote, exposed through the EasyLoco expression menu.
            EnsureEmoteParameter(host);

            // Localised copies of the EasyLoco entry/main/action menus. The shared assets carry the
            // in-game labels in English, so cloning them here lets "Action", "Default Standing" and
            // "Default Sitting" follow the active language at build time. The entry menu is what the
            // host appends to the avatar's root menu; the main menu is where the idle poses nest.
            var mainMenu = GetOrCreateLocalizedMainMenu(outputFolder);
            var entryMenu = GetOrCreateLocalizedEntry(outputFolder, mainMenu);
            EnsureMenuInstaller(host, entryMenu);

            // Idle-pose selection menu + its synced parameters (only for stances with >1 pose).
            BuildIdlePoseMenu(host, stances, outputFolder, mainMenu);
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

        // Sleep clips live at the leaves of the DefaultSleepingFacing* trees, one per sleeping state.
        // Registering them here lets ReplaceMotion's existing clone path rebuild those trees for this
        // avatar, so the generated controller carries whatever the component overrides.
        private static AnimatorController BuildSleepController(EasyLoco easyLoco, string outputFolder)
        {
            var replacements = new Dictionary<string, Motion>();
            AddSleepReplacements(replacements, easyLoco.sleep, outputFolder);

            var controller = (AnimatorController)BuildController(SleepTemplatePath, outputFolder, "EasyLocoSleep.controller", replacements);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // The package prefab already carries everything about sleeping that does not depend on the
        // avatar - the contact rig, the two toggles' parameters, and the Sleep sub-menu installer.
        // Only the merged animator is avatar-specific, so the per-avatar copy is that prefab with
        // its animator reference repointed at the generated controller.
        //
        // Built detached and saved before it is instantiated, the same as the outer host: a save
        // that throws cannot leave a half-configured object under the avatar.
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

                // Appended, not Replace: the base locomotion controller already claimed the Base
                // layer, and these layers only override it while the avatar is actually asleep.
                EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Base, controller, MergeAnimatorMode.Append);

                var installer = host.GetComponent<ModularAvatarMenuInstaller>();
                if (installer != null)
                {
                    // Localised Sleep menu + entry, so "Sleep", "Sleep Loco" and "Feet Lock" follow the
                    // active language at build time. Nested under the localised EasyLocoMain when the
                    // main prefab is present, otherwise appended at the root.
                    var sleepMenu = GetOrCreateLocalizedSleep(outputFolder);
                    installer.menuToAppend = GetOrCreateLocalizedSleepEntry(outputFolder, sleepMenu);
                    installer.installTargetMenu = nestUnderMainMenu ? GetOrCreateLocalizedMainMenu(outputFolder) : null;
                    EditorUtility.SetDirty(installer);
                }

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
        //
        // The on-side pose fans out to one placeholder per tree. Left alone, the placeholders play
        // as authored and nothing is generated. Overridden, each placeholder is replaced by a copy
        // of the user's pose wearing that placeholder's root-transform settings, so the slot keeps
        // its yaw without the builder knowing what any slot's yaw is.
        private static void AddSleepReplacements(IDictionary<string, Motion> replacements, EasyLoco.SleepSet sleep, string outputFolder)
        {
            AddSleepReplacement(replacements, EasyLocoConst.SleepUpTarget, EasyLocoConst.SleepUpClip, sleep?.up);
            AddSleepReplacement(replacements, EasyLocoConst.SleepDownTarget, EasyLocoConst.SleepDownClip, sleep?.down);

            var sideClip = sleep?.side;
            if (sideClip == null || sideClip == AssetDatabase.LoadAssetAtPath<AnimationClip>(EasyLocoConst.SleepSideClip))
            {
                return;
            }

            foreach (var target in EasyLocoConst.SleepSideTargets)
            {
                var placeholder = AssetDatabase.LoadAssetAtPath<AnimationClip>(EasyLocoConst.SleepSidePlaceholderClip(target));
                if (placeholder == null || placeholder == sideClip)
                {
                    continue;
                }

                replacements[target] = CreateSideClipForSlot(sideClip, placeholder, outputFolder);
            }
        }

        // The user's pose, wearing the placeholder's root-transform settings. Only that group is
        // taken across: it is what makes a slot a slot (its yaw above all), while loop and additive
        // settings belong to whoever authored the pose.
        private static AnimationClip CreateSideClipForSlot(AnimationClip sideClip, AnimationClip placeholder, string outputFolder)
        {
            var slot = AnimationUtility.GetAnimationClipSettings(placeholder);
            var clip = Object.Instantiate(sideClip);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.orientationOffsetY = slot.orientationOffsetY;
            settings.level = slot.level;
            settings.cycleOffset = slot.cycleOffset;
            settings.keepOriginalOrientation = slot.keepOriginalOrientation;
            settings.keepOriginalPositionY = slot.keepOriginalPositionY;
            settings.keepOriginalPositionXZ = slot.keepOriginalPositionXZ;
            settings.heightFromFeet = slot.heightFromFeet;
            settings.mirror = slot.mirror;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // Named after the slot, not the pose, so rebuilding overwrites in place and two slots
            // sharing a yaw still get one asset each - no clone can delete another's file.
            clip.name = placeholder.name;
            var path = outputFolder + "/" + GeneratedAssetPrefix + SanitizeFileName(placeholder.name) + ".anim";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(clip, path);
            EditorUtility.SetDirty(clip);
            return clip;
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
            var clones = new Dictionary<BlendTree, BlendTree>();
            foreach (var layer in controller.layers)
            {
                ReplaceMotions(layer.stateMachine, replacements, outputFolder, controllerPath, clones);
            }
        }

        // The clone cache spans the whole controller: nothing stops one shared tree from being the
        // motion of several states, and cloning it per state would have each clone delete the asset
        // the previous state was pointed at, leaving that state with a missing motion. The clone
        // path is deterministic, so the cache is what keeps that safe.
        private static void ReplaceMotions(AnimatorStateMachine stateMachine, IReadOnlyDictionary<string, Motion> replacements, string outputFolder, string controllerPath, Dictionary<BlendTree, BlendTree> clones)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                var replaced = ReplaceMotion(state.motion, replacements, outputFolder, controllerPath, clones);
                if (replaced != state.motion)
                {
                    state.motion = replaced;
                    EditorUtility.SetDirty(state);
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                ReplaceMotions(childStateMachine.stateMachine, replacements, outputFolder, controllerPath, clones);
            }
        }

        private static Motion ReplaceMotion(Motion motion, IReadOnlyDictionary<string, Motion> replacements, string outputFolder, string controllerPath, Dictionary<BlendTree, BlendTree> clones)
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
                    if (!clones.TryGetValue(blendTree, out var clone))
                    {
                        clone = CloneBlendTree(blendTree, replacements, outputFolder);
                        clones.Add(blendTree, clone);
                    }

                    return clone;
                }

                // Blend trees embedded inside the copied controller are owned by it and safe to edit.
                ReplaceBlendTreeMotionsInPlace(blendTree, replacements, outputFolder, controllerPath, clones);
                return blendTree;
            }

            return replacements.TryGetValue(motion.name, out var replacement) ? replacement : motion;
        }

        private static void ReplaceBlendTreeMotionsInPlace(BlendTree blendTree, IReadOnlyDictionary<string, Motion> replacements, string outputFolder, string controllerPath, Dictionary<BlendTree, BlendTree> clones)
        {
            var children = blendTree.children;
            var changed = false;

            for (var i = 0; i < children.Length; i++)
            {
                var original = children[i].motion;
                var replaced = ReplaceMotion(original, replacements, outputFolder, controllerPath, clones);
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

        // Built field by field rather than with Object.Instantiate: a blend tree's child motions are
        // serialized as strong pointers (that is how a tree owns its sub-trees), and Unity's clone
        // path asserts on every one of them - "(metaFlags & kStrongPPtrMask) == 0", once per child,
        // dozens of lines per build. Instantiate also deep-copies the sub-trees itself, which the
        // recursion below would then copy a second time and leak the first set.
        private static BlendTree CloneBlendTreeInMemory(BlendTree source, IReadOnlyDictionary<string, Motion> replacements, List<BlendTree> collected)
        {
            var clone = new BlendTree
            {
                name = source.name,
                blendType = source.blendType,
                blendParameter = source.blendParameter,
                blendParameterY = source.blendParameterY,
                // Off while the children are assigned so Unity keeps the thresholds copied below
                // instead of redistributing them evenly; the source's own setting is restored after.
                useAutomaticThresholds = false,
                minThreshold = source.minThreshold,
                maxThreshold = source.maxThreshold,
            };

            var children = source.children;
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
            clone.useAutomaticThresholds = source.useAutomaticThresholds;
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

            EnsureSubMenuInstaller(host, entry, targetMenu); // nest the idle poses under the localised EasyLocoMain
        }

        // A synced VRChat Float only carries -1..1, so pose N cannot be selected by its raw index:
        // anything above 1 clamps down to 1. With three stand poses that made "Wide2" land on the
        // same value as "Wide1", so the menu drew Wide1 as already active and the next click on it
        // read as switching it off - back to the default pose. Spreading the poses evenly across
        // 0..1 keeps every selection inside the syncable range and distinct from its neighbours.
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

        // Matched on what it appends, not on its target: EasyLocoMain is the target of more than one
        // sub-menu (the Sleep one, from the sleep prefab), so the target alone does not identify an
        // installer.
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

        // Deep-enough clone of a menu control: a new Control instance so renaming it or rewiring its
        // subMenu never touches the shared package asset it was copied from. The nested parameter and
        // arrays are reused by reference - nothing here mutates them.
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

        // Builds a per-avatar copy of a shared menu asset with control names translated to the active
        // language and selected subMenu references rewired to other generated copies. The shared menus
        // carry the in-game labels, so localising them means cloning rather than editing in place -
        // mutating a shared asset would change it for every avatar. The Parameters reference is
        // carried across so the copy still validates against the same expression-parameter set.
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

        // The localised EasyLocoMain + Action menus. Built once per avatar in its generated folder and
        // shared by the main build (which appends the idle-pose entry under it) and the sleep build
        // (which nests the Sleep entry under it when the main prefab is present). "Action",
        // "Default Standing" and "Default Sitting" follow the active language at build time; the emote
        // submenus under them stay shared and untranslated, matching VRChat's emote names.
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

        // The localised top entry: the "EasyLoco" control (product name, kept as-is, icon preserved by
        // cloning) rewired to point at the localised main menu.
        private static VRCExpressionsMenu GetOrCreateLocalizedEntry(string outputFolder, VRCExpressionsMenu mainMenu)
        {
            return LocalizeMenuCopy(
                EntryMenuPath,
                outputFolder + "/EasyLocoEntry.asset",
                null,
                new Dictionary<string, VRCExpressionsMenu> { { "EasyLoco", mainMenu } });
        }

        // The localised Sleep menu: "Sleep Loco" and "Feet Lock" toggles follow the active language.
        private static VRCExpressionsMenu GetOrCreateLocalizedSleep(string outputFolder)
        {
            return LocalizeMenuCopy(
                EasyLocoConst.SleepMenuPath,
                outputFolder + "/EasyLocoSleep.asset",
                new Dictionary<string, string>
                {
                    { "Sleep Loco", Localized.menuSleepLoco },
                    { "Feet Lock", Localized.menuFeetLock },
                },
                null);
        }

        // The localised Sleep entry: "Sleep" follows the active language and points at the localised
        // Sleep menu.
        private static VRCExpressionsMenu GetOrCreateLocalizedSleepEntry(string outputFolder, VRCExpressionsMenu sleepMenu)
        {
            return LocalizeMenuCopy(
                EasyLocoConst.SleepEntryMenuPath,
                outputFolder + "/EasyLocoSleepEntry.asset",
                new Dictionary<string, string> { { "Sleep", Localized.menuSleep } },
                new Dictionary<string, VRCExpressionsMenu> { { "Sleep", sleepMenu } });
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

        // Float to match the animator parameter the idle blend trees read (see EnsureFloatParameter).
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
