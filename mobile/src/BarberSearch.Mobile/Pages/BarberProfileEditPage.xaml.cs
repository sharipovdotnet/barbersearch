using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Редактировать профиль»: имя, опыт, языки, оплата, тексты и списки карточки.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута. Карточку и владельца раздела передаёт
/// «Профиль» кабинета объектами маршрута: правка заменяет карточку целиком, поэтому форма
/// получает уже прочитанный ответ, а не перечитывает его сама.
/// </para>
/// <para>
/// Форма ничего не читает: <c>PageVisibility</c> и автоповтор здесь не нужны. Сохранение —
/// запись, и после успеха страница просто закрывается: раздел «Профиль» перечитывает
/// карточку сам на возврате (кабинет активирует активный раздел после <c>Pop</c>).
/// «Отменить» закрывает форму без запроса — правки ещё никуда не ушли.
/// </para>
/// </remarks>
public partial class BarberProfileEditPage : ContentPage, IQueryAttributable
{
    private readonly BarberProfileEditViewModel _viewModel;

    private bool _queryApplied;

    /// <summary>
    /// Создаёт форму правки карточки.
    /// </summary>
    /// <param name="viewModel">Состояние формы.</param>
    public BarberProfileEditPage(BarberProfileEditViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает карточку и владельца раздела.
    /// </summary>
    /// <remarks>
    /// Владелец проверяется, но не хранится: после успеха раздел перечитывается сам на
    /// возврате, и второй ссылки на чужой экран странице держать не нужно. Без карточки
    /// заполнять форму нечем — страница сразу закрывается.
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        if (!query.TryGetValue("owner", out var ownerValue)
            || ownerValue is not BarberCabinetProfileViewModel
            || !query.TryGetValue("profile", out var profileValue)
            || profileValue is not BarberProfileResponse profile)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _queryApplied = true;

        _viewModel.Initialize(profile);
        _viewModel.Saved += OnSaved;
    }

    /// <summary>
    /// Закрывает форму после сохранения: раздел перечитает карточку на возврате.
    /// </summary>
    /// <param name="sender">ViewModel формы.</param>
    /// <param name="e">Событие без данных.</param>
    private async void OnSaved(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");

    /// <summary>
    /// Закрывает форму без сохранения.
    /// </summary>
    /// <remarks>
    /// Правки ещё не ушли на сервер, поэтому подтверждение не нужно: возврат и есть отказ.
    /// </remarks>
    private async void OnDiscardClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");
}
