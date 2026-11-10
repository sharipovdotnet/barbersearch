namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Результат проверки загруженного файла: формат, тип содержимого и размеры.
/// </summary>
/// <remarks>
/// <see cref="ContentType"/> и <see cref="Extension"/> определяются по сигнатуре, а не по
/// имени файла клиента: оригинальное имя не сохраняется и не логируется.
/// </remarks>
/// <param name="Format">Формат изображения.</param>
/// <param name="ContentType">MIME-тип для раздачи файла.</param>
/// <param name="Extension">Расширение без точки: <c>jpg</c>, <c>png</c> или <c>webp</c>.</param>
/// <param name="Width">Ширина в пикселях.</param>
/// <param name="Height">Высота в пикселях.</param>
public sealed record ImageFileInfo(
    PhotoFormat Format,
    string ContentType,
    string Extension,
    int Width,
    int Height);
