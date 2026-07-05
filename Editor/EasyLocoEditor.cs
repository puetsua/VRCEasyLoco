using Puetsua.VRCEasyLoco;
using UnityEditor;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor
{
    [CustomEditor(typeof(EasyLoco))]
    public class EasyLocoEditor : UnityEditor.Editor
    {
        private SerializedProperty useCustomBaseLocomotion;
        private SerializedProperty baseStandStill;
        private SerializedProperty baseCrouchStill;
        private SerializedProperty baseLowCrawlStill;

        private SerializedProperty useCustomAction;
        private SerializedProperty actionAfk;

        private void OnEnable()
        {
            useCustomBaseLocomotion = serializedObject.FindProperty(nameof(EasyLoco.useCustomBaseLocomotion));
            baseStandStill = serializedObject.FindProperty(nameof(EasyLoco.baseStandStill));
            baseCrouchStill = serializedObject.FindProperty(nameof(EasyLoco.baseCrouchStill));
            baseLowCrawlStill = serializedObject.FindProperty(nameof(EasyLoco.baseLowCrawlStill));

            useCustomAction = serializedObject.FindProperty(nameof(EasyLoco.useCustomAction));
            actionAfk = serializedObject.FindProperty(nameof(EasyLoco.actionAfk));
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
            EditorGUILayout.LabelField("Template Animators", EditorStyles.boldLabel);
            DrawBaseSlots();
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

        private void DrawBaseSlots()
        {
            EditorGUILayout.PropertyField(useCustomBaseLocomotion, new GUIContent("Use Custom Base"));
            if (!useCustomBaseLocomotion.boolValue)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(baseStandStill, new GUIContent("Stand Still"));
                EditorGUILayout.PropertyField(baseCrouchStill, new GUIContent("Crouch Still"));
                EditorGUILayout.PropertyField(baseLowCrawlStill, new GUIContent("Prone Still"));
            }
        }

        private void DrawActionSlots()
        {
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

        private static void Build(EasyLoco easyLoco)
        {
            try
            {
                EasyLocoModularAvatarBuilder.Build(easyLoco);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(EasyLocoConst.DisplayName, exception.Message, "OK");
            }
        }
    }
}
