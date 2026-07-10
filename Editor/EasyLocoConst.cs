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
