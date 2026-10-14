using KnowBall.Application.Text;

namespace KnowBall.UnitTests;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Nikola Jokić", "nikola jokic")]
    [InlineData("Luka Dončić", "luka doncic")]
    [InlineData("Alperen Şengün", "alperen sengun")]
    [InlineData("Shai Gilgeous-Alexander", "shai gilgeous alexander")]
    [InlineData("De'Aaron Fox", "deaaron fox")]
    [InlineData("Michael Porter Jr.", "michael porter jr")]
    [InlineData("  LeBron   JAMES ", "lebron james")]
    [InlineData("Kristaps Porziņģis", "kristaps porzingis")]
    [InlineData("Jusuf Nurkić", "jusuf nurkic")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalizes(string? input, string expected) =>
        Assert.Equal(expected, NameNormalizer.Normalize(input));
}
