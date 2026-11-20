using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Карточка заявки «Стать мастером»: анкета заявителя и решение администратора.
/// </summary>
/// <remarks>
/// <para>
/// Идентификатор приходит из маршрута Shell: отдельной ссылки на заявку в приложении нет,
/// но страница открывается строкой очереди и должна пережить восстановление стека. Формат
/// тот же, что у остальных карточек: <c>?id={guid}</c> и <see cref="IQueryAttributable"/>.
/// </para>
/// <para>
/// Решение принимает ViewModel, а навигация — страница: успех возвращает <c>null</c>, и
/// страница уходит назад, где очередь перечитывается. Ошибка остаётся на экране. Перед
/// переходом автоповтор останавливается: Shell не поднимает <c>OnDisappearing</c> у страницы,
/// поверх которой открыли следующую, но здесь страница и есть последняя.
/// </para>
/// </remarks>
public partial class AdminRequestPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут карточки заявки в Shell.
    /// </summary>
    public const string Route = AdminRoutes.Request;

    private readonly AdminRequestViewModel _viewModel;

    private IDisposable? _visibility;
    private bool _queryApplied;

    /// <summary>
    /// Создаёт карточку заявки.
    /// </summary>
    /// <param name="viewModel">Состояние карточки.</param>
    public AdminRequestPage(AdminRequestViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает идентификатор заявки из маршрута.
    /// </summary>
    /// <remarks>
    /// Без валидного идентификатора открывать нечего: страница сразу закрывается, как
    /// просмотр портфолио без кадров. <c>Dispatcher</c> откладывает переход до готовности
    /// Shell: <c>GoToAsync</c> из обработчика параметров маршрута ещё нельзя.
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        if (!query.TryGetValue("id", out var value)
            || value is not string rawId
            || !Guid.TryParse(rawId, out var requestId))
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _queryApplied = true;

        _viewModel.Initialize(requestId);
    }

    /// <summary>
    /// Читает заявку при появлении и включает автоповтор на время показа.
    /// </summary>
    /// <remarks>
    /// Область видимости создаётся до загрузки: сбой, пришедший из неё, уже может запустить
    /// автоповтор. <c>async void</c> здесь — единственная допустимая форма переопределения.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(
            this,
            _viewModel.StartAutoRetry,
            _viewModel.StopAutoRetry);

        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Останавливает автоповтор и снимает подписку на окно.
    /// </summary>
    protected override void OnDisappearing()
    {
        _visibility?.Dispose();
        _visibility = null;

        base.OnDisappearing();
    }

    /// <summary>
    /// Главное действие: «Одобрить» либо подтверждение отказа.
    /// </summary>
    /// <remarks>
    /// Успех уводит назад, в очередь; отказ остаётся на экране с причиной. Истёкшая сессия
    /// тоже возвращает <c>null</c>, но уходить по ней нельзя: страница уже показывает
    /// состояние входа, и возврат сделал бы это дважды.
    /// </remarks>
    /// <param name="sender">Кнопка решения.</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnPrimaryActionClicked(object? sender, EventArgs e)
    {
        var error = _viewModel.IsRejecting
            ? await _viewModel.RejectAsync()
            : await _viewModel.ApproveAsync();

        if (error is null && !_viewModel.ShowSignInRequired)
        {
            _viewModel.StopAutoRetry();

            await Shell.Current.GoToAsync("..");
        }
    }

    /// <summary>
    /// Второе действие: «Отклонить» открывает причину, «Отмена» закрывает её.
    /// </summary>
    /// <param name="sender">Вторая кнопка.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnSecondaryActionClicked(object? sender, EventArgs e)
    {
        if (_viewModel.IsRejecting)
        {
            _viewModel.CancelRejectionCommand.Execute(null);

            return;
        }

        _viewModel.StartRejectionCommand.Execute(null);
    }

    /// <summary>
    /// Закрывает карточку без решения: «К списку» и «Войти».
    /// </summary>
    /// <param name="sender">Кнопка возврата.</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnBackClicked(object? sender, EventArgs e)
    {
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync("..");
    }
}
