namespace Puetsua.VRCEasyLoco.Editor
{
    internal static class EasyLocoConst
    {
        public const string PackageName = "vrchat.puetsuaworkshop.easyloco";
        public const string DisplayName = "EasyLoco";

        public const string GeneratedObjectName = "GeneratedEasyLocoMA";

        // Namespace for EditorPrefs keys holding per-user inspector state (never project data).
        public const string EditorPrefsPrefix = PackageName + ".";

        public const string PackageRoot = "Packages/" + PackageName;
        public const string AnimationsFolder = PackageRoot + "/Animations";
        public const string IdleAnimationsFolder = AnimationsFolder + "/Idle";
        public const string SleepAnimationsFolder = AnimationsFolder + "/Sleeping";
        public const string AfkAnimationsFolder = AnimationsFolder + "/Afk";
        public const string MenusFolder = PackageRoot + "/Menus";
        public const string MainMenuPath = MenusFolder + "/EasyLocoMain.asset";

        // Contact rig that reports head orientation while sleeping. Instantiated under the
        // generated host; the prefab carries its own MA parameter registrations.
        public const string SleepSensorsPrefabPath = PackageRoot + "/Prefabs/SleepLoco.prefab";
        public const string SleepSensorsObjectName = "SleepLoco";

        // Drives the Sleeping state inside the Base controller's Prone sub-state machine. Set from
        // the Sleep Loco toggle in the Sleep sub-menu; the state also releases on Upright, so
        // standing up leaves sleep even while this is still true.
        public const string SleepModeParam = "EasyLocoSleepMode";

        // Drives the FeetLock layer in the Base controller, locking both feet to the animated pose
        // (VRC tracking control). Set from the Feet Lock toggle in the Sleep sub-menu; it only
        // engages while [[SleepModeParam]] is on and Upright is below 0.43 (lying down asleep). The
        // layer releases when sleep ends, when the toggle is cleared, or when Upright passes 0.43 -
        // and the release path's parameter driver clears this back to false, so the toggle never
        // sticks once you are upright or awake.
        public const string FeetLockParam = "EasyLocoFeetLock";

        // Idle-pose selector parameters (one Float per stance, carrying 0..1 - see PoseValue in the
        // builder). Toggle menu items and the nested idle blend trees both reference these by name.
        public const string IdleStandParam = "EasyLocoIdleStand";
        public const string IdleCrouchParam = "EasyLocoIdleCrouch";
        public const string IdleProneParam = "EasyLocoIdleProne";

        // The idle (velocity-zero) clip embedded at the centre of each Default* locomotion blend
        // tree. The builder swaps these for the stance's idle selector.
        public const string StandIdleTarget = "IdleStandDefault";
        public const string CrouchIdleTarget = "IdleCrouchDefault";
        public const string ProneIdleTarget = "IdleProneDefault";

        // Built-in idle clips used to prefill a freshly added component.
        public const string StandDefaultClip = IdleAnimationsFolder + "/IdleStandDefault.anim";
        public const string StandWide1Clip = IdleAnimationsFolder + "/IdleStandWide1.anim";
        public const string StandWide2Clip = IdleAnimationsFolder + "/IdleStandWide2.anim";
        public const string CrouchDefaultClip = IdleAnimationsFolder + "/IdleCrouchDefault.anim";
        public const string CrouchSquattingClip = IdleAnimationsFolder + "/IdleCrouchSquatting.anim";
        public const string ProneDefaultClip = IdleAnimationsFolder + "/IdleProneDefault.anim";
        public const string ProneLyingDownClip = IdleAnimationsFolder + "/IdleProneLyingDown.anim";

        // The sleep pose clips sitting at the leaves of the DefaultSleepingFacing{Up,Down} trees.
        // Each is the motion of one Sleeping state in the Base controller, and the states switch on
        // the facing parameters. Swapping these clips by name lets the existing blend-tree clone
        // path rebuild those trees with the user's clips.
        //
        // The feet-locked branch (DefaultSleepingFacing*FeetLock, played while the Feet Lock toggle
        // holds both feet on the floor) is a parallel pair of trees that reuses the same
        // facing-up/down clips but has its own on-side poses, hence the extra targets. Every clip
        // file is named after its target, so the built-in paths are derived from these names.
        public const string SleepUpTarget = "SleepUp";
        public const string SleepDownTarget = "SleepDown";
        public const string SleepLeftTarget = "SleepLeft";
        public const string SleepRightTarget = "SleepRight";
        public const string SleepLeftFeetLockUpTarget = "SleepLeftFeetLockUp";
        public const string SleepLeftFeetLockDownTarget = "SleepLeftFeetLockDown";
        public const string SleepRightFeetLockUpTarget = "SleepRightFeetLockUp";
        public const string SleepRightFeetLockDownTarget = "SleepRightFeetLockDown";

        // Built-in sleep clips used to prefill a freshly added component.
        public const string SleepUpClip = SleepAnimationsFolder + "/" + SleepUpTarget + ".anim";
        public const string SleepDownClip = SleepAnimationsFolder + "/" + SleepDownTarget + ".anim";
        public const string SleepLeftClip = SleepAnimationsFolder + "/" + SleepLeftTarget + ".anim";
        public const string SleepRightClip = SleepAnimationsFolder + "/" + SleepRightTarget + ".anim";
        public const string SleepLeftFeetLockUpClip = SleepAnimationsFolder + "/" + SleepLeftFeetLockUpTarget + ".anim";
        public const string SleepLeftFeetLockDownClip = SleepAnimationsFolder + "/" + SleepLeftFeetLockDownTarget + ".anim";
        public const string SleepRightFeetLockUpClip = SleepAnimationsFolder + "/" + SleepRightFeetLockUpTarget + ".anim";
        public const string SleepRightFeetLockDownClip = SleepAnimationsFolder + "/" + SleepRightFeetLockDownTarget + ".anim";

        // AFK is branched by posture; each stance state is named "Afk <Stance> <Stage>". The builder
        // swaps these states' motions with the component's per-stance clips.
        public const string AfkStatePrefix = "Afk ";

        // Built-in AFK clips shared as the default for every stance branch.
        public const string AfkEnteringDefaultClip = AfkAnimationsFolder + "/AfkEnteringDefault.anim";
        public const string AfkLoopingDefaultClip = AfkAnimationsFolder + "/AfkLoopingDefault.anim";
        public const string AfkExitingDefaultClip = AfkAnimationsFolder + "/AfkExitingDefault.anim";

        public static readonly string[] LayerNames =
        {
            "Base",
            "Additive",
            "Gesture",
            "Action",
            "FX",
            "Sitting",
            "TPose",
            "IKPose"
        };
    }
}
