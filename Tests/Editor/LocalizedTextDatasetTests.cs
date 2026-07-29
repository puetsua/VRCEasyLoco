using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>
    /// Guards the dataset contract. There is no per-key fallback by design, so a field nobody filled
    /// in draws as an empty label rather than as English - which is easy to ship without noticing.
    /// These tests are what makes that design safe.
    /// </summary>
    public class LocalizedTextDatasetTests
    {
        private static readonly FieldInfo[] TextFields = typeof(LocalizedTextDataset)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.FieldType == typeof(string))
            .ToArray();

        private SupportedLanguage previousLanguage;

        // SetLanguage writes globals, so every test here puts them back through the same call rather
        // than poking the fields. The saved *preference* (EditorPrefs) is never touched - a test run
        // must not change what the user sees next.
        [SetUp]
        public void SetUp()
        {
            previousLanguage = LocalizedTextDataset.Current;
        }

        [TearDown]
        public void TearDown()
        {
            LocalizedTextDataset.SetLanguage(previousLanguage);
        }

        [Test]
        public void EveryDatasetFillsEveryField()
        {
            Assume.That(TextFields, Is.Not.Empty, "reflection found no string fields to check");

            for (var i = 0; i < LocalizedTextDataset.All.Length; i++)
            {
                var dataset = LocalizedTextDataset.All[i];
                foreach (var field in TextFields)
                {
                    Assert.That((string)field.GetValue(dataset), Is.Not.Null.And.Not.Empty,
                        $"dataset #{i} leaves '{field.Name}' unset");
                }
            }
        }

        [Test]
        public void AllCoversEverySupportedLanguage()
        {
            Assert.That(LocalizedTextDataset.All.Length,
                Is.EqualTo(Enum.GetValues(typeof(SupportedLanguage)).Length),
                "a language was added to the enum but not to LocalizedTextDataset.All");
        }

        [Test]
        public void AllIsPopulatedWithDistinctDatasets()
        {
            // Catches the static-initialization-order trap this array exists to avoid: if All were a
            // field initializer it could run before the datasets in the other partial file and end up
            // holding nulls.
            Assert.That(LocalizedTextDataset.All, Has.None.Null);
            Assert.That(LocalizedTextDataset.All.Distinct().Count(),
                Is.EqualTo(LocalizedTextDataset.All.Length), "the same dataset is listed twice");
        }

        [Test]
        public void SetLanguageHandlesEverySupportedLanguage()
        {
            foreach (SupportedLanguage language in Enum.GetValues(typeof(SupportedLanguage)))
            {
                Assert.DoesNotThrow(() => LocalizedTextDataset.SetLanguage(language),
                    $"SetLanguage has no case for {language}");
                Assert.That(LocalizedTextDataset.primary, Is.Not.Null);
            }
        }

        [Test]
        public void SetLanguageRecordsWhatItSelected()
        {
            foreach (SupportedLanguage language in Enum.GetValues(typeof(SupportedLanguage)))
            {
                LocalizedTextDataset.SetLanguage(language);
                Assert.That(LocalizedTextDataset.Current, Is.EqualTo(language),
                    "Current is what the inspector draws from; it must track SetLanguage");
            }
        }

        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void UnknownStoredLanguageFallsBackToEnglish(int stored)
        {
            // Feeds the static constructor. If this threw, the runtime would cache a
            // TypeInitializationException and every later access to the class would rethrow it,
            // leaving the whole tool dead for the rest of the editor session.
            Assert.That(LocalizedTextDataset.Sanitize(stored), Is.EqualTo(SupportedLanguage.English));
        }

        [Test]
        public void EveryDefinedLanguageSurvivesSanitize()
        {
            foreach (SupportedLanguage language in Enum.GetValues(typeof(SupportedLanguage)))
            {
                Assert.That(LocalizedTextDataset.Sanitize((int)language), Is.EqualTo(language));
            }
        }

        [Test]
        public void EachLanguageSelectsItsOwnDataset()
        {
            var selected = Enum.GetValues(typeof(SupportedLanguage))
                .Cast<SupportedLanguage>()
                .Select(language =>
                {
                    LocalizedTextDataset.SetLanguage(language);
                    return LocalizedTextDataset.primary;
                })
                .ToArray();

            Assert.That(selected.Distinct().Count(), Is.EqualTo(selected.Length),
                "two languages resolve to the same dataset - a copy/paste slip in SetLanguage");
        }
    }
}
