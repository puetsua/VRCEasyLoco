using System;
using System.IO;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor
{
    internal static class EasyLocoConst
    {
        public const string PackageName = "vrchat.puetsuaworkshop.easyloco";
        public const string DisplayName = "EasyLoco";

        // Read once from package.json and cached. Falls back to "dev" when the file cannot be read
        // (e.g. the package folder was renamed or the file is missing), so the inspector never
        // throws over a label. The resolved flag is separate from the value so a failed or empty
        // read is cached too - without it every repaint would re-read the file and re-log the
        // exception. Uses JsonUtility rather than Newtonsoft so the package does not have to take a
        // dependency on com.unity.nuget.newtonsoft-json just to read one field.
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

        // Namespace for EditorPrefs keys holding per-user inspector state (never project data).
        public const string EditorPrefsPrefix = PackageName + ".";

        public const string PackageRoot = "Packages/" + PackageName;
        public const string AnimationsFolder = PackageRoot + "/Animations";
        public const string IdleAnimationsFolder = AnimationsFolder + "/Idle";
        public const string SleepAnimationsFolder = AnimationsFolder + "/Sleeping";
        public const string AfkAnimationsFolder = AnimationsFolder + "/Afk";
        public const string MenusFolder = PackageRoot + "/Menus";
        public const string MainMenuPath = MenusFolder + "/EasyLocoMain.asset";
        public const string ActionMenuPath = MenusFolder + "/Action.asset";
        public const string SleepMenuPath = MenusFolder + "/EasyLocoSleep.asset";

        // The Sleep sub-menu is installed by a menu installer on the sleep prefab rather than sitting
        // in EasyLocoMain's controls, so an avatar without the sleeping module never shows the entry.
        // The build retargets that installer: under EasyLocoMain when the avatar has the main prefab,
        // at the root menu when it does not. Recorded here for the reader - the menu asset itself is
        // referenced from the prefab, so nothing in code loads this path.
        public const string SleepEntryMenuPath = MenusFolder + "/EasyLocoSleepEntry.asset";

        // Header artwork drawn at the top of the component inspector. Authored at 400x80; the
        // inspector scales it down to the panel width and never draws it larger than that.
        public const string TexturesFolder = PackageRoot + "/Textures";
        public const string BannerTexturePath = TexturesFolder + "/easylocobanner.png";

        // The whole sleep feature as one droppable unit: the contact rig that reports head
        // orientation, plus the Modular Avatar components that install sleeping - its parameters,
        // its sub-menu, and the animator appended over the base locomotion.
        //
        // Only the animator is avatar-specific, so the build saves a per-avatar copy of this prefab
        // with that one reference repointed at the generated controller. That copy is what the sleep
        // button puts on the avatar - inside the generated host, so everything EasyLoco installs sits
        // under one object, or beside the descriptor when the avatar has no host yet. It goes on as
        // an added child of the host instance, which survives rebuilding the host prefab but does not
        // travel with that prefab: dropping the host on another avatar does not bring sleeping.
        //
        // Its Merge Animator carries Layer Priority 100. Being appended only guarantees the sleep
        // layers land after EasyLoco's own base locomotion (Modular Avatar always merges the Replace
        // first); among appended layers the order is otherwise hierarchy order, so any other tool
        // adding a Base layer at the default priority could land after the sleeping pose and
        // override it. The positive priority puts sleeping last on purpose - it plays an empty clip
        // whenever the avatar is awake, so sitting on top costs nothing. Anything that genuinely has
        // to win over the sleeping pose needs a priority above this.
        public const string SleepPrefabPath = PackageRoot + "/Prefabs/EasyLocoSleep.prefab";
        public const string SleepObjectName = "EasyLocoSleep";

        // The two sleep toggles. Both are registered by the Modular Avatar Parameters component on
        // the sleep prefab, so no code adds them - these names exist to keep the prefab, the menu
        // assets, and the sleep template describing the same contract.
        //
        // Drives the Sleeping state inside the sleep controller's Prone sub-state machine. Set from
        // the Sleep Loco toggle in the Sleep sub-menu; the state also releases on Upright, so
        // standing up leaves sleep even while this is still true.
        public const string SleepModeParam = "EasyLocoSleepMode";

        // Drives the FeetLock layer in the sleep controller, locking both feet to the animated pose
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
        // Each is the motion of one Sleeping state in the sleep controller, and the states switch on
        // the facing parameters. Swapping these clips by name lets the existing blend-tree clone
        // path rebuild those trees with the user's clips.
        //
        // The on-side pose sits in every tree twice - once plain, once with the child's Mirror flag
        // set - so one pose covers both sides. SleepSide lies on the left (right side up, so it
        // plays at EasyLocoFacingRight) and the clips are humanoid, so Unity mirrors it exactly.
        //
        // Each tree holds its own on-side placeholder rather than sharing one clip, because the
        // slots differ in Root Transform Rotation: the free branch sits halfway between its facing
        // clips (SleepUp at 0, SleepDown at -180), while the feet-locked branch matches its facing
        // clip, since Feet Lock puts both feet on Animation and they land where that pose puts them.
        // Those offsets live in the placeholder assets, not in code - so a placeholder can be
        // re-yawed, re-posed, or swapped for a new motion in the Inspector and the build follows,
        // and the tree's own positions and mirror flags are never touched by the builder.
        //
        // Every clip file is named after its target, so the built-in paths follow from these names.
        public const string SleepUpTarget = "SleepUp";
        public const string SleepDownTarget = "SleepDown";
        public const string SleepSideFacingUpTarget = "SleepSideFacingUp";
        public const string SleepSideFacingDownTarget = "SleepSideFacingDown";
        public const string SleepSideFacingUpFeetLockTarget = "SleepSideFacingUpFeetLock";
        public const string SleepSideFacingDownFeetLockTarget = "SleepSideFacingDownFeetLock";

        // Built-in sleep clips used to prefill a freshly added component.
        public const string SleepUpClip = SleepAnimationsFolder + "/" + SleepUpTarget + ".anim";
        public const string SleepDownClip = SleepAnimationsFolder + "/" + SleepDownTarget + ".anim";

        // The reference on-side pose. Not in any tree itself - it is what the placeholders were cut
        // from, and what the component's On Side slot defaults to.
        public const string SleepSideClip = SleepAnimationsFolder + "/SleepSide.anim";

        // The per-slot placeholders, in the order the builder replaces them.
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
