using System;
using System.Collections.Generic;
using System.Linq;
using Puetsua.VRCEasyLoco;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor
{
    [CustomEditor(typeof(EasyLoco))]
    public class EasyLocoEditor : UnityEditor.Editor
    {
        private SerializedProperty standPoses;
        private SerializedProperty crouchPoses;
        private SerializedProperty pronePoses;
        private SerializedProperty sleep;
        private SerializedProperty standAfk;
        private SerializedProperty crouchAfk;
        private SerializedProperty proneAfk;

        private ReorderableList standList;
        private ReorderableList crouchList;
        private ReorderableList proneList;

        // Section keys are stable strings rather than field names: they end up in the user's editor
        // prefs, so renaming a field must not silently forget their choice.
        private const string PosesFoldoutKey = "Idle";
        private const string SleepFoldoutKey = "Sleep";
        private const string AfkFoldoutKey = "Afk";

        private bool showPoses;
        private bool showSleep;
        private bool showAfk;

        private bool showPosesHelp;
        private bool showSleepHelp;
        private bool showAfkHelp;

        private const float InfoButtonSize = 18f;

        // Leaves room for the inspector's own padding and a scrollbar, so the banner never forces a
        // horizontal scroll.
        private const float BannerMargin = 24f;

        private static Texture2D banner;

        private static Texture2D Banner =>
            banner != null ? banner : (banner = AssetDatabase.LoadAssetAtPath<Texture2D>(EasyLocoConst.BannerTexturePath));

        private static LocalizedTextDataset Localized => LocalizedTextDataset.primary;

        // Built-in editor icons are only valid once the skin exists, so this is fetched lazily
        // rather than in a field initializer. The image is what's worth caching; the tooltip is
        // reassigned every time so a language change is picked up without rebuilding the content.
        private static GUIContent infoIcon;

        private static GUIContent InfoIcon
        {
            get
            {
                if (infoIcon == null)
                {
                    infoIcon = new GUIContent(EditorGUIUtility.IconContent("console.infoicon").image);
                }

                infoIcon.tooltip = Localized.tooltipInfoButton;
                return infoIcon;
            }
        }

        private void OnEnable()
        {
            showPoses = LoadFoldout(PosesFoldoutKey);
            showSleep = LoadFoldout(SleepFoldoutKey);
            showAfk = LoadFoldout(AfkFoldoutKey);

            showPosesHelp = LoadHelp(PosesFoldoutKey);
            showSleepHelp = LoadHelp(SleepFoldoutKey);
            showAfkHelp = LoadHelp(AfkFoldoutKey);

            InitializeDefaults((EasyLoco)target);

            standPoses = serializedObject.FindProperty(nameof(EasyLoco.standPoses));
            crouchPoses = serializedObject.FindProperty(nameof(EasyLoco.crouchPoses));
            pronePoses = serializedObject.FindProperty(nameof(EasyLoco.pronePoses));
            sleep = serializedObject.FindProperty(nameof(EasyLoco.sleep));
            standAfk = serializedObject.FindProperty(nameof(EasyLoco.standAfk));
            crouchAfk = serializedObject.FindProperty(nameof(EasyLoco.crouchAfk));
            proneAfk = serializedObject.FindProperty(nameof(EasyLoco.proneAfk));

            // Headers are resolved per draw rather than captured here: the lists outlive a language
            // change, and a captured string would keep showing the old language until reselection.
            standList = CreatePoseList(standPoses, () => Localized.headerStandPoses);
            crouchList = CreatePoseList(crouchPoses, () => Localized.headerCrouchPoses);
            proneList = CreatePoseList(pronePoses, () => Localized.headerPronePoses);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawBanner();
            DrawLanguage();

            var easyLoco = (EasyLoco)target;

            // Build installs onto the descriptor sharing this GameObject; with none there is nothing
            // to install onto, so explain the disabled button rather than leaving it dead.
            var hasAvatar = easyLoco.Avatar != null;
            if (!hasAvatar)
            {
                EditorGUILayout.HelpBox(Localized.msgNeedsAvatarDescriptor, MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!hasAvatar))
            {
                if (GUILayout.Button(Localized.buttonBuild))
                {
                    serializedObject.ApplyModifiedProperties();
                    Build(easyLoco);
                    serializedObject.Update();
                }
            }

            EditorGUILayout.Space();
            showPoses = DrawFoldout(PosesFoldoutKey, Localized.sectionIdle, showPoses, ref showPosesHelp);
            if (showPoses)
            {
                DrawHelp(showPosesHelp, Localized.helpIdle);

                standList.DoLayoutList();
                crouchList.DoLayoutList();
                proneList.DoLayoutList();
            }

            EditorGUILayout.Space();
            showAfk = DrawFoldout(AfkFoldoutKey, Localized.sectionAfk, showAfk, ref showAfkHelp);
            if (showAfk)
            {
                DrawHelp(showAfkHelp, Localized.helpAfk);
                DrawAfkSet(Localized.labelStandAfk, standAfk);
                DrawAfkSet(Localized.labelCrouchAfk, crouchAfk);
                DrawAfkSet(Localized.labelProneAfk, proneAfk);
            }

            // Ruled off from the sections above: everything before this is the locomotion that Build
            // Modular Avatar installs, while sleeping is a module of its own - its own prefab, put on
            // the avatar by its own button.
            EditorGUILayout.Space();
            DrawSeparator();
            EditorGUILayout.Space();

            showSleep = DrawFoldout(SleepFoldoutKey, Localized.sectionSleep, showSleep, ref showSleepHelp);
            if (showSleep)
            {
                DrawHelp(showSleepHelp, Localized.helpSleep);

                DrawSleepBuild(easyLoco, hasAvatar);

                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.up)), new GUIContent(Localized.labelSleepUp));
                    EditorGUILayout.PropertyField(sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.down)), new GUIContent(Localized.labelSleepDown));
                    EditorGUILayout.PropertyField(sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.side)), new GUIContent(Localized.labelSleepSide));
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        // The language itself is a global editor preference rather than component state, so it is not
        // part of the serialized object. Sits under the banner: it changes what every label below
        // reads, so it belongs above all of them.
        //
        // Switching it does touch the component, though - the pose names EasyLoco owns are re-labelled
        // on the spot, so the expression menu follows the language without the user having to remove
        // and re-add the component. serializedObject is refreshed afterwards because that edit goes
        // through the object directly, behind the back of the properties drawn below.
        private void DrawLanguage()
        {
            EditorGUI.BeginChangeCheck();
            var language = (SupportedLanguage)EditorGUILayout.EnumPopup(
                Localized.labelLanguage, LocalizedTextDataset.Current);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            LocalizedTextDataset.SetLanguage(language);
            LocalizedTextDataset.SaveLanguage(language);

            // Undoable here but not in OnEnable: this is a deliberate user action, where the same
            // call during a mere selection change has no business landing on the undo stack. Note
            // that undoing only rolls back the names, not the language itself, so reselecting the
            // component re-applies them - the undo is a within-session escape hatch, not a way to
            // keep the old names under the new language.
            Undo.RecordObject(target, "Change EasyLoco Language");
            InitializeDefaults((EasyLoco)target);
            serializedObject.Update();
        }

        // Shrinks to the inspector width but never scales past the artwork's native size, so a wide
        // inspector doesn't blow it up into a blurry strip. Only the height is reserved: the row
        // spans the full width and ScaleToFit centres the artwork inside it. Silently skipped if the
        // texture is missing - a lost banner shouldn't cost the user the rest of the inspector.
        private static void DrawBanner()
        {
            var banner = Banner;
            if (banner == null)
            {
                return;
            }

            var width = Mathf.Min(EditorGUIUtility.currentViewWidth - BannerMargin, banner.width);
            var height = width * banner.height / (float)banner.width;
            var rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, banner, ScaleMode.ScaleToFit);
        }

        // Sections start collapsed, and the user's expand/collapse choice is remembered across
        // selections and editor sessions. Only writes on change: the pref is read once in OnEnable
        // and the in-memory copy carries the repaints.
        //
        // The header also carries an (i) button toggling the section's explanation, so the text is
        // there when wanted without permanently eating inspector height. Its state is remembered the
        // same way. The button sits outside the foldout's own rect so the two clicks never overlap.
        private static bool DrawFoldout(string key, string label, bool expanded, ref bool helpShown)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4f,
                EditorStyles.foldoutHeader);
            var buttonRect = new Rect(rect.xMax - InfoButtonSize, rect.y + 2f, InfoButtonSize, InfoButtonSize);
            var foldoutRect = new Rect(rect.x, rect.y, rect.width - InfoButtonSize, rect.height);

            var value = EditorGUI.Foldout(foldoutRect, expanded, label, true, EditorStyles.foldoutHeader);
            if (value != expanded)
            {
                EditorPrefs.SetBool(FoldoutPrefKey(key), value);
            }

            if (GUI.Button(buttonRect, InfoIcon, EditorStyles.iconButton))
            {
                helpShown = !helpShown;
                EditorPrefs.SetBool(HelpPrefKey(key), helpShown);

                // Toggling the description on a collapsed section would otherwise do nothing
                // visible, since the text is drawn inside the section body.
                if (helpShown && !value)
                {
                    value = true;
                    EditorPrefs.SetBool(FoldoutPrefKey(key), true);
                }
            }

            return value;
        }

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.15f, 0.15f, 0.15f)
                : new Color(0.6f, 0.6f, 0.6f));
        }

        private static void DrawHelp(bool shown, string text)
        {
            if (shown)
            {
                EditorGUILayout.HelpBox(text, MessageType.None);
            }
        }

        private static bool LoadFoldout(string key)
        {
            return EditorPrefs.GetBool(FoldoutPrefKey(key), false);
        }

        private static bool LoadHelp(string key)
        {
            return EditorPrefs.GetBool(HelpPrefKey(key), false);
        }

        private static string FoldoutPrefKey(string key)
        {
            return EasyLocoConst.EditorPrefsPrefix + "Foldout." + key;
        }

        private static string HelpPrefKey(string key)
        {
            return EasyLocoConst.EditorPrefsPrefix + "Help." + key;
        }

        private ReorderableList CreatePoseList(SerializedProperty listProperty, Func<string> header)
        {
            var list = new ReorderableList(serializedObject, listProperty, false, true, true, true);

            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, header());

            list.elementHeightCallback = index => EditorGUIUtility.singleLineHeight + 6f;

            list.drawElementCallback = (rect, index, active, focused) =>
            {
                var element = listProperty.GetArrayElementAtIndex(index);
                var nameProp = element.FindPropertyRelative(nameof(EasyLoco.IdlePose.menuName));
                var clipProp = element.FindPropertyRelative(nameof(EasyLoco.IdlePose.clip));

                rect.y += 3f;
                rect.height = EditorGUIUtility.singleLineHeight;

                var nameWidth = Mathf.Min(140f, rect.width * 0.4f);
                var nameRect = new Rect(rect.x, rect.y, nameWidth - 6f, rect.height);
                var clipRect = new Rect(rect.x + nameWidth, rect.y, rect.width - nameWidth, rect.height);

                if (index == 0)
                {
                    // Shows the stored name rather than a literal: row 0's name is written in
                    // whatever language created the component, so a hard-coded one would lie.
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.TextField(nameRect, nameProp.stringValue);
                    }
                }
                else
                {
                    EditorGUI.PropertyField(nameRect, nameProp, GUIContent.none);
                }

                EditorGUI.PropertyField(clipRect, clipProp, GUIContent.none);
            };

            list.onAddCallback = reorderable =>
            {
                var property = reorderable.serializedProperty;
                var index = property.arraySize;
                property.arraySize++;
                var element = property.GetArrayElementAtIndex(index);
                element.FindPropertyRelative(nameof(EasyLoco.IdlePose.menuName)).stringValue = Localized.posePrefix + index;
                element.FindPropertyRelative(nameof(EasyLoco.IdlePose.clip)).objectReferenceValue = null;
            };

            // The Default pose (row 0) is permanent.
            list.onCanRemoveCallback = reorderable => reorderable.index > 0;
            list.onRemoveCallback = reorderable =>
            {
                if (reorderable.index > 0)
                {
                    ReorderableList.defaultBehaviours.DoRemoveButton(reorderable);
                }
            };

            return list;
        }

        // Sleeping installs as a prefab of its own, appended over whatever base locomotion the avatar
        // already has - so it is built on its own button instead of by Build Modular Avatar. Useful
        // for an avatar that wants the sleeping pose but not EasyLoco's locomotion, and for carrying
        // a customised set of clips to another avatar; the generated prefab is pinged afterwards so
        // it is easy to find and drag.
        // One button, because the two actions are the two halves of the same switch: with the module
        // on the avatar the only thing left to offer is taking it off. Rebuilding after a clip change
        // means Remove then Build, which also re-picks where the Sleep menu belongs.
        private void DrawSleepBuild(EasyLoco easyLoco, bool hasAvatar)
        {
            var installed = EasyLocoModularAvatarBuilder.HasSleepLocomotion(easyLoco);

            using (new EditorGUI.DisabledScope(!hasAvatar))
            {
                if (GUILayout.Button(installed ? Localized.buttonRemoveSleep : Localized.buttonBuildSleep))
                {
                    serializedObject.ApplyModifiedProperties();
                    if (installed)
                    {
                        EasyLocoModularAvatarBuilder.RemoveSleepLocomotion(easyLoco);
                    }
                    else
                    {
                        BuildSleepLocomotion(easyLoco);
                    }
                    serializedObject.Update();
                }
            }
        }

        private static void BuildSleepLocomotion(EasyLoco easyLoco)
        {
            try
            {
                var path = EasyLocoModularAvatarBuilder.BuildSleepLocomotion(easyLoco);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    EditorGUIUtility.PingObject(prefab);
                }

                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName,
                    Localized.msgSleepInstalled + "\n\n" + path, Localized.dialogOk);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName, exception.Message, Localized.dialogOk);
            }
        }

        private static void DrawAfkSet(string label, SerializedProperty afkSet)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(afkSet.FindPropertyRelative(nameof(EasyLoco.AfkSet.entering)), new GUIContent(Localized.labelAfkEntering));
                EditorGUILayout.PropertyField(afkSet.FindPropertyRelative(nameof(EasyLoco.AfkSet.looping)), new GUIContent(Localized.labelAfkLooping));
                EditorGUILayout.PropertyField(afkSet.FindPropertyRelative(nameof(EasyLoco.AfkSet.exiting)), new GUIContent(Localized.labelAfkExiting));
            }
        }

        /// <summary>One built-in idle pose: the localized name it carries, and the clip behind it.</summary>
        internal sealed class PoseDefault
        {
            public readonly Func<LocalizedTextDataset, string> Name;
            public readonly string ClipPath;

            public PoseDefault(Func<LocalizedTextDataset, string> name, string clipPath)
            {
                Name = name;
                ClipPath = clipPath;
            }
        }

        // The built-in pose sets, described once so that filling a fresh component and deciding
        // whether an existing one is still untouched read from the same table.
        internal static readonly PoseDefault[] StandDefaults =
        {
            new PoseDefault(text => text.poseDefault, EasyLocoConst.StandDefaultClip),
            new PoseDefault(text => text.poseStandWide1, EasyLocoConst.StandWide1Clip),
            new PoseDefault(text => text.poseStandWide2, EasyLocoConst.StandWide2Clip),
        };

        internal static readonly PoseDefault[] CrouchDefaults =
        {
            new PoseDefault(text => text.poseDefault, EasyLocoConst.CrouchDefaultClip),
            new PoseDefault(text => text.poseCrouchSquatting, EasyLocoConst.CrouchSquattingClip),
        };

        internal static readonly PoseDefault[] ProneDefaults =
        {
            new PoseDefault(text => text.poseDefault, EasyLocoConst.ProneDefaultClip),
            new PoseDefault(text => text.poseProneLyingDown, EasyLocoConst.ProneLyingDownClip),
        };

        // The three take the dataset as an argument rather than reading the global: the language a
        // list is being written in is exactly the interesting variable here, and tests must be able
        // to drive it without touching the user's saved preference.
        internal static List<EasyLoco.IdlePose> BuildDefaults(PoseDefault[] spec, LocalizedTextDataset text)
        {
            return spec.Select(pose => new EasyLoco.IdlePose(pose.Name(text), LoadClip(pose.ClipPath))).ToList();
        }

        // Whether a stance still holds exactly what EasyLoco put there - same number of rows, same
        // clips, and names that match the built-ins in *some* language. That last part is what lets a
        // component authored in English still be recognised after a switch to Chinese; without it
        // EasyLoco would mistake its own English names for the user's edits and never update them.
        //
        // Row 0's name is left out of the test on purpose: the inspector locks that field, so it can
        // never be the thing the user customised, and judging by it would only produce false negatives.
        internal static bool IsPristine(List<EasyLoco.IdlePose> poses, PoseDefault[] spec)
        {
            if (poses == null || poses.Count != spec.Length)
            {
                return false;
            }

            for (var i = 0; i < spec.Length; i++)
            {
                if (poses[i] == null || poses[i].clip != LoadClip(spec[i].ClipPath))
                {
                    return false;
                }

                if (i > 0 && !LocalizedTextDataset.All.Any(text => spec[i].Name(text) == poses[i].menuName))
                {
                    return false;
                }
            }

            return true;
        }

        // Re-labels one stance for the current language. Row 0 always follows it - that name is
        // locked in the inspector, so there is no user edit there to protect and leaving it in the
        // old language would only look broken. The other rows follow only while the stance is
        // untouched, which is decided per stance: customising the stand poses freezes their names
        // without freezing crouch and prone.
        internal static bool SyncPoseNames(List<EasyLoco.IdlePose> poses, PoseDefault[] spec, LocalizedTextDataset text)
        {
            if (poses == null || poses.Count == 0)
            {
                return false;
            }

            var rows = IsPristine(poses, spec) ? spec.Length : 1;
            var changed = false;

            for (var i = 0; i < rows; i++)
            {
                var name = spec[i].Name(text);
                if (poses[i] == null || poses[i].menuName == name)
                {
                    continue;
                }

                poses[i].menuName = name;
                changed = true;
            }

            return changed;
        }

        private static void InitializeDefaults(EasyLoco easyLoco)
        {
            var changed = false;

            if (easyLoco.standPoses == null || easyLoco.standPoses.Count == 0)
            {
                easyLoco.standPoses = BuildDefaults(StandDefaults, Localized);
                changed = true;
            }

            if (easyLoco.crouchPoses == null || easyLoco.crouchPoses.Count == 0)
            {
                easyLoco.crouchPoses = BuildDefaults(CrouchDefaults, Localized);
                changed = true;
            }

            if (easyLoco.pronePoses == null || easyLoco.pronePoses.Count == 0)
            {
                easyLoco.pronePoses = BuildDefaults(ProneDefaults, Localized);
                changed = true;
            }

            changed |= SyncPoseNames(easyLoco.standPoses, StandDefaults, Localized);
            changed |= SyncPoseNames(easyLoco.crouchPoses, CrouchDefaults, Localized);
            changed |= SyncPoseNames(easyLoco.pronePoses, ProneDefaults, Localized);

            changed |= InitializeSleepDefaults(easyLoco.sleep);
            changed |= InitializeAfkDefaults(easyLoco.standAfk);
            changed |= InitializeAfkDefaults(easyLoco.crouchAfk);
            changed |= InitializeAfkDefaults(easyLoco.proneAfk);

            if (changed)
            {
                EditorUtility.SetDirty(easyLoco);

                // These writes go through the object rather than a SerializedProperty, so on a prefab
                // instance they would not register as overrides and would be dropped on reload.
                // A no-op on anything that is not a prefab instance.
                PrefabUtility.RecordPrefabInstancePropertyModifications(easyLoco);
            }
        }

        // Prefill empty parts of a sleep set with the built-in clips. The facings and the side pose
        // are filled independently: a component authored before the side pose collapsed to one clip
        // still has its facings, and an all-or-nothing guard would leave the new side group blank.
        private static bool InitializeSleepDefaults(EasyLoco.SleepSet set)
        {
            if (set == null)
            {
                return false;
            }

            var changed = false;

            if (set.up == null && set.down == null)
            {
                set.up = LoadClip(EasyLocoConst.SleepUpClip);
                set.down = LoadClip(EasyLocoConst.SleepDownClip);
                changed = true;
            }

            if (set.side == null)
            {
                set.side = LoadClip(EasyLocoConst.SleepSideClip);
                changed = true;
            }

            return changed;
        }

        // Prefill a fresh AFK set (all stages empty) with the shared built-in defaults.
        private static bool InitializeAfkDefaults(EasyLoco.AfkSet set)
        {
            if (set == null || set.entering != null || set.looping != null || set.exiting != null)
            {
                return false;
            }

            set.entering = LoadClip(EasyLocoConst.AfkEnteringDefaultClip);
            set.looping = LoadClip(EasyLocoConst.AfkLoopingDefaultClip);
            set.exiting = LoadClip(EasyLocoConst.AfkExitingDefaultClip);
            return true;
        }

        private static AnimationClip LoadClip(string path)
        {
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        private static void Build(EasyLoco easyLoco)
        {
            try
            {
                EasyLocoModularAvatarBuilder.Build(easyLoco);
                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName,
                    Localized.msgBuildSucceeded, Localized.dialogOk);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName, exception.Message, Localized.dialogOk);
            }
        }
    }
}
