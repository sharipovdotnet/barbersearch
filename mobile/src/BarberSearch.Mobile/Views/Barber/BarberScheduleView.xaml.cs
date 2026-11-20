using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Views.Barber;

/// <summary>
/// Раздел «Расписание» кабинета: недельный шаблон, исключения на даты и отпуска.
/// </summary>
/// <remarks>
/// Раздел живёт внутри <c>BarberCabinetPage</c> и не имеет собственного жизненного цикла:
/// появление, уход и возврат с дочерних экранов ему сообщает страница, а конфликт 409
/// показывается поверх содержимого этого же <c>ContentView</c>, а не системным алертом.
/// Кодовая часть нужна только там, где XAML не справляется: подпись строки выбора салона
/// и удаление строки списка, у которого команда принимает параметр.
/// </remarks>
public partial class BarberScheduleView : ContentView
{
    private readonly BarberScheduleViewModel _viewModel;

    /// <summary>
    /// Создаёт раздел и настраивает подпись строк выбора салона.
    /// </summary>
    /// <param name="viewModel">Состояние раздела.</param>
    public BarberScheduleView(BarberScheduleViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        // Подпись строки задаётся кодом: ItemDisplayBinding — привязка к элементу, а не к
        // ViewModel раздела, поэтому компилированная разметка здесь не подходит.
        SalonPicker.ItemDisplayBinding = new Binding(nameof(BarberScheduleSalonItem.DisplayText));
    }

    /// <summary>
    /// Состояние раздела: странице нужен он как общий контракт раздела кабинета.
    /// </summary>
    public BarberScheduleViewModel ViewModel => _viewModel;

    /// <summary>
    /// Снимает исключение с даты.
    /// </summary>
    private async void OnRemoveExceptionClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberScheduleExceptionItem item })
        {
            await _viewModel.RemoveExceptionCommand.ExecuteAsync(item);
        }
    }

    /// <summary>
    /// Удаляет отпуск.
    /// </summary>
    private async void OnRemoveTimeOffClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberTimeOffItem item })
        {
            await _viewModel.RemoveTimeOffCommand.ExecuteAsync(item);
        }
    }
}
