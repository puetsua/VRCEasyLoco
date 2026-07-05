using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Puetsua.VRCEasyLoco.Editor
{
    public class EasyLocoWindow : EditorWindow
    {
        private VRCAvatarDescriptor avatar;
        private Vector2 scrollPosition;

        [MenuItem(EasyLocoConst.MenuPath, priority = EasyLocoConst.MenuPriority)]
        public static void Open()
        {
            var window = GetWindow<EasyLocoWindow>(EasyLocoConst.DisplayName);
            window.minSize = new Vector2(420, 320);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(EasyLocoConst.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Initial scaffold: inspect a VRChat avatar's animation layer assignments before EasyLoco adds edit operations.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Avatar", avatar, typeof(VRCAvatarDescriptor), true);
            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(avatar == null))
            {
                if (GUILayout.Button("Analyze Avatar"))
                {
                    LogAvatarSummary();
                }
            }

            EditorGUILayout.Space();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawLayerSummary();
            EditorGUILayout.EndScrollView();
        }

        private void DrawLayerSummary()
        {
            if (avatar == null)
            {
                EditorGUILayout.HelpBox("Assign an avatar to inspect its VRChat animation layers.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Animation Layers", EditorStyles.boldLabel);

            var layers = avatar.baseAnimationLayers;
            for (var i = 0; i < EasyLocoConst.LayerNames.Length; i++)
            {
                var controller = GetControllerName(layers, i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(EasyLocoConst.LayerNames[i], GUILayout.Width(90));
                EditorGUILayout.LabelField(controller);
                EditorGUILayout.EndHorizontal();
            }
        }

        private static string GetControllerName(VRCAvatarDescriptor.CustomAnimLayer[] layers, int index)
        {
            if (layers == null || index < 0 || index >= layers.Length)
            {
                return "Unavailable";
            }

            var layer = layers[index];
            if (!layer.isDefault && layer.animatorController != null)
            {
                return layer.animatorController.name;
            }

            return layer.isDefault ? "Default" : "None";
        }

        private void LogAvatarSummary()
        {
            if (avatar == null)
            {
                return;
            }

            Debug.Log($"[{EasyLocoConst.DisplayName}] Avatar: {avatar.name}");
        }
    }
}
