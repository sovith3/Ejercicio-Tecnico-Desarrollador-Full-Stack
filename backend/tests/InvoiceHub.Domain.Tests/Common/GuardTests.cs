using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Tests.Common;

/// <summary>Pruebas de las validaciones reutilizables de invariantes (<see cref="Guard"/>).</summary>
public sealed class GuardTests
{
    [Fact]
    public void NotEmpty_ReturnsTrimmedValue()
    {
        Assert.Equal("cliente", Guard.NotEmpty("  cliente  ", "run"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotEmpty_WithBlankValue_ThrowsArgumentException(string? value)
    {
        var exception = Assert.Throws<ArgumentException>(() => Guard.NotEmpty(value, "run"));

        Assert.Equal("run", exception.ParamName);
    }

    [Fact]
    public void NotNull_WithNullReference_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Guard.NotNull<string>(null, "customer"));

        Assert.Equal("customer", exception.ParamName);
    }

    [Fact]
    public void NotNegative_WithPositiveValue_ReturnsSameValue()
    {
        Assert.Equal(1500m, Guard.NotNegative(1500m, "total"));
        Assert.Equal(3, Guard.NotNegative(3, "dias"));
    }

    [Fact]
    public void NotNegative_WithNegativeValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Guard.NotNegative(-0.01m, "total"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Guard.NotNegative(-1, "dias"));
    }

    [Fact]
    public void Positive_WithZeroOrNegative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(0, "numero"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(-7, "numero"));
    }

    [Fact]
    public void Positive_WithPositiveValue_ReturnsSameValue()
    {
        Assert.Equal(42, Guard.Positive(42, "numero"));
    }
}
