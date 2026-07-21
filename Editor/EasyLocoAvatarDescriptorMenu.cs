using Puetsua.VRCEasyLoco;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Puetsua.VRCEasyLoco.Editor
{
    internal static class EasyLocoGameObjectMenu
    {
        private const string MenuPath = "GameObject/Add EasyLoco Component";
        private const int Priority = 49;

        [MenuItem(MenuPath, priority = Priority)]
        private static void AddEasyLocoComponent(MenuCommand command)
        {
            var gameObject = GetTargetGameObject(command);
            if (gameObject == null)
            {
                return;
            }

            var descriptor = gameObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return;
            }

            var easyLoco = gameObject.GetComponent<EasyLoco>();
            if (easyLoco == null)
            {
                easyLoco = Undo.AddComponent<EasyLoco>(gameObject);
            }

            Selection.activeObject = easyLoco;
            EditorGUIUtility.PingObject(easyLoco);
        }

        // Validation functions must be parameterless for Unity to bind them; a MenuCommand
        // parameter here is silently ignored, which left the item permanently enabled. Selection
        // reflects the right-clicked/target object during validation.
        [MenuItem(MenuPath, true)]
        private static bool CanAddEasyLocoComponent()
        {
            var gameObject = Selection.activeGameObject;
            return gameObject != null &&
                   gameObject.GetComponent<VRCAvatarDescriptor>() != null &&
                   gameObject.GetComponent<EasyLoco>() == null;
        }

        private static GameObject GetTargetGameObject(MenuCommand command)
        {
            if (command.context is GameObject contextGameObject)
            {
                return contextGameObject;
            }

            return Selection.activeGameObject;
        }
    }
}
