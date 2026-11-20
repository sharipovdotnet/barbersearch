using BarberSearch.Contracts.BarberClients;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// Данные формы «Записать клиента» в кабинете мастера.
/// </summary>
/// <remarks>
/// Все три чтения закрыты ролью <c>Barber</c> и берут профиль из сессии: параметра с чужим
/// профилем нет, поэтому прочитать контакты клиентов или слоты другого мастера нельзя.
/// Это не публичный <see cref="IBarberSearchApi"/>: тот считает слоты видимого профиля и
/// не умеет искать клиентов. <c>X-CSRF-TOKEN</c> не отправляется — здесь только чтения.
/// </remarks>
public interface IBarberClientsApi
{
    /// <summary>
    /// Ищет активного пользователя по email или телефону для ручной записи.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ответ — подсказка максимум из десяти строк без пагинации: это форма, а не справочник.
    /// Роль найденного не фильтруется: записать можно клиента, администратора, мастера и т. д.
    /// </para>
    /// <para>
    /// Запрос тримится, но пустым не проверяется: минимум и максимум символов объявлены
    /// сервером (<c>barberClient.search.*</c>), и отказ должен приходить его кодом, а не
    /// расходиться с ним в клиенте.
    /// </para>
    /// </remarks>
    /// <param name="search">Email или телефон; пробелы по краям срезаются.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Найденные пользователи в порядке сервера.</returns>
    /// <exception cref="BarberSearchApiException">Запрос слишком короткий, длинный или API ответил ошибкой.</exception>
    Task<IReadOnlyList<BarberClientResponse>> SearchClientsAsync(
        string search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает активные услуги и салоны собственного профиля.
    /// </summary>
    /// <remarks>
    /// Публичная карточка отдаёт только подтверждённый профиль, а записать клиента можно и
    /// до публикации, поэтому коллекции читаются из собственного агрегата мастера.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Услуги и салоны для формы записи.</returns>
    /// <exception cref="BarberSearchApiException">Профиль не найден или API ответил ошибкой.</exception>
    Task<BarberBookingOptionsResponse> GetBookingOptionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает свободное время собственного профиля на локальную дату салона.
    /// </summary>
    /// <remarks>
    /// Отличие от публичных слотов только в проверке видимости профиля: кабинет работает и до
    /// подтверждения. Активность салона и услуги, отпуска и занятые интервалы учитываются
    /// одинаково, поэтому экран не фильтрует слоты сам.
    /// </remarks>
    /// <param name="slotDate">Локальная дата салона.</param>
    /// <param name="hairServiceId">Услуга справочника; задаёт шаг сетки.</param>
    /// <param name="salonId">Салон; <c>null</c> — основной салон мастера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сетка слотов с поясом салона и длительностью шага.</returns>
    /// <exception cref="BarberSearchApiException">Услуга, салон или профиль недоступны.</exception>
    Task<BarberSlotsResponse> GetSlotsAsync(
        DateOnly slotDate,
        Guid? hairServiceId,
        Guid? salonId,
        CancellationToken cancellationToken = default);
}
