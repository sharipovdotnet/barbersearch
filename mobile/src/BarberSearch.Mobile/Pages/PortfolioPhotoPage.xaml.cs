using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Полноэкранный просмотр портфолио: свайп по кадрам, подпись и счётчик.
/// </summary>
/// <remarks>
/// <para>
/// Просмотр — отдельная страница, а не диалог: у MAUI нет модального лайтбокса, а системная
/// кнопка «назад» закрывает страницу без своего кода. На телефоне листание — свайп;
/// стрелок «Назад»/«Дальше» из веб-диалога здесь нет.
/// </para>
/// <para>
/// Кадры приходят объектами маршрута, как «Изменить отзыв» в срезе 4: эндпоинта «фото по id»
/// нет, а карточка под ними уже загрузила портфолио. Нет кадров или номера — страница сразу
/// закрывается, вместо того чтобы показывать пустой экран.
/// </para>
/// </remarks>
public partial class PortfolioPhotoPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут просмотра в Shell.
    /// </summary>
    public const string Route = "portfolio-photo";

    private readonly PortfolioViewerViewModel _viewModel;

    private bool _queryApplied;

    /// <summary>
    /// Создаёт просмотр.
    /// </summary>
    /// <param name="viewModel">Состояние просмотра.</param>
    public PortfolioPhotoPage(PortfolioViewerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает кадры портфолио и номер выбранного.
    /// </summary>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        if (!query.TryGetValue("photos", out var photosValue)
            || photosValue is not IReadOnlyList<BarberPhotoOption> photos
            || photos.Count == 0
            || !query.TryGetValue("index", out var indexValue)
            || indexValue is not int index)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _queryApplied = true;

        _viewModel.Initialize(photos, index);
    }
}
