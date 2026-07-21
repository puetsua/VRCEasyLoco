namespace Puetsua.VRCEasyLoco.Editor
{
    internal static class EasyLocoConst
    {
        public const string PackageName = "vrchat.puetsuaworkshop.easyloco";
        public const string DisplayName = "EasyLoco";
        public const string MenuPath = "Tools/EasyLoco";
        public const int MenuPriority = 1124;

        public const string GeneratedObjectName = "GeneratedEasyLocoMA";

        public const string PackageRoot = "Packages/" + PackageName;
        public const string AnimationsFolder = PackageRoot + "/Animations";
        public const string MenusFolder = PackageRoot + "/Menus";
        public const string MainMenuPath = MenusFolder + "/EasyLocoMain.asset";

        // Contact rig that reports head orientation while sleeping. Instantiated under the
        // generated host; the prefab carries its own MA parameter registrations.
        public const string SleepSensorsPrefabPath = PackageRoot + "/Prefabs/SleepLoco.prefab";
        public const string SleepSensorsObjectName = "SleepLoco";

        // Drives the Sleeping state inside the Base controller's Prone sub-state machine. Set from
        // the Sleep toggle on the main menu; the state also releases on Upright, so standing up
        // leaves sleep even while this is still true.
        public const string SleepModeParam = "EasyLocoSleepMode";

        // Idle-pose selector parameters (one Int per stance). Toggle menu items and the nested
        // idle blend trees both reference these by name.
        public const string IdleStandParam = "EasyLocoIdleStand";
        public const string IdleCrouchParam = "EasyLocoIdleCrouch";
        public const string IdleProneParam = "EasyLocoIdleProne";

        // The idle (velocity-zero) clip embedded at the centre of each Default* locomotion blend
        // tree. The builder swaps these for the stance's idle selector.
        public const string StandIdleTarget = "IdleStandDefault";
        public const string CrouchIdleTarget = "IdleCrouchDefault";
        public const string ProneIdleTarget = "IdleProneDefault";

        // Built-in idle clips used to prefill a freshly added component.
        public const string StandDefaultClip = AnimationsFolder + "/IdleStandDefault.anim";
        public const string StandWide1Clip = AnimationsFolder + "/IdleStandWide1.anim";
        public const string StandWide2Clip = AnimationsFolder + "/IdleStandWide2.anim";
        public const string CrouchDefaultClip = AnimationsFolder + "/IdleCrouchDefault.anim";
        public const string CrouchSquattingClip = AnimationsFolder + "/IdleCrouchSquatting.anim";
        public const string ProneDefaultClip = AnimationsFolder + "/IdleProneDefault.anim";
        public const string ProneLyingDownClip = AnimationsFolder + "/IdleProneLyingDown.anim";

        // The sleep pose clips sitting at the leaves of DefaultProneSleeping -> DefaultSleeping ->
        // DefaultSleepingFacing{Up,Down}. Swapping these by name lets the existing blend-tree clone
        // path rebuild the whole nested tree with the user's clips.
        public const string SleepUpTarget = "SleepUp";
        public const string SleepDownTarget = "SleepDown";
        public const string SleepSideTarget = "SleepSide";

        // Built-in sleep clips used to prefill a freshly added component.
        public const string SleepUpClip = AnimationsFolder + "/SleepUp.anim";
        public const string SleepDownClip = AnimationsFolder + "/SleepDown.anim";
        public const string SleepSideClip = AnimationsFolder + "/SleepSide.anim";

        // AFK is branched by posture; each stance state is named "Afk <Stance> <Stage>". The builder
        // swaps these states' motions with the component's per-stance clips.
        public const string AfkStatePrefix = "Afk ";

        // Built-in AFK clips shared as the default for every stance branch.
        public const string AfkEnteringDefaultClip = AnimationsFolder + "/AfkEnteringDefault.anim";
        public const string AfkLoopingDefaultClip = AnimationsFolder + "/AfkLoopingDefault.anim";
        public const string AfkExitingDefaultClip = AnimationsFolder + "/AfkExitingDefault.anim";

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
