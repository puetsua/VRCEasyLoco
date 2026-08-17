using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Puetsua.VRCEasyLoco.Editor.Tests
{
    /// <summary>Every language dataset must fill every string field. There is no fallback.</summary>
    public class LocalizedTextDatasetTests
    {
        private static readonly FieldInfo[] TextFields = typeof(LocalizedTextDataset)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.FieldType == typeof(string))
            .ToArray();

        private SupportedLanguage previousLanguage;

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
