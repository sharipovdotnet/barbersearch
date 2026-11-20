namespace BarberSearch.Mobile.Core.Media;

/// <summary>
/// Причина, по которой выбранное изображение не удалось подготовить к загрузке.
/// </summary>
/// <remarks>
/// Код машинный, а текст для человека выбирает <see cref="Api.Barber.PortfolioPhotoErrors"/>:
/// пикер живёт в приложении и не знает языка интерфейса, а Core переводит причину в
/// строку у поля «Файл».
/// </remarks>
public enum PortfolioImageError
{
    /// <summary>
    /// Исходный файл больше 15 МиБ: такой кадр незачем читать в память.
    /// </summary>
    SourceTooLarge,

    /// <summary>
    /// Формат не JPEG, PNG или WebP.
    /// </summary>
    UnsupportedType,

    /// <summary>
    /// Файл не декодируется: повреждён или это не изображение.
    /// </summary>
    InvalidImage,

    /// <summary>
    /// После перекодирования файл всё ещё больше 5 МиБ.
    /// </summary>
    TooLarge,
}
