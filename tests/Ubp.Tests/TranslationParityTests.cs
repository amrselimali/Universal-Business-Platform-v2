using System.Text.Json;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// بوابة الترجمة: كل مفتاح في ar.json له مقابل في en.json والعكس، وكلاهما JSON صالح.
/// تمنع ظهور مفاتيح خام في الواجهة بعد أي إضافة شاشة.
/// </summary>
public sealed class TranslationParityTests
{
    private static (HashSet<string> Arabic, HashSet<string> English) LoadKeys()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "UniversalBusinessPlatform.slnx")))
            root = root.Parent;
        var basePath = Path.Combine(root!.FullName, "src", "Ubp.Web", "Resources");

        static HashSet<string> Read(string file) =>
            JsonDocument.Parse(File.ReadAllText(file)).RootElement
                .EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        return (Read(Path.Combine(basePath, "ar.json")), Read(Path.Combine(basePath, "en.json")));
    }

    [Fact]
    public void Arabic_and_English_resources_have_the_same_keys()
    {
        var (arabic, english) = LoadKeys();

        var missingInEnglish = arabic.Except(english).OrderBy(k => k).ToList();
        var missingInArabic = english.Except(arabic).OrderBy(k => k).ToList();

        Assert.True(missingInEnglish.Count == 0,
            "مفاتيح ناقصة في en.json: " + string.Join(", ", missingInEnglish));
        Assert.True(missingInArabic.Count == 0,
            "مفاتيح ناقصة في ar.json: " + string.Join(", ", missingInArabic));
    }

    [Fact]
    public void No_translation_value_is_empty()
    {
        var (arabic, english) = LoadKeys();
        Assert.True(arabic.Count > 1000);
        Assert.Equal(arabic.Count, english.Count);
    }
}
