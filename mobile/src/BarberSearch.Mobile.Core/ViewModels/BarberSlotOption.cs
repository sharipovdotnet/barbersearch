using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Слот в сетке «Свободное время»: время для показа и признак выбора.
/// </summary>
/// <remarks>
/// Время слота — UTC-инстанты; <see cref="TimeText"/> уже переведён в пояс салона на момент
/// получения ответа, чтобы экран не занимался часовыми поясами. <see cref="IsSelected"/>
/// наблюдается: чип подсвечивается без перезагрузки списка.
/// </remarks>
public sealed partial class BarberSlotOption : ObservableObject
{
    /// <summary>
    /// Создаёт чип слота.
    /// </summary>
    /// <param name="startAt">Начало в UTC — ровно то, что уйдёт в бронь.</param>
    /// <param name="endAt">Окончание в UTC — ровно то, что уйдёт в бронь.</param>
    /// <param name="timeText">Время в поясе салона: «18:30–19:15».</param>
    public BarberSlotOption(DateTimeOffset startAt, DateTimeOffset endAt, string timeText)
    {
        StartAt = startAt;
        EndAt = endAt;
        TimeText = timeText;
    }

    /// <summary>
    /// Начало слота в UTC.
    /// </summary>
    public DateTimeOffset StartAt { get; }

    /// <summary>
    /// Окончание слота в UTC.
    /// </summary>
    public DateTimeOffset EndAt { get; }

    /// <summary>
    /// Время в поясе салона для показа на чипе.
    /// </summary>
    public string TimeText { get; }

    /// <summary>
    /// Выбран ли слот; подсветка чипа.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Команда выбора слота.
    /// </summary>
    /// <remarks>
    /// Чипы строятся шаблоном внутри <c>FlexLayout</c>, и «дотянуться» до ViewModel через
    /// <c>RelativeSource</c> в скомпилированных привязках нельзя. Команда передаётся пункту
    /// при сборке списка — шаблон остаётся простым, а логика выбора живёт в ViewModel.
    /// </remarks>
    public ICommand? SelectCommand { get; init; }
}

/// <summary>
/// Неизменяемый снимок выбранного слота: всё, что нужно брони и её подтверждению.
/// </summary>
/// <remarks>
/// Снимок делается в момент выбора и не пересчитывается по перезапрошенной сетке: иначе после
/// успешной отправки подтверждение показало бы чужое время, а панель записи — другую цену.
/// </remarks>
/// <param name="Date">Локальная дата салона, на которую считались слоты.</param>
/// <param name="TimeZoneId">Пояс салона из ответа слотов.</param>
/// <param name="SalonId">Салон, в котором создаётся бронь.</param>
/// <param name="BarberServiceId">Услуга барбера — то, что принимает <c>POST api/bookings</c>.</param>
/// <param name="StartAt">Начало в UTC.</param>
/// <param name="EndAt">Окончание в UTC.</param>
/// <param name="ServiceName">Название услуги для сводки.</param>
/// <param name="Price">Цена на момент выбора: сервер скопирует её из прайса.</param>
/// <param name="TimeRangeText">Время в поясе салона: «18:30–19:15».</param>
public sealed record BookingSnapshot(
    DateOnly Date,
    string TimeZoneId,
    Guid SalonId,
    Guid BarberServiceId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string ServiceName,
    decimal Price,
    string TimeRangeText);
