namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Результат сохранения файла: публичный URL, по которому он будет раздаваться.
/// </summary>
/// <param name="Url">Относительный media-URL вида <c>/api/media/portfolio/…</c>.</param>
public sealed record StoredPhoto(string Url);
