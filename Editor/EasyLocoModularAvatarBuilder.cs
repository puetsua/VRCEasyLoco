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

            /// <summary>Whether this stance replaces the template's built-in idle at all.</summary>
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

            foreach (var stance in stances)
            {
                SelectIdleEntries(stance);
            }

            var afkOverrides = BuildAfkOverrides(easyLoco);

            // Checked before a single asset is written. Everything below this line is destructive:
            // BuildController deletes the previous generated controller and copies a fresh one,
            // which mints a new GUID, and the avatar's installed prefab only catches up when the
            // build reaches SaveAsPrefabAsset. Throwing after that point would leave a working
            // avatar pointing at a controller that no longer exists - so a template that stopped
            // carrying a name this package writes into has to fail here, with nothing touched yet.
            EnsureTemplateCarriesMotions(BaseTemplatePath, stances.Where(stance => stance.Overrides).Select(stance => stance.IdleTargetName),
                EasyLocoConst.DesktopLocomotionStateMachine);
            EnsureTemplateCarriesStates(ActionTemplatePath, afkOverrides.Keys);

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
            // Scoped to the desktop branch: the VR stances play the same clips under the same names
            // and must keep the built-in poses (see DesktopLocomotionStateMachine).
            var baseController = (AnimatorController)BuildController(BaseTemplatePath, outputFolder, "EasyLocoBase.controller", baseReplacements,
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

            var actionController = (AnimatorController)BuildController(ActionTemplatePath, outputFolder, "EasyLocoAction.controller", new Dictionary<string, Motion>());
            ApplyStateMotionOverrides(actionController, new MotionReplacements(afkOverrides));
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
            // Same pre-flight as the main build, for the same reason: everything after it writes,
            // and the controller copy mints a new GUID that only the sleep prefab's save catches up
            // with.
            EnsureTemplateCarriesMotions(SleepTemplatePath, SleepTargets(easyLoco.sleep), null);

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

        // Which poses this stance actually contributes, and therefore whether it replaces the
        // template's built-in idle at all. Kept apart from BuildIdleSelector because it writes
        // nothing: the build has to know what it is going to look for in the templates before it
        // starts writing assets (see the pre-flight in BuildHost).
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
                stance.Motion = null; // keep the template's built-in idle
                return;
            }

            if (stance.Entries.Count == 1)
            {
                stance.Motion = stance.Entries[0].clip; // single override, no selector/menu
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

        // The pre-flight: does the template still carry every name the build is about to write into?
        // Read-only, and run before anything is generated, so a template that renamed one of them
        // fails while the avatar's previous build is still whole. The walk after the copy stays as
        // well - it is tied to what was actually swapped, where this one only asks what is reachable.
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

        // The pre-flight has no motions to put anywhere - it only cares about the names - but it
        // reports through the same ledger so a rename reads identically wherever it is caught.
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

        // Leaves only, because leaves are all the replacement walk ever matches: a blend tree is
        // recursed into, never swapped by its own name. Collecting tree names here would let a key
        // naming one pass the pre-flight and then fail after the copy, which is the one outcome
        // this check exists to prevent.
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

        // scopeStateMachineName limits the replacement to the state machines of that name; null
        // covers the whole controller.
        private static RuntimeAnimatorController BuildController(string sourcePath, string outputFolder, string fileName, IReadOnlyDictionary<string, Motion> replacements,
            string scopeStateMachineName = null)
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
            ReplaceMotions(controller, new MotionReplacements(replacements), outputFolder, scopeStateMachineName);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // Which sleeping slots this component actually overrides. Pure, and separate from producing
        // the clips for them, because the build has to know the names before it writes anything.
        //
        // A clip equal to the built-in is not an override: registering it would force a needless
        // clone of the whole sleeping tree for an avatar that never customised anything.
        //
        // The on-side pose fans out to one placeholder per tree. Left alone, the placeholders play
        // as authored and nothing is generated. Overridden, each placeholder slot is listed here and
        // filled below by a copy of the user's pose wearing that placeholder's root-transform
        // settings, so the slot keeps its yaw without the builder knowing what any slot's yaw is.
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

        private static Dictionary<string, Motion> BuildAfkOverrides(EasyLoco easyLoco)
        {
            var overrides = new Dictionary<string, Motion>();
            AddAfkOverrides(overrides, EasyLocoConst.AfkStances[0], easyLoco.standAfk);
            AddAfkOverrides(overrides, EasyLocoConst.AfkStances[1], easyLoco.crouchAfk);
            AddAfkOverrides(overrides, EasyLocoConst.AfkStances[2], easyLoco.proneAfk);
            return overrides;
        }

        // The AFK counterpart of ReplaceMotions: same ledger, but the names are states rather than
        // motions - the AFK clips are the whole motion of a state, so there is no tree to walk into.
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
                // The lookup comes first so a state already playing the user's clip still counts as
                // found - it is the name that has to exist, not the change.
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

        internal static void ReplaceMotions(AnimatorController controller, MotionReplacements replacements, string outputFolder, string scopeStateMachineName)
        {
            if (controller == null || replacements == null || replacements.IsEmpty)
            {
                return;
            }

            var roots = CollectReplacementRoots(controller, scopeStateMachineName);
            var controllerPath = AssetDatabase.GetAssetPath(controller);
            var clones = new Dictionary<BlendTree, BlendTree>();
            foreach (var root in roots)
            {
                ReplaceMotions(root, replacements, outputFolder, controllerPath, clones);
            }

            // The pre-flight already asked the template this, before anything was written. This one
            // is tied to the swaps that actually happened, so the two disagreeing is itself worth a
            // failed build.
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

        // Where the replacement is allowed to walk. An unscoped build starts at every layer; a
        // scoped one starts at the named state machines only.
        //
        // A scope that matches nothing throws rather than falling back to the whole controller: the
        // template and this name ship together, so a miss means someone renamed the state machine,
        // and the quiet failure would be the overrides leaking into the branch the scope exists to
        // protect - a build that looks fine and only shows up in VR.
        //
        // Nothing to replace never reaches here, by the early return above. That is on purpose: an
        // avatar with no overrides has nothing to leak, and failing its build over a branch someone
        // renamed in their own copy of the template would be a false alarm. The shipped template's
        // names are pinned by the tests instead.
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

        // A match is not descended into: the caller walks all of it anyway, and a nested state
        // machine of the same name would then be visited twice.
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

        // The clone cache spans the whole controller: nothing stops one shared tree from being the
        // motion of several states, and cloning it per state would have each clone delete the asset
        // the previous state was pointed at, leaving that state with a missing motion. The clone
        // path is deterministic, so the cache is what keeps that safe.
        private static void ReplaceMotions(AnimatorStateMachine stateMachine, MotionReplacements replacements, string outputFolder, string controllerPath, Dictionary<BlendTree, BlendTree> clones)
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

        private static Motion ReplaceMotion(Motion motion, MotionReplacements replacements, string outputFolder, string controllerPath, Dictionary<BlendTree, BlendTree> clones)
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

            return replacements.TryGet(motion.name, out var replacement) ? replacement : motion;
        }

        private static void ReplaceBlendTreeMotionsInPlace(BlendTree blendTree, MotionReplacements replacements, string outputFolder, string controllerPath, Dictionary<BlendTree, BlendTree> clones)
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
                else if (motion != null && replacements.Contains(motion.name))
                {
                    return true;
                }
            }

            return false;
        }

        private static BlendTree CloneBlendTree(BlendTree source, MotionReplacements replacements, string outputFolder)
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

        // Copied through serialization rather than with Object.Instantiate: every child motion of a
        // blend tree is serialized as a strong pointer, and Unity's clone path asserts once per
        // child - "(metaFlags & kStrongPPtrMask) == 0" - so the three Default* idle trees alone
        // logged 37 of those per build. Nothing else was wrong with Instantiate: its copy is
        // shallow, the children still pointing at the source's own motions, which is exactly what
        // the recursion below expects. CopySerialized is shallow in the same way.
        //
        // Assigning the public properties one at a time would silence the assert too, but it
        // silently drops m_NormalizedBlendValues - serialized, yet with no setter to reach it -
        // along with anything Unity adds to the type later.
        internal static BlendTree CloneBlendTreeInMemory(BlendTree source, MotionReplacements replacements, List<BlendTree> collected)
        {
            var clone = new BlendTree();
            EditorUtility.CopySerialized(source, clone);
            // CreateAsset renames the object after its file, and the clone is named for the
            // template it came from, so the name has to survive the copy verbatim.
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
