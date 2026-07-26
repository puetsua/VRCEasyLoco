using System.Collections.Generic;
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

        private void OnEnable()
        {
            showPoses = LoadFoldout(PosesFoldoutKey);
            showSleep = LoadFoldout(SleepFoldoutKey);
            showAfk = LoadFoldout(AfkFoldoutKey);

            InitializeDefaults((EasyLoco)target);

            standPoses = serializedObject.FindProperty(nameof(EasyLoco.standPoses));
            crouchPoses = serializedObject.FindProperty(nameof(EasyLoco.crouchPoses));
            pronePoses = serializedObject.FindProperty(nameof(EasyLoco.pronePoses));
            sleep = serializedObject.FindProperty(nameof(EasyLoco.sleep));
            standAfk = serializedObject.FindProperty(nameof(EasyLoco.standAfk));
            crouchAfk = serializedObject.FindProperty(nameof(EasyLoco.crouchAfk));
            proneAfk = serializedObject.FindProperty(nameof(EasyLoco.proneAfk));

            standList = CreatePoseList(standPoses, "Stand Idle Poses");
            crouchList = CreatePoseList(crouchPoses, "Crouch Idle Poses");
            proneList = CreatePoseList(pronePoses, "Prone Idle Poses");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var easyLoco = (EasyLoco)target;

            // Build installs onto the descriptor sharing this GameObject; with none there is nothing
            // to install onto, so explain the disabled button rather than leaving it dead.
            var hasAvatar = easyLoco.Avatar != null;
            if (!hasAvatar)
            {
                EditorGUILayout.HelpBox("This component must be on the same GameObject as the VRCAvatarDescriptor.", MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!hasAvatar))
            {
                if (GUILayout.Button("Build Modular Avatar"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Build(easyLoco);
                    serializedObject.Update();
                }
            }

            EditorGUILayout.Space();
            showPoses = DrawFoldout(PosesFoldoutKey, "Idle Animations", showPoses);
            if (showPoses)
            {
                EditorGUILayout.HelpBox("Row 0 is the Default pose (its clip may be overridden but it cannot be removed). Add rows to expose extra poses in the Idle Poses menu.", MessageType.None);

                standList.DoLayoutList();
                crouchList.DoLayoutList();
                proneList.DoLayoutList();
            }

            EditorGUILayout.Space();
            showSleep = DrawFoldout(SleepFoldoutKey, "Sleep Animations", showSleep);
            if (showSleep)
            {
                EditorGUILayout.HelpBox("Played while Sleep is toggled on and the avatar is prone. Head orientation blends between the poses. Leave a clip empty to keep the built-in default.", MessageType.None);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.up)), new GUIContent("Facing Up"));
                    EditorGUILayout.PropertyField(sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.down)), new GUIContent("Facing Down"));
                    DrawSleepSideSet("On Side", sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.side)));
                }
            }

            EditorGUILayout.Space();
            showAfk = DrawFoldout(AfkFoldoutKey, "AFK Animations", showAfk);
            if (showAfk)
            {
                EditorGUILayout.HelpBox("AFK is branched by posture at runtime. Leave a clip empty to keep the built-in default for that stage.", MessageType.None);
                DrawAfkSet("Stand AFK", standAfk);
                DrawAfkSet("Crouch AFK", crouchAfk);
                DrawAfkSet("Prone AFK", proneAfk);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // Sections start collapsed, and the user's expand/collapse choice is remembered across
        // selections and editor sessions. Only writes on change: the pref is read once in OnEnable
        // and the in-memory copy carries the repaints.
        private static bool DrawFoldout(string key, string label, bool expanded)
        {
            var value = EditorGUILayout.Foldout(expanded, label, true, EditorStyles.foldoutHeader);
            if (value != expanded)
            {
                EditorPrefs.SetBool(FoldoutPrefKey(key), value);
            }

            return value;
        }

        private static bool LoadFoldout(string key)
        {
            return EditorPrefs.GetBool(FoldoutPrefKey(key), false);
        }

        private static string FoldoutPrefKey(string key)
        {
            return EasyLocoConst.EditorPrefsPrefix + "Foldout." + key;
        }

        private ReorderableList CreatePoseList(SerializedProperty listProperty, string header)
        {
            var list = new ReorderableList(serializedObject, listProperty, false, true, true, true);

            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, header);

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
                        EditorGUI.TextField(nameRect, "Default");
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
                element.FindPropertyRelative(nameof(EasyLoco.IdlePose.menuName)).stringValue = "Pose " + index;
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

        // Feet Lock keeps both feet on the floor, which needs its own on-side pose per facing; the
        // facing up/down clips above are shared with that branch. One clip covers both sides - it is
        // authored lying on the left and played mirrored for the right.
        private static void DrawSleepSideSet(string label, SerializedProperty sideSet)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Authored lying on the left; the right side is mirrored automatically.", MessageType.None);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(sideSet.FindPropertyRelative(nameof(EasyLoco.SleepSideSet.normal)), new GUIContent("Normal"));
                EditorGUILayout.PropertyField(sideSet.FindPropertyRelative(nameof(EasyLoco.SleepSideSet.feetLockUp)), new GUIContent("Feet Lock (Facing Up)"));
                EditorGUILayout.PropertyField(sideSet.FindPropertyRelative(nameof(EasyLoco.SleepSideSet.feetLockDown)), new GUIContent("Feet Lock (Facing Down)"));
            }
        }

        private static void DrawAfkSet(string label, SerializedProperty afkSet)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(afkSet.FindPropertyRelative(nameof(EasyLoco.AfkSet.entering)), new GUIContent("Entering"));
                EditorGUILayout.PropertyField(afkSet.FindPropertyRelative(nameof(EasyLoco.AfkSet.looping)), new GUIContent("Looping"));
                EditorGUILayout.PropertyField(afkSet.FindPropertyRelative(nameof(EasyLoco.AfkSet.exiting)), new GUIContent("Exiting"));
            }
        }

        private static void InitializeDefaults(EasyLoco easyLoco)
        {
            var changed = false;

            if (easyLoco.standPoses == null || easyLoco.standPoses.Count == 0)
            {
                easyLoco.standPoses = new List<EasyLoco.IdlePose>
                {
                    new EasyLoco.IdlePose("Default", LoadClip(EasyLocoConst.StandDefaultClip)),
                    new EasyLoco.IdlePose("Wide1", LoadClip(EasyLocoConst.StandWide1Clip)),
                    new EasyLoco.IdlePose("Wide2", LoadClip(EasyLocoConst.StandWide2Clip)),
                };
                changed = true;
            }

            if (easyLoco.crouchPoses == null || easyLoco.crouchPoses.Count == 0)
            {
                easyLoco.crouchPoses = new List<EasyLoco.IdlePose>
                {
                    new EasyLoco.IdlePose("Default", LoadClip(EasyLocoConst.CrouchDefaultClip)),
                    new EasyLoco.IdlePose("Squatting", LoadClip(EasyLocoConst.CrouchSquattingClip)),
                };
                changed = true;
            }

            if (easyLoco.pronePoses == null || easyLoco.pronePoses.Count == 0)
            {
                easyLoco.pronePoses = new List<EasyLoco.IdlePose>
                {
                    new EasyLoco.IdlePose("Default", LoadClip(EasyLocoConst.ProneDefaultClip)),
                    new EasyLoco.IdlePose("LyingDown", LoadClip(EasyLocoConst.ProneLyingDownClip)),
                };
                changed = true;
            }

            changed |= InitializeSleepDefaults(easyLoco.sleep);
            changed |= InitializeAfkDefaults(easyLoco.standAfk);
            changed |= InitializeAfkDefaults(easyLoco.crouchAfk);
            changed |= InitializeAfkDefaults(easyLoco.proneAfk);

            if (changed)
            {
                EditorUtility.SetDirty(easyLoco);
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

            if (!IsSideSetFilled(set.side))
            {
                set.side = new EasyLoco.SleepSideSet
                {
                    normal = LoadClip(EasyLocoConst.SleepSideClip),
                    feetLockUp = LoadClip(EasyLocoConst.SleepSideFeetLockUpClip),
                    feetLockDown = LoadClip(EasyLocoConst.SleepSideFeetLockDownClip),
                };
                changed = true;
            }

            return changed;
        }

        private static bool IsSideSetFilled(EasyLoco.SleepSideSet set)
        {
            return set != null && (set.normal != null || set.feetLockUp != null || set.feetLockDown != null);
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
                    "Build succeeded — prefab added to the avatar.", "OK");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName, exception.Message, "OK");
            }
        }
    }
}
