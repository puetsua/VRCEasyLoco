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
        private SerializedProperty useCustomAction;
        private SerializedProperty actionAfk;

        private ReorderableList standList;
        private ReorderableList crouchList;
        private ReorderableList proneList;

        private bool showPoses = true;

        private void OnEnable()
        {
            InitializeDefaults((EasyLoco)target);

            standPoses = serializedObject.FindProperty(nameof(EasyLoco.standPoses));
            crouchPoses = serializedObject.FindProperty(nameof(EasyLoco.crouchPoses));
            pronePoses = serializedObject.FindProperty(nameof(EasyLoco.pronePoses));
            useCustomAction = serializedObject.FindProperty(nameof(EasyLoco.useCustomAction));
            actionAfk = serializedObject.FindProperty(nameof(EasyLoco.actionAfk));

            standList = CreatePoseList(standPoses, "Stand Idle Poses");
            crouchList = CreatePoseList(crouchPoses, "Crouch Idle Poses");
            proneList = CreatePoseList(pronePoses, "Prone Idle Poses");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var easyLoco = (EasyLoco)target;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Avatar", easyLoco.Avatar, typeof(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor), true);
            }

            EditorGUILayout.Space();
            showPoses = EditorGUILayout.Foldout(showPoses, "EasyLoco Animators", true, EditorStyles.foldoutHeader);
            if (showPoses)
            {
                EditorGUILayout.HelpBox("Row 0 is the Default pose (its clip may be overridden but it cannot be removed). Add rows to expose extra poses in the Idle Poses menu.", MessageType.None);

                standList.DoLayoutList();
                crouchList.DoLayoutList();
                proneList.DoLayoutList();
            }

            DrawActionSlots();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(easyLoco.Avatar == null))
            {
                if (GUILayout.Button("Build Modular Avatar"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Build(easyLoco);
                    serializedObject.Update();
                }
            }

            serializedObject.ApplyModifiedProperties();
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

        private void DrawActionSlots()
        {
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(useCustomAction, new GUIContent("Use Custom Action"));
            if (!useCustomAction.boolValue)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(actionAfk, new GUIContent("AFK"));
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

            if (changed)
            {
                EditorUtility.SetDirty(easyLoco);
            }
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
                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName, "Built Modular Avatar controllers and expression menu.", "OK");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName, exception.Message, "OK");
            }
        }
    }
}
