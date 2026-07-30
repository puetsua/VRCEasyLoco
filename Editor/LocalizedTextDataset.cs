using System;
using UnityEditor;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor
{
    /// <summary>
    /// The languages the inspector can be shown in. The enum's numeric values are what gets written
    /// to the user's editor prefs, so append new languages at the end rather than reordering these.
    /// </summary>
    internal enum SupportedLanguage
    {
        [InspectorName("English")]
        English,

        [InspectorName("正體中文")]
        ChineseTraditional,
    }

    /// <summary>
    /// Every user-facing string in the inspector, one instance per language. The strings themselves
    /// live in the .Data.cs half of this partial class; this half holds the field list, the language
    /// preference, and the switch that picks the active set.
    ///
    /// There is no per-key fallback: a language whose dataset misses a field shows null, not English.
    /// That is deliberate - a blank label is obvious in the editor, where a silent fallback would let
    /// an untranslated string ship unnoticed. Populate every dataset when adding a field.
    /// </summary>
    internal partial class LocalizedTextDataset
    {
        public static LocalizedTextDataset primary;

        /// <summary>
        /// Every dataset, for recognising a string that was written by an earlier language. A
        /// component filled in under English carries "Wide1", and that still has to read as an
        /// untouched EasyLoco default once the user switches to 正體中文 - otherwise the switch
        /// would mistake its own handiwork for the user's and refuse to update it.
        ///
        /// Populated in the static constructor rather than by a field initializer: the datasets live
        /// in the other half of this partial class, and the order initializers run across the two
        /// files is not guaranteed. A constructor body runs after all of them. Add new languages
        /// here as well as to <see cref="SetLanguage"/>.
        /// </summary>
        public static readonly LocalizedTextDataset[] All;

        /// <summary>
        /// The language in effect. This, not the saved preference, is what the UI draws from: the
        /// pref getter reaches the registry on Windows and the inspector asks once per repaint.
        /// </summary>
        public static SupportedLanguage Current { get; private set; }

        static LocalizedTextDataset()
        {
            All = new[] { English, ChineseTraditional };
            SetLanguage(LoadLanguage());
        }

        // Per-user, not per-project: which language someone reads the inspector in is a property of
        // the person, not the avatar project, so it rides along with the rest of EasyLoco's
        // EditorPrefs state instead of being committed.
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
        /// Maps a stored pref value to a language, falling back to English for anything this build
        /// does not know. Never throws, and that is the point: it feeds the static constructor, and
        /// an exception there is cached by the runtime as a TypeInitializationException that every
        /// later access rethrows - one stale pref would leave the whole tool dead for the rest of
        /// the editor session. Downgrading from a future version with more languages, or a
        /// hand-edited pref, is exactly how a value with no case arrives here.
        /// </summary>
        internal static SupportedLanguage Sanitize(int stored)
        {
            return Enum.IsDefined(typeof(SupportedLanguage), stored)
                ? (SupportedLanguage)stored
                : SupportedLanguage.English;
        }

        public string
            // Inspector chrome
            labelLanguage,
            labelVersion,
            tooltipInfoButton,
            msgNeedsAvatarDescriptor,
            buttonBuild,

            // Idle animations
            sectionIdle,
            helpIdle,
            headerStandPoses,
            headerCrouchPoses,
            headerPronePoses,

            // AFK animations
            sectionAfk,
            helpAfk,
            labelStandAfk,
            labelCrouchAfk,
            labelProneAfk,
            labelAfkEntering,
            labelAfkLooping,
            labelAfkExiting,

            // Sleep animations
            sectionSleep,
            helpSleep,
            buttonBuildSleep,
            buttonRemoveSleep,
            labelSleepUp,
            labelSleepDown,
            labelSleepSide,

            // Dialogs
            dialogOk,
            msgBuildSucceeded,
            msgSleepInstalled,

            // Expression menu labels. Unlike everything above these do not just draw in the
            // inspector - they are baked onto the avatar, so what a player reads in-game is whatever
            // language was active when the component was filled in and built, not the current one.
            // The pose names are only ever *defaults*: once written to the component they are the
            // user's data and switching language leaves them alone.
            menuIdlePoses,
            menuStandPoses,
            menuCrouchPoses,
            menuPronePoses,
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
