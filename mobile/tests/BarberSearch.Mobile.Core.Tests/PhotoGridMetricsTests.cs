using BarberSearch.Mobile.Core.Layout;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раскладки сетки работ: число колонок и сторона квадратной плитки по доступной ширине.
/// </summary>
/// <remarks>
/// Ширины — в независимых от плотности единицах, как их получает менеджер раскладки из MAUI.
/// 256 — ширина внутри карточки на экране 320 pt, 294 и 296 — на 360 pt, 347 — на 411 pt.
/// </remarks>
public sealed class PhotoGridMetricsTests
{
    private const double Phone320ContentWidth = 256;
    private const double Phone360ContentWidth = 294;
    private const double Phone411ContentWidth = 347;

    [Fact]
    public void Resolve_Phone320ContentWidth_ReturnsTwoTilesOf124()
    {
        var layout = PhotoGridMetrics.Resolve(Phone320ContentWidth);

        Assert.Equal(2, layout.Columns);
        Assert.Equal(124, layout.TileSize);
        AssertPacksExactly(Phone320ContentWidth, layout, 8);
    }

    [Theory]
    [InlineData(294, 143)]
    [InlineData(296, 144)]
    public void Resolve_Phone360ContentWidth_ReturnsTwoEqualColumns(double width, double tileSize)
    {
        var layout = PhotoGridMetrics.Resolve(width);

        Assert.Equal(2, layout.Columns);
        Assert.Equal(tileSize, layout.TileSize, 3);
        AssertPacksExactly(width, layout, 8);
    }

    [Fact]
    public void Resolve_WidePhoneContentWidth_KeepsTwoColumns()
    {
        var layout = PhotoGridMetrics.Resolve(Phone411ContentWidth);

        Assert.Equal(2, layout.Columns);
        Assert.Equal(169.5, layout.TileSize, 3);
        AssertPacksExactly(Phone411ContentWidth, layout, 8);
    }

    [Fact]
    public void Resolve_TabletWidth_CapsColumnsAtFour()
    {
        var layout = PhotoGridMetrics.Resolve(700);

        Assert.Equal(4, layout.Columns);
        Assert.Equal(169, layout.TileSize);
        AssertPacksExactly(700, layout, 8);
    }

    [Fact]
    public void Resolve_NoSpacing_MakesTilesWider()
    {
        var spaced = PhotoGridMetrics.Resolve(296);
        var tight = PhotoGridMetrics.Resolve(296, spacing: 0);

        Assert.Equal(2, tight.Columns);
        Assert.Equal(148, tight.TileSize);
        Assert.True(tight.TileSize > spaced.TileSize);
        AssertPacksExactly(296, tight, 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Resolve_NoUsableWidth_ReturnsOneMinimumTile(double width)
    {
        var layout = PhotoGridMetrics.Resolve(width);

        Assert.Equal(1, layout.Columns);
        Assert.Equal(PhotoGridMetrics.DefaultTileWidth, layout.TileSize);
    }

    [Fact]
    public void Resolve_MaxColumnsBelowOne_StillReturnsOneColumn()
    {
        var layout = PhotoGridMetrics.Resolve(Phone411ContentWidth, maximumColumns: 0);

        Assert.Equal(1, layout.Columns);
        Assert.Equal(Phone411ContentWidth, layout.TileSize);
        AssertPacksExactly(Phone411ContentWidth, layout, 8);
    }

    [Fact]
    public void Resolve_NonPositiveMinimumWidth_FallsBackToOnePoint()
    {
        // Предел в 100 колонок не мешает: при минимуме в 1 pt и без промежутков
        // в 50 pt помещается ровно 50 плиток по 1 pt.
        var layout = PhotoGridMetrics.Resolve(50, 0, 100, 0);

        Assert.Equal(50, layout.Columns);
        Assert.Equal(1, layout.TileSize);
        AssertPacksExactly(50, layout, 0);
    }

    private static void AssertPacksExactly(double width, PhotoGridLayout layout, double spacing)
    {
        Assert.True(layout.TileSize > 0);

        var packed = layout.Columns * layout.TileSize + (layout.Columns - 1) * spacing;

        Assert.Equal(width, packed, 0.01);
    }
}
