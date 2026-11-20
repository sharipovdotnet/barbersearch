namespace BarberSearch.Mobile.Core.Media;

/// <summary>
/// Выбор и подготовка фотографии портфолио на телефоне.
/// </summary>
/// <remarks>
/// <para>
/// Реализация живёт в приложении: системный файловый пикер и платформенный декодер —
/// зависимости MAUI, которых в Core быть не должно. Core лишь просит готовый JPEG и
/// раскладывает отказ на поле формы.
/// </para>
/// <para>
/// Возврат <c>null</c> — человек закрыл пикер, не выбрав файл; это не ошибка и не повод
/// показывать текст. Отказ подготовки приходит <see cref="PortfolioImageException"/> с
/// машинным кодом, чтобы Core выбрал русскую или английскую строку сам.
/// </para>
/// </remarks>
public interface IPortfolioPhotoPicker
{
    /// <summary>
    /// Открывает системный выбор файла и готовит выбранный кадр к загрузке.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены: уход со страницы закрывает пикер.</param>
    /// <returns>
    /// Подготовленный JPEG либо <c>null</c>, если человек отменил выбор.
    /// </returns>
    /// <exception cref="PortfolioImageException">Файл не удалось прочитать или подготовить.</exception>
    Task<PortfolioImage?> PickAsync(CancellationToken cancellationToken = default);
}
