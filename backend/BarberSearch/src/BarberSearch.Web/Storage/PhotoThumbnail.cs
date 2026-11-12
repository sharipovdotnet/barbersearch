using BarberSearch.Application.PortfolioPhotos;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Готовый производный вариант: представление, байты WebP и фактические стороны.
/// </summary>
/// <remarks>
/// Стороны нужны тестам и журналу; в ответ они не попадают — клиент узнаёт ширину из
/// дескрипторов <c>srcset</c>, которые зафиксированы в <see cref="PhotoVariantWidths"/>.
/// </remarks>
/// <param name="Variant">Представление: <c>sm</c>, <c>md</c> или <c>lg</c>.</param>
/// <param name="Content">Байты WebP-файла без метаданных.</param>
/// <param name="Width">Ширина варианта в пикселях.</param>
/// <param name="Height">Высота варианта в пикселях (пропорции исходника сохранены).</param>
public sealed record PhotoThumbnail(
    PhotoVariant Variant,
    ReadOnlyMemory<byte> Content,
    int Width,
    int Height);
