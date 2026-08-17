using System;
using UnityEditor;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor
{
    /// <summary>Inspector languages. Prefs store the enum's numeric value — append, do not reorder.</summary>
    internal enum SupportedLanguage
    {
        [InspectorName("English")]
        English,

        [InspectorName("正體中文")]
        ChineseTraditional,
    }

    /// <summary>
    /// Inspector strings, one instance per language. Missing fields stay blank — there is no English fallback.
    /// </summary>
    internal partial class LocalizedTextDataset
    {
        public static LocalizedTextDataset primary;

        /// <summary>
        /// Every language dataset. Filled in the static constructor so partial-class field initializers have already run.
        /// </summary>
        public static readonly LocalizedTextDataset[] All;

        public static SupportedLanguage Current { get; private set; }

        static LocalizedTextDataset()
        {
            All = new[] { English, ChineseTraditional };
            SetLanguage(LoadLanguage());
        }

        private const string LanguagePrefKey = EasyLocoConst.EditorPrefsPrefix + "Language";

        private static SupportedLanguage LoadLanguage()
        {
            return Sanitize(EditorPrefs.GetInt(LanguagePrefKey, (int)SupportedLanguage.English));
        }

        public static void SaveLanguage(SupportedLanguage language)
        {
            EditorPrefs.SetInt(LanguagePrefKey, (int)language);
        }

        /// <summary>
        /// Unknown stored prefs become English. Must not throw: this runs from the static constructor.
        /// </summary>
        internal static SupportedLanguage Sanitize(int stored)
        {
            return Enum.IsDefined(typeof(SupportedLanguage), stored)
                ? (SupportedLanguage)stored
                : SupportedLanguage.English;
        }

        public string
            labelLanguage,
            labelVersion,
            tooltipInfoButton,
            msgNeedsAvatarDescriptor,
            buttonBuild,

            sectionIdle,
            helpIdle,
            headerStandPoses,
            headerCrouchPoses,
            headerPronePoses,

            sectionAfk,
            helpAfk,
            labelStandAfk,
            labelCrouchAfk,
            labelProneAfk,
            labelAfkEntering,
            labelAfkLooping,
            labelAfkExiting,

            sectionSleep,
            helpSleep,
            buttonBuildSleep,
            buttonRemoveSleep,
            labelSleepUp,
            labelSleepDown,
            labelSleepSide,

            dialogOk,
            msgBuildSucceeded,
            msgBuildSucceededWithSleep,
            msgSleepInstalled,

            menuIdlePoses,
            menuStandPoses,
            menuCrouchPoses,
            menuPronePoses,
            menuAction,
            menuDefaultStanding,
            menuDefaultSitting,
            menuSleep,
            menuSleepLoco,
            menuFeetLock,
            menuAdjustHeight,
            menuEnableHeight,
            posePrefix,
            poseDefault,
            poseStandWide1,
            poseStandWide2,
            poseCrouchSquatting,
            poseProneLyingDown;

        public static void SetLanguage(SupportedLanguage language)
        {
            Current = language;
            switch (language)
            {
                case SupportedLanguage.English:
                    primary = English;
                    break;
                case SupportedLanguage.ChineseTraditional:
                    primary = ChineseTraditional;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(language), language, null);
            }
        }
    }
}
