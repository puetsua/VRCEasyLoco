using System;
using System.IO;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor
{
    internal static class EasyLocoConst
    {
        public const string PackageName = "vrchat.puetsuaworkshop.easyloco";
        public const string DisplayName = "EasyLoco";

        private static string _cachedVersion = "dev";
        private static bool _versionResolved;

        public static string Version
        {
            get
            {
                if (_versionResolved)
                {
                    return _cachedVersion;
                }

                // Cache failures too: without this every inspector repaint re-reads and re-logs.
                _versionResolved = true;
                try
                {
                    var json = File.ReadAllText(PackageRoot + "/package.json");
                    var info = JsonUtility.FromJson<PackageJson>(json);
                    if (!string.IsNullOrEmpty(info.version))
                    {
                        _cachedVersion = info.version;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                return _cachedVersion;
            }
        }

        [Serializable]
        private struct PackageJson
        {
            public string version;
        }

        public const string GeneratedObjectName = "GeneratedEasyLocoMA";

        // Per-user inspector state. Never commit this prefix into project assets.
        public const string EditorPrefsPrefix = PackageName + ".";

        public const string PackageRoot = "Packages/" + PackageName;
        public const string AnimationsFolder = PackageRoot + "/Animations";
        public const string IdleAnimationsFolder = AnimationsFolder + "/Idle";
        public const string SleepAnimationsFolder = AnimationsFolder + "/Sleeping";
        public const string AfkAnimationsFolder = AnimationsFolder + "/Afk";
        public const string AnimatorsFolder = PackageRoot + "/Animators";
        public const string BaseTemplatePath = AnimatorsFolder + "/EasyLocoBaseTemplate.controller";
        public const string ActionTemplatePath = AnimatorsFolder + "/EasyLocoActionTemplate.controller";
        public const string SleepTemplatePath = AnimatorsFolder + "/EasyLocoSleepTemplate.controller";
        public const string MenusFolder = PackageRoot + "/Menus";
        public const string MainMenuPath = MenusFolder + "/EasyLocoMain.asset";
        public const string ActionMenuPath = MenusFolder + "/Action.asset";
        public const string EntryMenuPath = MenusFolder + "/EasyLocoEntry.asset";

        public const string TexturesFolder = PackageRoot + "/Textures";
        public const string BannerTexturePath = TexturesFolder + "/easylocobanner.png";

        public const string SleepPrefabPath = PackageRoot + "/Prefabs/EasyLocoSleep.prefab";
        public const string SleepObjectName = "EasyLocoSleep";

        public const string SleepModeParam = "EL/SleepMode";
        public const string FeetLockParam = "EL/FeetLock";
        public const string HeightParam = "EL/Height";
        public const string EnableHeightParam = "EL/EnableHeight";
        public const string AdjustHeightParam = "EL/AdjustHeight";
        public const string HeightStateSuffix = " Height";
        public const string PoseSpaceLayer = "PoseSpaceLoopSet";
        public const string PoseSpaceIdleState = "Idle";
        public const string PoseSpaceSleepIdleState = "SleepModeIdle";
        public const string PoseSpaceState = "PoseSpace";
        public const string PoseSpaceRepeatState = "PoseSpaceRepeat";

        public const string EyeHeightAsMetersParam = "EyeHeightAsMeters";
        public const string EyeHeightNormParam = "EL/EyeHeightNorm";
        public const float EyeHeightMetersMin = 0f;
        public const float EyeHeightMetersMax = 5f;
        // ~1.25 m avatar (metres / 5) until the Normalize clip writes the live value.
        public const float EyeHeightNormDefault = 0.25f;

        public const string EyeHeightNormLayer = "EyeHeightNorm";
        public const string EyeHeightNormState = "Normalize";
        public const string EyeHeightNormZeroClip = SleepAnimationsFolder + "/EyeHeightNorm0.anim";
        public const string EyeHeightNormOneClip = SleepAnimationsFolder + "/EyeHeightNorm1.anim";

        public const string IdleStandParam = "EL/IdleStand";
        public const string IdleCrouchParam = "EL/IdleCrouch";
        public const string IdleProneParam = "EL/IdleProne";

        // Replacement is scoped to the desktop machine. VR stays on built-in poses so IK is not fought.
        public const string DesktopLocomotionStateMachine = "Desktop Locomotion";
        public const string VrLocomotionStateMachine = "VR Locomotion";

        public const string StandIdleTarget = "IdleStandDefault";
        public const string CrouchIdleTarget = "IdleCrouchDefault";
        public const string ProneIdleTarget = "IdleProneDefault";
        public const string VrCrouchIdleTarget = "IdleCrouchSquatting";

        public const string StandDefaultClip = IdleAnimationsFolder + "/IdleStandDefault.anim";
        public const string StandWide1Clip = IdleAnimationsFolder + "/IdleStandWide1.anim";
        public const string StandWide2Clip = IdleAnimationsFolder + "/IdleStandWide2.anim";
        public const string CrouchDefaultClip = IdleAnimationsFolder + "/IdleCrouchDefault.anim";
        public const string CrouchSquattingClip = IdleAnimationsFolder + "/IdleCrouchSquatting.anim";
        public const string ProneDefaultClip = IdleAnimationsFolder + "/IdleProneDefault.anim";
        public const string ProneLyingDownClip = IdleAnimationsFolder + "/IdleProneLyingDown.anim";

        public const string SleepUpTarget = "SleepUp";
        public const string SleepDownTarget = "SleepDown";
        public const string SleepSideFacingUpTarget = "SleepSideFacingUp";
        public const string SleepSideFacingDownTarget = "SleepSideFacingDown";
        public const string SleepSideFacingUpFeetLockTarget = "SleepSideFacingUpFeetLock";
        public const string SleepSideFacingDownFeetLockTarget = "SleepSideFacingDownFeetLock";

        public const string SleepUpClip = SleepAnimationsFolder + "/" + SleepUpTarget + ".anim";
        public const string SleepDownClip = SleepAnimationsFolder + "/" + SleepDownTarget + ".anim";
        public const string SleepSideClip = SleepAnimationsFolder + "/SleepSide.anim";

        public static readonly string[] SleepSideTargets =
        {
            SleepSideFacingUpTarget,
            SleepSideFacingDownTarget,
            SleepSideFacingUpFeetLockTarget,
            SleepSideFacingDownFeetLockTarget,
        };

        public static string SleepSidePlaceholderClip(string target)
        {
            return SleepAnimationsFolder + "/" + target + ".anim";
        }

        public const string GeneratedSleepClipPrefix = "EL";

        public static readonly string[] SleepTargets =
        {
            SleepUpTarget,
            SleepDownTarget,
            SleepSideFacingUpTarget,
            SleepSideFacingDownTarget,
            SleepSideFacingUpFeetLockTarget,
            SleepSideFacingDownFeetLockTarget,
        };

        public static string GeneratedSleepClipName(string slot, bool is5m)
        {
            return GeneratedSleepClipPrefix + slot + (is5m ? "5m" : string.Empty);
        }

        public const string AfkStatePrefix = "Afk ";
        public static readonly string[] AfkStances = { "Stand", "Crouch", "Prone" };
        public static readonly string[] AfkStages = { "Entering", "Looping", "Exiting" };

        public static string AfkStateName(string stance, string stage)
        {
            return AfkStatePrefix + stance + " " + stage;
        }

        public const string AfkEnteringDefaultClip = AfkAnimationsFolder + "/AfkEnteringDefault.anim";
        public const string AfkLoopingDefaultClip = AfkAnimationsFolder + "/AfkLoopingDefault.anim";
        public const string AfkExitingDefaultClip = AfkAnimationsFolder + "/AfkExitingDefault.anim";
    }
}
