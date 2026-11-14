using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Полноэкранный просмотр портфолио: кадры, позиция и подписи.
/// </summary>
/// <remarks>
/// <para>
/// Просмотр получает готовые кадры объектами маршрута — эндпоинта «фото по id» нет,
/// а повторно грузить карточку ради той же галереи бессмысленно. Поэтому база адресов
/// и навигация сюда не попадают: Core умеет только листать список.
/// </para>
/// <para>
/// Индекс приходит из <c>Photos.IndexOf</c> страницы карточки и может оказаться вне
/// диапазона (галерея успела перезагрузиться) — тогда он приводится к краю, а не падает.
/// </para>
/// </remarks>
public sealed partial class PortfolioViewerViewModel : ObservableObject
{
    private IReadOnlyList<BarberPhotoOption> _photos = [];

    /// <summary>
    /// Кадры портфолио в серверном порядке.
    /// </summary>
    public IReadOnlyList<BarberPhotoOption> Photos => _photos;

    /// <summary>
    /// Номер текущего кадра с нуля; <c>CarouselView</c> держит его в двусторонней привязке.
    /// </summary>
    [ObservableProperty]
    private int _position;

    /// <summary>
    /// Счётчик кадров: «3 из 12».
    /// </summary>
    public string CounterText => BarberText.PhotoCounter(Position + 1, _photos.Count);

    /// <summary>
    /// Подпись текущего кадра; пустая, если подписи нет.
    /// </summary>
    public string CaptionText => _photos.Count == 0
        ? string.Empty
        : _photos[Math.Clamp(Position, 0, _photos.Count - 1)].Description ?? string.Empty;

    /// <summary>
    /// Есть ли у текущего кадра подпись.
    /// </summary>
    public bool HasCaption => CaptionText.Length > 0;

    /// <summary>
    /// Заполняет просмотр кадрами и открывает его на нужном номере.
    /// </summary>
    /// <param name="photos">Кадры портфолио.</param>
    /// <param name="index">Номер кадра с нуля; вне диапазона приводится к краю.</param>
    public void Initialize(IReadOnlyList<BarberPhotoOption> photos, int index)
    {
        ArgumentNullException.ThrowIfNull(photos);

        _photos = photos;

        Position = _photos.Count == 0 ? 0 : Math.Clamp(index, 0, _photos.Count - 1);

        OnPropertyChanged(nameof(Photos));
        RaiseTexts();
    }

    partial void OnPositionChanged(int value) => RaiseTexts();

    private void RaiseTexts()
    {
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(CaptionText));
        OnPropertyChanged(nameof(HasCaption));
    }
}
