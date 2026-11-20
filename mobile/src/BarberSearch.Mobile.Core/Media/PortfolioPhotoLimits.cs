namespace BarberSearch.Mobile.Core.Media;

/// <summary>
/// Пределы портфолио мастера — те же, что у веба и домена.
/// </summary>
/// <remarks>
/// <para>
/// Один набор констант на подготовку файла, форму загрузки и подсказки экрана: расхождение
/// здесь читалось бы как два разных продукта. Значения повторяют
/// <c>frontend/src/lib/portfolio-photos.ts</c> и <c>frontend/src/lib/image-prep.ts</c>:
/// до 10 фотографий, подпись до 2000 символов, файл после подготовки до 5 МиБ, исходник
/// до 15 МиБ, длинная сторона до 1600 px, стороны кадра от 300 до 6000 px.
/// </para>
/// <para>
/// Клиентская граница — не источник истины: те же пределы проверяет сервер, а коды его
/// отказов разбирает <see cref="Api.Barber.PortfolioPhotoErrors"/>. Здесь они нужны, чтобы
/// не отправлять заведомо отклоняемый файл и не показывать кнопку, которая ничего не сделает.
/// </para>
/// </remarks>
public static class PortfolioPhotoLimits
{
    /// <summary>
    /// Доменный предел портфолио: одиннадцатая фотография отклоняется.
    /// </summary>
    public const int MaxPhotos = 10;

    /// <summary>
    /// Предел подписи: та же длина стоит в домене.
    /// </summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>
    /// Предел файла после клиентской подготовки (5 МиБ).
    /// </summary>
    public const int MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Предел исходного файла до декодирования: больше не читаем вовсе.
    /// </summary>
    public const int MaxSourceFileBytes = 15 * 1024 * 1024;

    /// <summary>
    /// Длинная сторона результата: больше не растягиваем и не уменьшаем сверх этого.
    /// </summary>
    public const int MaxSide = 1600;

    /// <summary>
    /// Минимальная сторона кадра, которую принимает сервер.
    /// </summary>
    public const int MinDimension = 300;

    /// <summary>
    /// Максимальная сторона кадра, которую принимает сервер.
    /// </summary>
    public const int MaxDimension = 6000;
}
