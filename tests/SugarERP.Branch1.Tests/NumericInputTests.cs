using SugarERP.Desktop.Shared;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class NumericInputTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ClearedOptionalNumbers_AreZero(string input)
    {
        Assert.True(ArabicDisplay.TryParseMoney(input, out var money));
        Assert.Equal(0, money);
        Assert.True(ArabicDisplay.TryParseQuantity(input, 1, true, out var quantity));
        Assert.Equal(0, quantity);
        Assert.False(ArabicDisplay.TryParseQuantity(input, 1, false, out _));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.001")]
    public void InvalidMoney_IsRejected(string input) =>
        Assert.False(ArabicDisplay.TryParseMoney(input, out _));

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void InvalidPieceQuantity_IsRejected(string input) =>
        Assert.False(ArabicDisplay.TryParseQuantity(input, 1, true, out _));
}
