using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

namespace Harvest.Tests.Unit.Entities;

/// <summary>Name normalization: one event's Russian, Uzbek Latin, Uzbek Cyrillic and English names share one key, with
/// years and ordinals stripped; places compare by stem.</summary>
public sealed class EntityNamesTests
{
    [Theory]
    [InlineData("Tashkent Fashion Week")]
    [InlineData("Tashkent Fashion Week 2026")]
    [InlineData("9th Tashkent Fashion Week")]
    [InlineData("Fashion Week Tashkent IX")]
    [InlineData("Ташкентская неделя моды 2026")]
    [InlineData("Неделя моды в Ташкенте")]
    [InlineData("9-я Неделя моды Ташкент")]
    [InlineData("Toshkent moda haftaligi 2026")]
    [InlineData("Toshkent moda haftaligi 9-chi")]
    [InlineData("Тошкент мода ҳафталиги")]
    [InlineData("TOSHKENT MODA HAFTALIGʻI")]
    public void Key_ShouldCollapseEveryFormOfOneName(string name)
    {
        EntityNames.Key(name).Should().Be("fashion tashkent week");
    }

    [Fact]
    public void Key_ShouldTransliterateUzbekLettersAndApostrophes_TheSameInBothScripts()
    {
        EntityNames.Key("O‘zbekiston to‘qimachilik ko'rgazmasi").Should().Be(EntityNames.Key("Ўзбекистон тўқимачилик кўргазмаси"));
        EntityNames.Key("Ўзбекистон тўқимачилик кўргазмаси").Should().Be("exhibition textile uzbekistan");
        EntityNames.Key("Международная выставка текстиля 2025").Should().Be("exhibition international tekstilya");
        EntityNames.Key("Xalqaro tekstil yarmarkasi").Should().Be("fair international textile");
    }

    [Theory]
    [InlineData("2026", "")]
    [InlineData("12th 2026", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("Visa Fashion Week Tashkent", "fashion tashkent visa week")]
    [InlineData("Samarqand moda haftaligi", "fashion samarkand week")]
    [InlineData("Expo 2026 Bukhara", "bukhara expo")]
    public void Key_ShouldDropYearsAndNumbers_AndKeepOtherWords(string? name, string expected)
    {
        EntityNames.Key(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("Yunusobod tumani", "Юнусабадский район", true)]
    [InlineData("Chilonzor", "Чиланзар", true)]
    [InlineData("Toshkent shahri", "Tashkent", true)]
    [InlineData("Mirzo Ulug'bek", "Мирзо-Улугбекский район", true)]
    [InlineData("Chilonzor", "Yunusobod", false)]
    [InlineData("Tashkent", "Samarkand", false)]
    [InlineData("", "Tashkent", false)]
    public void SamePlace_ShouldCompareStemsAcrossScripts(string left, string right, bool expected)
    {
        EntityNames.SamePlace(EntityNames.PlaceKey(left), EntityNames.PlaceKey(right)).Should().Be(expected);
    }

    [Theory]
    [InlineData("2026", 2026)]
    [InlineData("SS 2027", 2027)]
    [InlineData("9th", null)]
    [InlineData(null, null)]
    public void Year_ShouldReadAFourDigitYear(string? label, int? expected)
    {
        EntityNames.Year(label).Should().Be(expected);
    }
}
