using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Карточка отзыва админки: контекст записи и решение модератора.
/// </summary>
/// <remarks>
/// <para>
/// Идентификатор приходит в маршруте (<c>admin-review?id=…</c>) и читается через
/// <see cref="IQueryAttributable"/>: страница открывается и из строки очереди, и по прямой
/// ссылке, и в обоих случаях сама решает, что запрос без идентификатора — это возврат назад,
/// а не пустая карточка.
/// </para>
/// <para>
/// Решение принимает ViewModel, а переход выполняет страница: после успеха она закрывается,
/// очередь под ней перечитывается при появлении. Ошибка остаётся на карточке — человек
/// должен видеть, что решение не применилось.
/// </para>
/// <para>
/// Перед переходом автоповтор останавливается: Shell не поднимает <c>OnDisappearing</c>
/// у страницы, поверх которой открыли следующую.
/// </para>
/// </remarks>
public partial class AdminReviewPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут карточки в Shell.
    /// </summary>
    public const string Route = AdminRoutes.Review;

    private readonly AdminReviewViewModel _viewModel;

    private IDisposable? _visibility;
    private Guid? _reviewId;
    private bool _queryApplied;
    private bool _isLeaving;
    private bool _isSubmitting;

    /// <summary>
    /// Создаёт карточку.
    /// </summary>
    /// <param name="viewModel">Состояние карточки.</param>
    public AdminReviewPage(AdminReviewViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает идентификатор отзыва из маршрута.
    /// </summary>
    /// <remarks>
    /// Маршрут без идентификатора или с невалидным Guid — ошибка адреса, а не повод показать
    /// пустую карточку: страница сразу закрывается и возвращает к очереди.
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        _queryApplied = true;

        if (!query.TryGetValue("id", out var value)
            || value is not string raw
            || !Guid.TryParse(raw, out var reviewId)
            || reviewId == Guid.Empty)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _reviewId = reviewId;
    }

    /// <summary>
    /// Читает карточку при появлении и включает автоповтор на время показа.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(this, _viewModel.StartAutoRetry, _viewModel.StopAutoRetry);

        if (_reviewId is { } reviewId)
        {
            await _viewModel.InitializeAsync(reviewId);
        }
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
    /// Публикует отзыв и уходит к очереди при успехе.
    /// </summary>
    /// <param name="sender">Кнопка «Опубликовать».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnPublishClicked(object? sender, EventArgs e)
    {
        if (_isSubmitting)
        {
            return;
        }

        _isSubmitting = true;

        try
        {
            _viewModel.StopAutoRetry();

            var error = await _viewModel.PublishAsync();

            await LeaveOnSuccessAsync(error);
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    /// <summary>
    /// Открывает панель подтверждения скрытия.
    /// </summary>
    /// <param name="sender">Кнопка «Скрыть».</param>
    /// <param name="e">Аргументы события.</param>
    private void OnHideClicked(object? sender, EventArgs e) =>
        _viewModel.OpenHideConfirmationCommand.Execute(null);

    /// <summary>
    /// Закрывает панель подтверждения без записи.
    /// </summary>
    /// <param name="sender">Кнопка «Отмена».</param>
    /// <param name="e">Аргументы события.</param>
    private void OnCancelHideClicked(object? sender, EventArgs e) =>
        _viewModel.CancelHideConfirmationCommand.Execute(null);

    /// <summary>
    /// Скрывает отзыв после подтверждения в панели и уходит к очереди при успехе.
    /// </summary>
    /// <param name="sender">Кнопка подтверждения.</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnConfirmHideClicked(object? sender, EventArgs e)
    {
        if (_isSubmitting)
        {
            return;
        }

        _isSubmitting = true;

        try
        {
            _viewModel.StopAutoRetry();

            var error = await _viewModel.HideAsync();

            await LeaveOnSuccessAsync(error);
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    /// <summary>
    /// Возвращает к очереди из состояния «отзыв уже разобрали».
    /// </summary>
    /// <param name="sender">Кнопка «К очереди».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnBackToQueueClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");

    /// <summary>
    /// Возвращает на «Профиль»: сессии больше нет, форма входа стоит там.
    /// </summary>
    /// <param name="sender">Кнопка «Войти».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnSignInClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//profile");

    /// <summary>
    /// Закрывает карточку после успешного решения либо остаётся на ней при ошибке.
    /// </summary>
    /// <remarks>
    /// <c>null</c> от ViewModel означает успех или конец сессии. Конец сессии важен отдельно:
    /// карточка уже показала, что вход нужен, и уходить «назад» в очередь, которая тоже
    /// без сессии, бессмысленно — возврат идёт на вкладку «Профиль».
    /// </remarks>
    /// <param name="error">Текст ошибки либо <c>null</c>.</param>
    /// <returns>Задача перехода.</returns>
    private async Task LeaveOnSuccessAsync(string? error)
    {
        if (error is not null || _isLeaving)
        {
            return;
        }

        _isLeaving = true;

        try
        {
            await Shell.Current.GoToAsync(_viewModel.IsSignInRequired ? "//profile" : "..");
        }
        finally
        {
            _isLeaving = false;
        }
    }
}
