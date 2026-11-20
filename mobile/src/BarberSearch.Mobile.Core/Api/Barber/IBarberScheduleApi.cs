using BarberSearch.Contracts.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Contracts.BarberSalons.SetScheduleException;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// Расписание мастера в кабинете: недельный шаблон, исключения на даты и отпуска.
/// </summary>
/// <remarks>
/// <para>
/// Клиент ходит через <see cref="HttpClient"/> с <c>AuthenticatedHttpHandler</c>: владение
/// профилем сервер проверяет по токену, а <c>X-CSRF-TOKEN</c> Bearer-запросам не нужен.
/// </para>
/// <para>
/// Время недели и исключений — локальное время салона (<see cref="TimeOnly"/>), его нельзя
/// пересчитывать в пояс телефона. Отпуска, наоборот, передаются UTC-инстантами.
/// </para>
/// </remarks>
public interface IBarberScheduleApi
{
    /// <summary>
    /// Читает расписание мастера: салоны, недельные шаблоны, исключения и отпуска.
    /// </summary>
    /// <remarks>
    /// <paramref name="from"/> и <paramref name="toDate"/> ограничивают окно исключений; <c>null</c>
    /// оставляет серверное окно по умолчанию. Даты — календарные строки салона, не инстанты.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="from">Начало окна исключений; <c>null</c> — серверное значение.</param>
    /// <param name="toDate">Конец окна исключений; <c>null</c> — серверное значение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Расписание со всеми салонами профиля.</returns>
    /// <exception cref="BarberSearchApiException">Профиль не найден или API ответил ошибкой.</exception>
    Task<BarberScheduleResponse> GetAsync(
        Guid barberProfileId,
        DateOnly? from = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Полностью заменяет недельный шаблон пары (мастер, салон).
    /// </summary>
    /// <remarks>
    /// Дней должно быть ровно семь — по одному на день недели; неполный набор или повтор
    /// отклоняет домен. Если правка выводит активные визиты за новые часы, сервер отвечает
    /// <c>409 barberSchedule.conflict</c>, и только <paramref name="force"/> сохраняет правку.
    /// Успех — <c>204</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="salonId">Салон, чей шаблон меняется.</param>
    /// <param name="request">Семь дней недели.</param>
    /// <param name="force">Сохранить, даже если активные визиты выйдут за новые часы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Шаблон невалиден или конфликтует с визитами.</exception>
    Task ReplaceWorkingSchedulesAsync(
        Guid barberProfileId,
        Guid salonId,
        ReplaceWorkingSchedulesRequest request,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ставит или переписывает исключение на конкретную дату.
    /// </summary>
    /// <remarks>
    /// Дата — часть адреса ресурса и уходит строкой <c>YYYY-MM-DD</c>, а не инстантом.
    /// <paramref name="force"/> нужен, когда на дате есть активные визиты, которые новая
    /// правка делает невозможными; без него сервер отвечает <c>409</c>. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="salonId">Салон пары.</param>
    /// <param name="exceptionDate">Локальная дата салона.</param>
    /// <param name="request">Выходной или свои часы на дату.</param>
    /// <param name="force">Сохранить, даже если на дате есть активные визиты.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Исключение невалидно или конфликтует с визитами.</exception>
    Task SetScheduleExceptionAsync(
        Guid barberProfileId,
        Guid salonId,
        DateOnly exceptionDate,
        SetScheduleExceptionRequest request,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Снимает исключение с даты и возвращает недельный шаблон.
    /// </summary>
    /// <remarks>
    /// Успех — <c>204</c>; отсутствующее исключение — <c>404</c>, а не успех: экран должен
    /// узнать, что правку уже кто-то снял.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="salonId">Салон пары.</param>
    /// <param name="exceptionDate">Локальная дата салона.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Исключение не найдено или API ответил ошибкой.</exception>
    Task RemoveScheduleExceptionAsync(
        Guid barberProfileId,
        Guid salonId,
        DateOnly exceptionDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет нерабочий период (отпуск) профиля.
    /// </summary>
    /// <remarks>
    /// Отпуск общий для всех салонов профиля; границы — UTC-инстанты. Пересечение с активными
    /// визитами даёт <c>409 barberSchedule.conflict</c>, и только <paramref name="force"/>
    /// сохраняет период. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="request">Начало, конец и пометка периода.</param>
    /// <param name="force">Сохранить, даже если в интервал попадают активные визиты.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Период невалиден или конфликтует с визитами.</exception>
    Task AddTimeOffAsync(
        Guid barberProfileId,
        AddBarberProfileTimeOffRequest request,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет нерабочий период.
    /// </summary>
    /// <remarks>
    /// Успех — <c>204</c>; неизвестный период — <c>404</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="timeOffId">Идентификатор периода.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Период не найден или API ответил ошибкой.</exception>
    Task RemoveTimeOffAsync(
        Guid barberProfileId,
        Guid timeOffId,
        CancellationToken cancellationToken = default);
}
