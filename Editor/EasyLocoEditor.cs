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

        // Foldout prefs are keyed by these strings, not field names, so a rename does not reset them.
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

        private const float BannerMargin = 24f;

        private static Texture2D banner;

        private static Texture2D Banner =>
            banner != null ? banner : (banner = AssetDatabase.LoadAssetAtPath<Texture2D>(EasyLocoConst.BannerTexturePath));

        private static LocalizedTextDataset Localized => LocalizedTextDataset.primary;

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

            // Domain reload can reopen this inspector on a dummy/missing-script object.
            var easyLoco = target as EasyLoco;
            if (easyLoco == null)
            {
                return;
            }

            InitializeDefaults(easyLoco);

            standPoses = serializedObject.FindProperty(nameof(EasyLoco.standPoses));
            crouchPoses = serializedObject.FindProperty(nameof(EasyLoco.crouchPoses));
            pronePoses = serializedObject.FindProperty(nameof(EasyLoco.pronePoses));
            sleep = serializedObject.FindProperty(nameof(EasyLoco.sleep));
            standAfk = serializedObject.FindProperty(nameof(EasyLoco.standAfk));
            crouchAfk = serializedObject.FindProperty(nameof(EasyLoco.crouchAfk));
            proneAfk = serializedObject.FindProperty(nameof(EasyLoco.proneAfk));

            standList = CreatePoseList(standPoses, () => Localized.headerStandPoses);
            crouchList = CreatePoseList(crouchPoses, () => Localized.headerCrouchPoses);
            proneList = CreatePoseList(pronePoses, () => Localized.headerPronePoses);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawBanner();
            DrawVersion();
            DrawLanguage();

            var easyLoco = target as EasyLoco;
            if (easyLoco == null)
            {
                return;
            }

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

            var easyLoco = target as EasyLoco;
            if (easyLoco == null)
            {
                return;
            }

            Undo.RecordObject(easyLoco, "Change EasyLoco Language");
            InitializeDefaults(easyLoco);
            serializedObject.Update();
        }

        private static void DrawVersion()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{Localized.labelVersion} {EasyLocoConst.Version}", EditorStyles.miniLabel);
            }
        }

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
            catch (Exception exception)
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

        internal static List<EasyLoco.IdlePose> BuildDefaults(PoseDefault[] spec, LocalizedTextDataset text)
        {
            return spec.Select(pose => new EasyLoco.IdlePose(pose.Name(text), LoadClip(pose.ClipPath))).ToList();
        }

        /// <summary>
        /// Same row count and clips as the built-ins, and extra names match some language in
        /// <see cref="LocalizedTextDataset.All"/>. Row 0's name is ignored (the inspector locks it).
        /// </summary>
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

        /// <summary>Relabels row 0 always; other rows only while <see cref="IsPristine"/>.</summary>
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

        private static bool EnsureStanceDefaults(ref List<EasyLoco.IdlePose> poses, PoseDefault[] spec)
        {
            var changed = false;
            if (poses == null || poses.Count == 0)
            {
                poses = BuildDefaults(spec, Localized);
                changed = true;
            }

            return changed | SyncPoseNames(poses, spec, Localized);
        }

        private static void InitializeDefaults(EasyLoco easyLoco)
        {
            var changed = false;

            changed |= EnsureStanceDefaults(ref easyLoco.standPoses, StandDefaults);
            changed |= EnsureStanceDefaults(ref easyLoco.crouchPoses, CrouchDefaults);
            changed |= EnsureStanceDefaults(ref easyLoco.pronePoses, ProneDefaults);

            changed |= InitializeSleepDefaults(easyLoco.sleep);
            changed |= InitializeAfkDefaults(easyLoco.standAfk);
            changed |= InitializeAfkDefaults(easyLoco.crouchAfk);
            changed |= InitializeAfkDefaults(easyLoco.proneAfk);

            if (changed)
            {
                EditorUtility.SetDirty(easyLoco);

                PrefabUtility.RecordPrefabInstancePropertyModifications(easyLoco);
            }
        }

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
            // Capture before the main build: replacing the generated host destroys a
            // nested sleep module with it, and checking after would skip its rebuild.
            var sleepIncluded = EasyLocoModularAvatarBuilder.HasSleepLocomotion(easyLoco);
            try
            {
                EasyLocoModularAvatarBuilder.Build(easyLoco);
                if (sleepIncluded)
                {
                    EasyLocoModularAvatarBuilder.BuildSleepLocomotion(easyLoco);
                }

                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName,
                    sleepIncluded ? Localized.msgBuildSucceededWithSleep : Localized.msgBuildSucceeded,
                    Localized.dialogOk);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName, exception.Message, Localized.dialogOk);
            }
        }
    }
}
