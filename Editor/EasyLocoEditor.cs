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
        private SerializedProperty sleepEnabled;
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
        private const float HeaderToggleWidth = 16f;

        // Leaves room for the inspector's own padding and a scrollbar, so the banner never forces a
        // horizontal scroll.
        private const float BannerMargin = 24f;

        private static Texture2D banner;

        private static Texture2D Banner =>
            banner != null ? banner : (banner = AssetDatabase.LoadAssetAtPath<Texture2D>(EasyLocoConst.BannerTexturePath));

        // Built-in editor icons are only valid once the skin exists, so this is fetched lazily
        // rather than in a field initializer.
        private static GUIContent infoIcon;

        private static GUIContent InfoIcon =>
            infoIcon ?? (infoIcon = new GUIContent(EditorGUIUtility.IconContent("console.infoicon").image,
                "Show or hide the description for this section."));

        private static readonly GUIContent SleepEnabledContent = new GUIContent(string.Empty,
            "Install the sleeping locomotion. Off leaves the avatar with its plain prone idle and " +
            "adds no sleep animator, parameters, menu, or sensors.");

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
            sleepEnabled = sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.enabled));
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

            DrawBanner();

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
            showPoses = DrawFoldout(PosesFoldoutKey, "Idle Animations", showPoses, ref showPosesHelp);
            if (showPoses)
            {
                DrawHelp(showPosesHelp, "Row 0 is the Default pose (its clip may be overridden but it cannot be removed). Add rows to expose extra poses in the Idle Poses menu.");

                standList.DoLayoutList();
                crouchList.DoLayoutList();
                proneList.DoLayoutList();
            }

            EditorGUILayout.Space();
            showSleep = DrawFoldout(SleepFoldoutKey, "Sleep Animations", showSleep, ref showSleepHelp, sleepEnabled, SleepEnabledContent);
            if (showSleep)
            {
                DrawHelp(showSleepHelp, "Optional - the checkbox on this header installs it. Played while Sleep is toggled on and the avatar is prone. Head orientation blends between the poses. Leave a clip empty to keep the built-in default.\n\nOn Side (Left) is the authored pose - lying on the left side; the right side is mirrored from it automatically, and Feet Lock plays the same pose.");

                if (!sleepEnabled.boolValue)
                {
                    EditorGUILayout.HelpBox("Sleeping is off. The build installs no sleep animator, menu, parameters, or sensors.", MessageType.Info);
                }

                // The clips stay visible while off so a user can see what they would get back, but
                // editing them would have no effect on the build until sleeping is switched on.
                using (new EditorGUI.DisabledScope(!sleepEnabled.boolValue))
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.up)), new GUIContent("Facing Up"));
                    EditorGUILayout.PropertyField(sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.down)), new GUIContent("Facing Down"));
                    EditorGUILayout.PropertyField(sleep.FindPropertyRelative(nameof(EasyLoco.SleepSet.side)), new GUIContent("On Side (Left)"));
                }
            }

            EditorGUILayout.Space();
            showAfk = DrawFoldout(AfkFoldoutKey, "AFK Animations", showAfk, ref showAfkHelp);
            if (showAfk)
            {
                DrawHelp(showAfkHelp, "AFK is branched by posture at runtime. Leave a clip empty to keep the built-in default for that stage.");
                DrawAfkSet("Stand AFK", standAfk);
                DrawAfkSet("Crouch AFK", crouchAfk);
                DrawAfkSet("Prone AFK", proneAfk);
            }

            serializedObject.ApplyModifiedProperties();
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
        //
        // An optional section can pass its on/off property as <paramref name="toggle"/>; it draws as
        // a checkbox left of the (i), so the state stays readable with the section collapsed. Its
        // rect is carved out of the header the same way, so it never competes with the foldout click.
        private static bool DrawFoldout(string key, string label, bool expanded, ref bool helpShown, SerializedProperty toggle = null, GUIContent toggleContent = null)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4f,
                EditorStyles.foldoutHeader);
            var buttonRect = new Rect(rect.xMax - InfoButtonSize, rect.y + 2f, InfoButtonSize, InfoButtonSize);

            var toggleSpace = 0f;
            if (toggle != null)
            {
                toggleSpace = HeaderToggleWidth + 2f;
                var toggleRect = new Rect(buttonRect.x - toggleSpace, rect.y + 2f, HeaderToggleWidth, InfoButtonSize);

                // GUI.Toggle rather than PropertyField: the rect is only wide enough for the
                // checkbox, and PropertyField would spend it on the prefix label instead. The
                // content carries a tooltip and no text, so the box still explains itself on hover.
                EditorGUI.BeginProperty(toggleRect, toggleContent, toggle);
                EditorGUI.BeginChangeCheck();
                var toggled = GUI.Toggle(toggleRect, toggle.boolValue, toggleContent ?? GUIContent.none, EditorStyles.toggle);
                if (EditorGUI.EndChangeCheck())
                {
                    toggle.boolValue = toggled;
                }
                EditorGUI.EndProperty();
            }

            var foldoutRect = new Rect(rect.x, rect.y, rect.width - InfoButtonSize - toggleSpace, rect.height);

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
