namespace Puetsua.VRCEasyLoco.Editor
{
    internal partial class LocalizedTextDataset
    {
        private static readonly LocalizedTextDataset English = new LocalizedTextDataset
        {
            // Inspector chrome
            labelLanguage = "Language",
            labelVersion = "Version",
            tooltipInfoButton = "Show or hide the description for this section.",
            msgNeedsAvatarDescriptor = "This component must be on the same GameObject as the VRCAvatarDescriptor.",
            buttonBuild = "Build Modular Avatar",

            // Idle animations
            sectionIdle = "Idle Animations",
            helpIdle = "Row 0 is the Default pose (its clip may be overridden but it cannot be removed). " +
                       "Add rows to expose extra poses in the Idle Poses menu.",
            headerStandPoses = "Stand Idle Poses",
            headerCrouchPoses = "Crouch Idle Poses",
            headerPronePoses = "Prone Idle Poses",

            // AFK animations
            sectionAfk = "AFK Animations",
            helpAfk = "AFK is branched by posture at runtime. Leave a clip empty to keep the built-in " +
                      "default for that stage.",
            labelStandAfk = "Stand AFK",
            labelCrouchAfk = "Crouch AFK",
            labelProneAfk = "Prone AFK",
            labelAfkEntering = "Entering",
            labelAfkLooping = "Looping",
            labelAfkExiting = "Exiting",

            // Sleep animations
            sectionSleep = "Module - Sleep Animations",
            helpSleep = "Installed with the button below. Once it is on the avatar, Build Modular Avatar " +
                        "rebuilds it too, so a main build keeps it in place. Played while Sleep is toggled on " +
                        "and the avatar is prone. Head orientation blends between the poses. Leave a clip " +
                        "empty to keep the built-in default.\n\n" +
                        "On Side (Left) is the authored pose - lying on the left side; the right side is " +
                        "mirrored from it automatically, and Feet Lock plays the same pose.",
            buttonBuildSleep = "Build and append Sleep Locomotion",
            buttonRemoveSleep = "Remove Sleep Locomotion",
            labelSleepUp = "Facing Up",
            labelSleepDown = "Facing Down",
            labelSleepSide = "On Side (Left)",

            // Dialogs
            dialogOk = "OK",
            msgBuildSucceeded = "Build succeeded — prefab added to the avatar.",
            msgBuildSucceededWithSleep = "Build succeeded — prefab added to the avatar.\n\nThe Sleep module was already installed, so it was rebuilt too. Its menu entry is nested under the EasyLoco menu.",
            msgSleepInstalled = "Sleeping added to the avatar.",

            // Expression menu
            menuIdlePoses = "Idle Poses",
            menuStandPoses = "Stand",
            menuCrouchPoses = "Crouch",
            menuPronePoses = "Prone",
            menuAction = "Action",
            menuDefaultStanding = "Default Standing",
            menuDefaultSitting = "Default Sitting",
            menuSleep = "Sleep",
            menuSleepLoco = "Sleep Loco",
            menuFeetLock = "Feet Lock",
            posePrefix = "Pose ",
            poseDefault = "Default",
            poseStandWide1 = "Wide1",
            poseStandWide2 = "Wide2",
            poseCrouchSquatting = "Squatting",
            poseProneLyingDown = "LyingDown",
        };

        private static readonly LocalizedTextDataset ChineseTraditional = new LocalizedTextDataset
        {
            // Inspector chrome
            labelLanguage = "語言",
            labelVersion = "版本",
            tooltipInfoButton = "顯示或隱藏這個區塊的說明。",
            msgNeedsAvatarDescriptor = "這個元件必須和 VRCAvatarDescriptor 放在同一個 GameObject 上。",
            buttonBuild = "建置 Modular Avatar",

            // Idle animations
            sectionIdle = "靜止動畫",
            helpIdle = "第 0 列是預設姿勢（可以換掉動畫，但不能刪除）。" +
                       "新增列可以把額外的姿勢加進靜止姿勢選單。",
            headerStandPoses = "站立靜止姿勢",
            headerCrouchPoses = "蹲下靜止姿勢",
            headerPronePoses = "趴下靜止姿勢",

            // AFK animations
            sectionAfk = "AFK 動畫",
            helpAfk = "AFK 會依照當下的姿勢分別播放。動畫留空則沿用該段的內建預設。",
            labelStandAfk = "站立 AFK",
            labelCrouchAfk = "蹲下 AFK",
            labelProneAfk = "趴下 AFK",
            labelAfkEntering = "進入",
            labelAfkLooping = "循環",
            labelAfkExiting = "結束",

            // Sleep animations
            sectionSleep = "模組 - 睡覺動畫",
            helpSleep = "由下方的按鈕安裝。一旦裝上，「建置 Modular Avatar」也會一併重建，所以重新建置主模組時會把它留在原位。" +
                        "在睡眠模式開啟且 avatar 趴下時播放，並依頭部方向在各姿勢之間混合。" +
                        "動畫留空則沿用內建預設。\n\n" +
                        "側躺（左）是實際製作的姿勢——身體左側朝下；右側會自動鏡像產生，" +
                        "雙腳鎖定也是播放同一個姿勢。",
            buttonBuildSleep = "建置並附加睡覺動作",
            buttonRemoveSleep = "移除睡覺動作",
            labelSleepUp = "臉朝上",
            labelSleepDown = "臉朝下",
            labelSleepSide = "側躺（左）",

            // Dialogs
            dialogOk = "確定",
            msgBuildSucceeded = "建置成功——prefab 已加入 avatar。",
            msgBuildSucceededWithSleep = "建置成功——prefab 已加入 avatar。\n\n睡覺模組已安裝過，所以也一併重建；它的選單項目會放在 EasyLoco 選單底下。",
            msgSleepInstalled = "已將睡覺動作加入 avatar。",

            // Expression menu
            menuIdlePoses = "靜止姿勢",
            menuStandPoses = "站立姿勢",
            menuCrouchPoses = "蹲下姿勢",
            menuPronePoses = "趴下姿勢",
            menuAction = "動作",
            menuDefaultStanding = "預設站立",
            menuDefaultSitting = "預設坐下",
            menuSleep = "睡覺",
            menuSleepLoco = "睡眠模式",
            menuFeetLock = "雙腳鎖定",
            posePrefix = "姿勢 ",
            poseDefault = "預設",
            poseStandWide1 = "張開 1",
            poseStandWide2 = "張開 2",
            poseCrouchSquatting = "蹲下",
            poseProneLyingDown = "躺下",
        };
    }
}
