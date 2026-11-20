namespace BarberSearch.Mobile.Core.Media;

/// <summary>
/// Выбранное изображение не удалось подготовить к загрузке.
/// </summary>
/// <remarks>
/// Исключение несёт машинный код, а не готовый текст: пикер живёт в приложении и не
/// отвечает за локализацию. <see cref="Api.Barber.PortfolioPhotoErrors.DescribePicker"/>
/// превращает код в строку у поля «Файл», когда человек уже выбрал файл.
/// </remarks>
public sealed class PortfolioImageException : Exception
{
    /// <summary>
    /// Создаёт исключение с причиной подготовки.
    /// </summary>
    /// <param name="kind">Причина, по которой файл не подготовлен.</param>
    /// <param name="innerException">Исходная ошибка декодирования или кодирования.</param>
    public PortfolioImageException(
        PortfolioImageError kind,
        Exception? innerException = null)
        : base("Не удалось подготовить изображение.", innerException)
    {
        Kind = kind;
    }

    /// <summary>
    /// Причина, по которой файл не подготовлен.
    /// </summary>
    public PortfolioImageError Kind { get; }
}
