using System.Collections.Generic;
using System.Globalization;
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

            BuildSleep(easyLoco, host, outputFolder);

            var actionController = (AnimatorController)BuildController(ActionTemplatePath, outputFolder, "EasyLocoAction.controller", new Dictionary<string, Motion>());
            ApplyAfkOverrides(actionController, easyLoco);
            EditorUtility.SetDirty(actionController);
            EnsureMergeAnimator(host, VRCAvatarDescriptor.AnimLayerType.Action, actionController, MergeAnimatorMode.Replace);

            // The Action layer is driven by VRCEmote, exposed through the EasyLoco expression menu.
            EnsureEmoteParameter(host);
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

        // Sleeping installs as one nested prefab rather than as loose components on the host, so it
        // stays a unit the user can drag onto another avatar by itself. Switching the feature off is
        // then a single early return - the host is rebuilt from scratch on every build, so an avatar
        // that had sleeping and turns it off simply comes back without the nested object.
        private static void BuildSleep(EasyLoco easyLoco, GameObject host, string outputFolder)
        {
            if (easyLoco.sleep == null || !easyLoco.sleep.enabled)
            {
                return;
            }

            // Sleep clips live at the leaves of the DefaultSleepingFacing* trees, one per sleeping
            // state. Registering them here lets ReplaceMotion's existing clone path rebuild those
            // trees for this avatar.
            var replacements = new Dictionary<string, Motion>();
            var sideClip = AddSleepReplacements(replacements, easyLoco.sleep);

            var controller = (AnimatorController)BuildController(SleepTemplatePath, outputFolder, "EasyLocoSleep.controller", replacements);
            ApplySleepSideOffsets(controller, sideClip, outputFolder);
            EditorUtility.SetDirty(controller);

            var prefabPath = BuildSleepPrefab(controller, outputFolder);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException("EasyLoco sleep prefab was not found after building.", prefabPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, host.transform);
            instance.name = EasyLocoConst.SleepObjectName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }

        // The package prefab already carries everything about sleeping that does not depend on the
        // avatar - the contact rig, the two toggles' parameters, and the Sleep sub-menu installer.
        // Only the merged animator is avatar-specific, so the per-avatar copy is that prefab with
        // its animator reference repointed at the generated controller.
        //
        // Built detached and saved before it is instantiated, the same as the outer host: a save
        // that throws cannot leave a half-configured object under the avatar.
        private static string BuildSleepPrefab(AnimatorController controller, string outputFolder)
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
        // The on-side pose is the exception and is always registered, because every sleeping tree
        // needs its own yawed copy of it (see ApplySleepSideOffsets) and that rewrite may only touch
        // clones living in the avatar's folder - never the shared package trees. Returns the side
        // clip actually in effect, built-in or override.
        private static AnimationClip AddSleepReplacements(IDictionary<string, Motion> replacements, EasyLoco.SleepSet sleep)
        {
            AddSleepReplacement(replacements, EasyLocoConst.SleepUpTarget, EasyLocoConst.SleepUpClip, sleep?.up);
            AddSleepReplacement(replacements, EasyLocoConst.SleepDownTarget, EasyLocoConst.SleepDownClip, sleep?.down);

            var sideClip = sleep?.side != null ? sleep.side : AssetDatabase.LoadAssetAtPath<AnimationClip>(EasyLocoConst.SleepSideClip);
            if (sideClip != null)
            {
                replacements[EasyLocoConst.SleepSideTarget] = sideClip;
            }

            return sideClip;
        }

        // Root Transform Rotation offset the on-side pose needs in each sleeping tree. The pose is
        // the same in all four - only its yaw differs - so one authored clip covers every case and
        // the copies are generated here instead of shipped.
        //
        // The free branch sits halfway between its two facing clips (SleepUp at 0, SleepDown at
        // -180). The feet-locked branch instead matches its facing clip exactly, because Feet Lock
        // puts both feet on Animation and they have to land where that facing pose puts them.
        private static readonly Dictionary<string, float> SleepSideOrientationOffsets = new Dictionary<string, float>
        {
            { EasyLocoConst.SleepFacingUpTree, -90f },
            { EasyLocoConst.SleepFacingDownTree, -90f },
            { EasyLocoConst.SleepFacingUpFeetLockTree, 0f },
            { EasyLocoConst.SleepFacingDownFeetLockTree, 180f },
        };

        private static void ApplySleepSideOffsets(AnimatorController controller, AnimationClip sideClip, string outputFolder)
        {
            if (controller == null || sideClip == null)
            {
                return;
            }

            var oriented = new Dictionary<float, AnimationClip>();
            foreach (var layer in controller.layers)
            {
                ApplySleepSideOffsets(layer.stateMachine, sideClip, outputFolder, oriented);
            }
        }

        private static void ApplySleepSideOffsets(AnimatorStateMachine stateMachine, AnimationClip sideClip, string outputFolder, Dictionary<float, AnimationClip> oriented)
        {
            foreach (var childState in stateMachine.states)
            {
                ApplySleepSideOffsets(childState.state.motion as BlendTree, sideClip, outputFolder, oriented);
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                ApplySleepSideOffsets(childStateMachine.stateMachine, sideClip, outputFolder, oriented);
            }
        }

        private static void ApplySleepSideOffsets(BlendTree tree, AnimationClip sideClip, string outputFolder, Dictionary<float, AnimationClip> oriented)
        {
            // By this point the tree is a clone, and CreateAsset renamed it after its file - so the
            // name carries the generated prefix and will not match the template name as-is.
            if (tree == null || !SleepSideOrientationOffsets.TryGetValue(StripGeneratedPrefix(tree.name), out var offset))
            {
                return;
            }

            // The clone pass should have brought this tree into the avatar's folder. If it did not,
            // the tree is still the shared package asset and must be left alone.
            var treePath = AssetDatabase.GetAssetPath(tree);
            if (!string.IsNullOrEmpty(treePath) && !treePath.StartsWith(outputFolder))
            {
                return;
            }

            var clip = GetOrientedSideClip(sideClip, offset, outputFolder, oriented);
            var children = tree.children;
            var changed = false;

            for (var i = 0; i < children.Length; i++)
            {
                // The child at the origin is the facing pose, not the on-side one.
                var position = children[i].position;
                if (position == Vector2.zero || children[i].motion == clip)
                {
                    continue;
                }

                children[i].motion = clip;
                changed = true;
            }

            if (changed)
            {
                tree.children = children;
                EditorUtility.SetDirty(tree);
            }
        }

        // Yawing a humanoid clip is a clip setting, not a curve edit, so the copy differs from the
        // source only by orientationOffsetY. Cached per offset: the four trees need three distinct
        // yaws between them, and the deterministic path means re-creating one would delete the asset
        // an earlier tree was pointed at.
        private static AnimationClip GetOrientedSideClip(AnimationClip sideClip, float offset, string outputFolder, Dictionary<float, AnimationClip> oriented)
        {
            if (oriented.TryGetValue(offset, out var cached))
            {
                return cached;
            }

            var sourceSettings = AnimationUtility.GetAnimationClipSettings(sideClip);
            if (Mathf.Approximately(sourceSettings.orientationOffsetY, offset))
            {
                oriented.Add(offset, sideClip); // already yawed the way this slot wants
                return sideClip;
            }

            var clip = Object.Instantiate(sideClip);
            clip.name = sideClip.name + "_" + offset.ToString("0", CultureInfo.InvariantCulture);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.orientationOffsetY = offset;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var path = outputFolder + "/" + GeneratedAssetPrefix + SanitizeFileName(clip.name) + ".anim";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(clip, path);
            EditorUtility.SetDirty(clip);

            oriented.Add(offset, clip);
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
                    var label = string.IsNullOrEmpty(stance.Entries[i].menuName) ? "Pose " + i : stance.Entries[i].menuName;
                    stanceMenu.controls.Add(MakeToggle(label, stance.ParamName, PoseValue(i, stance.Entries.Count)));
                }
                EditorUtility.SetDirty(stanceMenu);

                root.controls.Add(MakeSubMenu(stance.Key, stanceMenu));
            }
            EditorUtility.SetDirty(root);

            var entry = GetOrCreateMenu(outputFolder + "/EasyLocoIdlePosesEntry.asset");
            entry.controls.Clear();
            entry.controls.Add(MakeSubMenu("Idle Poses", root));
            EditorUtility.SetDirty(entry);

            EnsureSubMenuInstaller(host, entry, LoadMainMenu()); // nest the idle poses under EasyLocoMain
        }

        // A synced VRChat Float only carries -1..1, so pose N cannot be selected by its raw index:
        // anything above 1 clamps down to 1. With three stand poses that made "Wide2" land on the
        // same value as "Wide1", so the menu drew Wide1 as already active and the next click on it
        // read as switching it off - back to the default pose. Spreading the poses evenly across
        // 0..1 keeps every selection inside the syncable range and distinct from its neighbours.
        private static float PoseValue(int index, int count)
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

        private static VRCExpressionsMenu LoadMainMenu()
        {
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(EasyLocoConst.MainMenuPath);
            if (menu == null)
            {
                throw new FileNotFoundException("EasyLoco main menu was not found.", EasyLocoConst.MainMenuPath);
            }

            return menu;
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

        private static string StripGeneratedPrefix(string name)
        {
            return name != null && name.StartsWith(GeneratedAssetPrefix) ? name.Substring(GeneratedAssetPrefix.Length) : name;
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
