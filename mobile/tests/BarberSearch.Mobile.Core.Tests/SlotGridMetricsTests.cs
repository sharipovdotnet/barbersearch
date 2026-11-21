using BarberSearch.Mobile.Core.Layout;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раскладки сетки слотов: число колонок и ширина ячейки по доступной ширине.
/// </summary>
/// <remarks>
/// Ширины — в независимых от плотности единицах, как их получает менеджер раскладки из MAUI.
/// Значения взяты с реальных телефонов: 294 — ширина карточки на экране 360 pt, 347 — на 411 pt.
/// </remarks>
public sealed class SlotGridMetricsTests
{
    [Fact]
    public void Resolve_PhoneCardWidth_ReturnsTwoEqualColumns()
    {
        var layout = SlotGridMetrics.Resolve(294, 110, 6, 8);

        Assert.Equal(2, layout.Columns);
        Assert.Equal(143, layout.SlotWidth);
    }

    [Fact]
    public void Resolve_WidePhoneCardWidth_ReturnsThreeEqualColumns()
    {
        var layout = SlotGridMetrics.Resolve(347, 110, 6, 8);

        Assert.Equal(3, layout.Columns);
        Assert.Equal(110.333, layout.SlotWidth, 3);
    }

    [Fact]
    public void Resolve_WidthSharingColumnsEvenly_PacksThemExactly()
    {
        // 6 × 110 + 5 × 8 = 700: шесть колонок помещаются без остатка.
        var layout = SlotGridMetrics.Resolve(700, 110, 6, 8);

        Assert.Equal(6, layout.Columns);
        Assert.Equal(110, layout.SlotWidth);
    }

    [Fact]
    public void Resolve_TabletWidth_CapsColumnsAtMaximum()
    {
        var layout = SlotGridMetrics.Resolve(760, 110, 6, 8);

        Assert.Equal(6, layout.Columns);
        Assert.Equal(120, layout.SlotWidth);
    }

    [Fact]
    public void Resolve_WidthBelowOneColumn_ReturnsSingleFullWidthColumn()
    {
        var layout = SlotGridMetrics.Resolve(120, 110, 6, 8);

        Assert.Equal(1, layout.Columns);
        Assert.Equal(120, layout.SlotWidth);
    }

    [Fact]
    public void Resolve_NoSpacing_CountsColumnsByMinimumWidthOnly()
    {
        var layout = SlotGridMetrics.Resolve(347, 110, 6, 0);

        Assert.Equal(3, layout.Columns);
        Assert.Equal(115.667, layout.SlotWidth, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Resolve_NoUsableWidth_ReturnsSingleMinimumColumn(double width)
    {
        var layout = SlotGridMetrics.Resolve(width, 110, 6, 8);

        Assert.Equal(1, layout.Columns);
        Assert.Equal(110, layout.SlotWidth);
    }

    [Fact]
    public void Resolve_MaxColumnsBelowOne_StillReturnsOneColumn()
    {
        var layout = SlotGridMetrics.Resolve(347, 110, 0, 8);

        Assert.Equal(1, layout.Columns);
        Assert.Equal(347, layout.SlotWidth);
    }

    [Fact]
    public void Resolve_NonPositiveMinimumWidth_FallsBackToSinglePixel()
    {
        var layout = SlotGridMetrics.Resolve(347, 0, 6, 8);

        Assert.Equal(6, layout.Columns);
        Assert.Equal(51.167, layout.SlotWidth, 3);
    }
}
