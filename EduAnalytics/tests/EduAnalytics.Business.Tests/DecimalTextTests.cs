using System.Globalization;
using EduAnalytics.Business.Helpers;

namespace EduAnalytics.Business.Tests;

public class DecimalTextTests
{
    [Theory]
    [InlineData("7,5", 7.5)]
    [InlineData("7.5", 7.5)]
    [InlineData("25,5", 25.5)]
    [InlineData("26,00", 26)]      // geçerli kültürde binlik sanılırsa 2600 olurdu
    [InlineData("26.00", 26)]
    [InlineData(" 30 ", 30)]
    [InlineData("0", 0)]
    [InlineData(",5", 0.5)]
    [InlineData("7,", 7)]
    [InlineData("7,555", 7.56)]    // iki basamağa yuvarlanır
    public void Comma_and_dot_are_both_decimal_separators(string text, double expected)
    {
        Assert.True(DecimalText.TryParse(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Parsing_does_not_depend_on_the_machine_culture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(cultureName);
        try
        {
            Assert.True(DecimalText.TryParse("7,5", out var comma));
            Assert.True(DecimalText.TryParse("7.5", out var dot));
            Assert.Equal(7.5m, comma);
            Assert.Equal(7.5m, dot);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_text_is_valid_and_means_no_score(string? text)
    {
        Assert.True(DecimalText.TryParse(text, out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("1.234,5")]   // binlik ayırıcı kabul edilmez
    [InlineData("1,234.5")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("7;5")]
    [InlineData("abc")]
    [InlineData(",")]
    [InlineData("1e3")]
    public void Ambiguous_or_invalid_text_is_rejected(string text)
    {
        Assert.False(DecimalText.TryParse(text, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Format_uses_the_current_culture_without_trailing_zeros()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            Assert.Equal("26", DecimalText.Format(26.00m));
            Assert.Equal("25,5", DecimalText.Format(25.50m));
            Assert.Equal(string.Empty, DecimalText.Format(null));

            // Yazılan her biçim aynı değere geri okunur.
            Assert.True(DecimalText.TryParse(DecimalText.Format(25.50m), out var roundTrip));
            Assert.Equal(25.5m, roundTrip);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
