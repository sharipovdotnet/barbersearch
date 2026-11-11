using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Разбор <c>?size=</c> у маршрутов раздачи: только три известных значения.
/// </summary>
/// <remarks>
/// Пользовательских размеров нет: неизвестное имя, пустое значение и повторённый параметр —
/// не «первое из двух», а отказ. Иначе один и тот же URL отдавал бы разное представление.
/// </remarks>
public class MediaDeliveryTests
{
    [Fact]
    public void TryParseVariant_WithoutSize_ReturnsOriginal()
    {
        var parsed = MediaDelivery.TryParseVariant(
            new QueryCollection(),
            out var variant);

        Assert.True(parsed);
        Assert.Equal(PhotoVariant.Original, variant);
    }

    [Theory]
    [InlineData("sm", PhotoVariant.Small)]
    [InlineData("md", PhotoVariant.Medium)]
    [InlineData("lg", PhotoVariant.Large)]
    public void TryParseVariant_KnownSize_ReturnsVariant(string value, PhotoVariant expected)
    {
        var parsed = MediaDelivery.TryParseVariant(CreateQuery(value), out var variant);

        Assert.True(parsed);
        Assert.Equal(expected, variant);
    }

    [Theory]
    [InlineData("SM")]
    [InlineData("xs")]
    [InlineData("")]
    [InlineData("mdx")]
    [InlineData("  md")]
    public void TryParseVariant_UnknownSize_IsRejected(string value)
    {
        var parsed = MediaDelivery.TryParseVariant(CreateQuery(value), out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryParseVariant_RepeatedSize_IsRejected()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            [PhotoThumbnails.SIZE_QUERY_PARAMETER] = new(["sm", "md"])
        });

        var parsed = MediaDelivery.TryParseVariant(query, out _);

        Assert.False(parsed);
    }

    private static QueryCollection CreateQuery(string value)
        => new(new Dictionary<string, StringValues>
        {
            [PhotoThumbnails.SIZE_QUERY_PARAMETER] = value
        });
}
