namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Формат изображения, определённый по сигнатуре файла.
/// </summary>
/// <remarks>
/// Значения явные, как у всех перечислений проекта, хотя в БД формат не хранится:
/// расширение и content type — производные от сигнатуры, а не от имени файла.
/// </remarks>
public enum PhotoFormat
{
    /// <summary>JPEG (сигнатура <c>FF D8 FF</c>).</summary>
    Jpeg = 1,

    /// <summary>PNG (сигнатура <c>89 50 4E 47 0D 0A 1A 0A</c>).</summary>
    Png = 2,

    /// <summary>WebP (контейнер <c>RIFF….WEBP</c>).</summary>
    WebP = 3
}
