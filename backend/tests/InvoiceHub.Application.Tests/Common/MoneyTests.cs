using InvoiceHub.Application.Common;

namespace InvoiceHub.Application.Tests.Common;

/// <summary>
/// Pruebas de <see cref="Money"/>: la conversion de montos a unidades enteras (cinco decimales de peso)
/// es la base de la exactitud aritmetica exigida (sumas, saldos y comparaciones sin errores de redondeo
/// binario).
/// </summary>
public sealed class MoneyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.00001, 1)]
    [InlineData(1, 100_000)]
    [InlineData(1500, 150_000_000)]
    [InlineData(1234.56, 123_456_000)]
    [InlineData(9_999_999_999.99999, 999_999_999_999_999)]
    public void ToUnits_ConvertsAndRoundsToIntegerUnits(decimal amount, long expected)
    {
        Assert.Equal(expected, Money.ToUnits(amount));
    }

    [Theory]
    [InlineData(0.000005, 1)]
    [InlineData(-0.000005, -1)]
    [InlineData(1234.567894, 123_456_789)]
    [InlineData(1234.567896, 123_456_790)]
    public void ToUnits_RoundsHalfAwayFromZero(decimal amount, long expected)
    {
        Assert.Equal(expected, Money.ToUnits(amount));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100_000, 1)]
    [InlineData(150_000_000, 1500)]
    [InlineData(999_999_999_999_999, 9_999_999_999.99999)]
    public void FromUnits_ConvertsBackToAmount(long units, decimal expected)
    {
        Assert.Equal(expected, Money.FromUnits(units));
    }

    [Theory]
    [InlineData(0.00001)]
    [InlineData(1999.99999)]
    [InlineData(1_234_567.89)]
    public void RoundTrip_PreservesTheAmount(decimal amount)
    {
        Assert.Equal(amount, Money.FromUnits(Money.ToUnits(amount)));
    }

    [Fact]
    public void UnitsPerPeso_MatchesTheDeclaredPrecision()
    {
        Assert.Equal(Money.Decimals, 5);
        Assert.Equal(100_000m, Money.UnitsPerPeso);
    }
}
