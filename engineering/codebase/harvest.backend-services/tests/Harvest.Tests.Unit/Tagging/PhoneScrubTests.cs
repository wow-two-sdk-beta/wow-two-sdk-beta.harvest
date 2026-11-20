using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.Tagging;

/// <summary>The phone scrub every prompt passes through: a number becomes <c>[phone]</c>, and prices, years, areas, floors and
/// dates survive. All numbers here are invented.</summary>
public sealed class PhoneScrubTests
{
    [Theory]
    [InlineData("Tel: +998 90 123 45 67", "Tel: [phone]")]
    [InlineData("+998901234567", "[phone]")]
    [InlineData("zvonite +998 (90) 123-45-67 posle 18:00", "zvonite [phone] posle 18:00")]
    [InlineData("+99890 123 45 67.", "[phone].")]
    [InlineData("Позвоните: +7 (999) 123-45-67, Алексей", "Позвоните: [phone], Алексей")]
    [InlineData("call +1 202 555 0143 now", "call [phone] now")]
    [InlineData("WhatsApp: +44 7700 900123", "WhatsApp: [phone]")]
    public void Apply_ShouldReplaceNumbersWithAPlusSign(string text, string expected)
    {
        PhoneScrub.Apply(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("998901234567", "[phone]")]
    [InlineData("998 90 123 45 67", "[phone]")]
    [InlineData("90 123 45 67", "[phone]")]
    [InlineData("90-123-45-67", "[phone]")]
    [InlineData("90.123.45.67", "[phone]")]
    [InlineData("(90) 123-45-67", "[phone]")]
    [InlineData("8 (900) 123-45-67", "[phone]")]
    [InlineData("89001234567", "[phone]")]
    [InlineData("901234567", "[phone]")]
    [InlineData("tel 33 123 4567", "tel [phone]")]
    [InlineData("Тел: 901 234 567", "Тел: [phone]")]
    [InlineData("aloqa uchun 90 123 45 67 ga qo'ng'iroq qiling", "aloqa uchun [phone] ga qo'ng'iroq qiling")]
    [InlineData("Kvartira 2 xonali. 90 123 45 67, 93 765 43 21", "Kvartira 2 xonali. [phone], [phone]")]
    public void Apply_ShouldReplaceUzbekAndLocalFormats(string text, string expected)
    {
        PhoneScrub.Apply(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("901234567", "[phone]")]
    [InlineData("90 123 45 67", "[phone]")]
    [InlineData("(90) 123-45-67", "[phone]")]
    [InlineData("901 234 567", "[phone]")]
    [InlineData("90.123.45.67", "[phone]")]
    [InlineData("90-123-45-67", "[phone]")]
    [InlineData("Сдаётся 2-комнатная. 901 234 567, Алексей", "Сдаётся 2-комнатная. [phone], Алексей")]
    [InlineData("Narxi 500 USD. 90.123.45.67 ga yozing", "Narxi 500 USD. [phone] ga yozing")]
    [InlineData("901234567 квартира", "[phone] квартира")]
    public void Apply_ShouldReplaceABareUzbekMobile_WithoutACueWord(string text, string expected)
    {
        PhoneScrub.Apply(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("20")]
    [InlineData("33")]
    [InlineData("50")]
    [InlineData("55")]
    [InlineData("61")]
    [InlineData("62")]
    [InlineData("65")]
    [InlineData("66")]
    [InlineData("67")]
    [InlineData("69")]
    [InlineData("70")]
    [InlineData("71")]
    [InlineData("72")]
    [InlineData("73")]
    [InlineData("74")]
    [InlineData("75")]
    [InlineData("76")]
    [InlineData("77")]
    [InlineData("78")]
    [InlineData("79")]
    [InlineData("88")]
    [InlineData("90")]
    [InlineData("91")]
    [InlineData("93")]
    [InlineData("94")]
    [InlineData("95")]
    [InlineData("97")]
    [InlineData("98")]
    [InlineData("99")]
    public void Apply_ShouldReplaceANineDigitNumber_ThatStartsWithAnOperatorCode(string code)
    {
        PhoneScrub.Apply($"{code}1234567").Should().Be("[phone]");
        PhoneScrub.Apply($"{code} 123 45 67").Should().Be("[phone]");
        PhoneScrub.Apply($"({code}) 123-45-67").Should().Be("[phone]");
        PhoneScrub.Apply($"{code}1 234 567").Should().Be("[phone]");
    }

    [Theory]
    [InlineData("650 000 000 сум")]
    [InlineData("900 000 000 so'm")]
    [InlineData("$85 000")]
    [InlineData("85000 у.е.")]
    [InlineData("год 2019")]
    [InlineData("площадь 72 м2")]
    [InlineData("5/9 этаж")]
    [InlineData("901 234 567 сум")]
    [InlineData("901234567 sum")]
    [InlineData("901234567 UZS")]
    [InlineData("901234567 у.е.")]
    [InlineData("901 234 567 у. е.")]
    [InlineData("901234567 $")]
    [InlineData("901234567$")]
    [InlineData("901234567 usd")]
    [InlineData("901 234 567 so'm")]
    [InlineData("901234567 м2")]
    [InlineData("901234567 m²")]
    [InlineData("901234567 кв.м")]
    [InlineData("901234567 кв")]
    [InlineData("901.234.567 сум")]
    public void Apply_ShouldKeepANineDigitQuantity_ACurrencyOrUnitFollows(string text)
    {
        PhoneScrub.Apply(text).Should().Be(text);
    }

    [Theory]
    [InlineData("Цена 500 USD в месяц")]
    [InlineData("Narxi 500$ oyiga, depozit 500$")]
    [InlineData("Цена: 1 500 000 сум")]
    [InlineData("Цена 12 000 000 сум в месяц")]
    [InlineData("Цена 150 000 000 сум")]
    [InlineData("Цена 1 200 000 000 сум")]
    [InlineData("Narxi 1.200.000.000 so'm")]
    [InlineData("Narxi 150,000,000 so'm")]
    [InlineData("Цена 500000000 сум")]
    [InlineData("Цена 900000000 сум")]
    [InlineData("narxi 770000000")]
    [InlineData("Цена 1200000000")]
    [InlineData("Цена от 5 000 000 до 6 000 000 сум")]
    [InlineData("500 000 000")]
    [InlineData("Год постройки 2019, дом кирпичный")]
    [InlineData("построен в 2015-2016 годах")]
    [InlineData("2018 2019 2020")]
    [InlineData("Площадь 54 м², 54,5 кв.м, жилая 30.5, кухня 9")]
    [InlineData("3/9 этаж, 12 этаж из 16, 5 этажный дом")]
    [InlineData("2+1 комнаты, 3+1, 1+1")]
    [InlineData("Потолки 2,7 м, 2 санузла, 45 м2")]
    [InlineData("Опубликовано 01.12.2026 18.30")]
    [InlineData("с 01.12.2026 по 31.12.2026")]
    [InlineData("12 000 000 3 комнаты")]
    [InlineData("ID: 123456789")]
    [InlineData("Депозит 500 у.е. + коммунальные ~ 100 000 сум")]
    public void Apply_ShouldKeepPricesYearsAreasFloorsAndDates(string text)
    {
        PhoneScrub.Apply(text).Should().Be(text);
    }

    [Fact]
    public void Apply_ShouldKeepALoneNumberThatFollowsAPhone()
    {
        PhoneScrub.Apply("90 123 45 67 2 комнаты").Should().Be("[phone] 2 комнаты");
        PhoneScrub.Apply("+998 90 123 45 67 25 м2").Should().Be("[phone] 25 м2");
        PhoneScrub.Apply("+998901234567 3 этаж").Should().Be("[phone] 3 этаж");
    }

    [Fact]
    public void Apply_ShouldReplaceEveryNumberOfAWholeText_AndLeaveTheRest()
    {
        const string text = "Сдается 2-комнатная квартира, 54 м², 3/9 этаж, 2019 г. Цена 500 USD в месяц, депозит 500.\n"
            + "Звоните: +998 90 123 45 67 или 93 765 43 21 (Алексей). Агентствам не звонить.";

        PhoneScrub.Apply(text).Should().Be("Сдается 2-комнатная квартира, 54 м², 3/9 этаж, 2019 г. Цена 500 USD в месяц, депозит 500.\n"
            + "Звоните: [phone] или [phone] (Алексей). Агентствам не звонить.");
        PhoneScrub.Apply(PhoneScrub.Apply(text)).Should().Be(PhoneScrub.Apply(text), "a scrubbed text is stable");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no numbers at all")]
    public void Apply_ShouldReturnTextWithoutANumberAsItIs(string? text)
    {
        PhoneScrub.Apply(text).Should().Be(text);
    }
}
