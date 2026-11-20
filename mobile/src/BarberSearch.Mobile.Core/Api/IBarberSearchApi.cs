using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
using BarberSearch.Contracts.Specializations.GetSpecializations;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// Чтения API, которые нужны клиентскому приложению.
/// </summary>
/// <remarks>
/// Интерфейс существует ради тестов ViewModel: экраны зависят от него, а не от
/// <see cref="HttpClient"/>, поэтому проверяются без сети и без эмулятора. Набор методов растёт
/// вместе с экранами — «на всякий случай» сюда ничего не добавляется. Поиск, справочники для его
/// фильтров, подсказки города, карточка и слоты — публичные чтения, поэтому живут здесь и ходят на
/// клиент без авторизационного обработчика.
/// </remarks>
public interface IBarberSearchApi
{
    /// <summary>
    /// Ищет мастеров с фильтрами витрины и keyset-пагинацией.
    /// </summary>
    /// <param name="filters">
    /// Фильтры: город, дата, язык, специализация, услуга справочника и дополнительная услуга.
    /// </param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы из предыдущего ответа.</param>
    /// <param name="pageSize">Размер страницы; сервер по умолчанию отдаёт 20.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница выдачи; <c>NextCursor = null</c> означает последнюю.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
        BarberSearchFilters filters,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Публичный справочник активных специализаций для фильтра витрины.
    /// </summary>
    /// <remarks>
    /// Ответ — голый массив без пагинации, порядок серверный (по имени). Пустой справочник —
    /// пустой список, а не ошибка: экран просто скрывает свой селект.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Активные специализации в порядке сервера.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Публичный справочник активных услуг для фильтра витрины.
    /// </summary>
    /// <remarks>
    /// Ответ — голый массив без пагинации; порядок — категория, затем имя.
    /// <c>DefaultDurationMinutes</c> фильтру не нужен, но приходит в том же контракте.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Активные услуги в порядке сервера.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Публичные подсказки города по началу названия — прокси DaData на сервере.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ответ — голый массив без пагинации, порядок серверный (по релевантности); пусто — не
    /// ошибка, а «ничего не найдено». Подсказка заполняет поле городом
    /// (<see cref="CitySuggestionResponse.City"/>), а <see cref="CitySuggestionResponse.Display"/>
    /// нужен только строке списка: сервер сравнивает город точно, и «Екатеринбург» из подсказки
    /// находит салоны, а набранное «Екат» — нет.
    /// </para>
    /// <para>
    /// Запрос короче двух символов сервер отвергает кодом
    /// <c>addressSuggestions.query.tooShort</c>, поэтому порог проверяет вызывающий: экран
    /// не дёргает сеть на первой букве. Лимит подсказок фиксирован — восемь, как на сервере.
    /// </para>
    /// </remarks>
    /// <param name="query">Начало названия города; тримится.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Подсказки города в порядке сервера.</returns>
    /// <exception cref="ArgumentException">Запрос пуст или состоит из пробелов.</exception>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<IReadOnlyList<CitySuggestionResponse>> GetCitySuggestionsAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Публичная карточка мастера: шапка, услуги, салоны и специализации.
    /// </summary>
    /// <remarks>
    /// Выключенные услуги сервер отдаёт вместе с активными (<c>IsActive = false</c>), а скрытый
    /// или неподтверждённый профиль — <c>404</c>. Оба случая разбирает экран, а не клиент.
    /// </remarks>
    /// <param name="id">Идентификатор профиля мастера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Карточка профиля.</returns>
    /// <exception cref="BarberSearchApiException">Профиль не найден или API ответил ошибкой.</exception>
    Task<BarberProfileResponse> GetBarberProfileAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Свободные слоты мастера на локальную дату салона.
    /// </summary>
    /// <remarks>
    /// <paramref name="date"/> — календарная дата салона, а не инстант: без <paramref name="salonId"/>
    /// сервер берёт основной салон, без <paramref name="hairServiceId"/> шаг сетки — 30 минут.
    /// Пару <c>barberServiceId</c> + <c>durationMinutes</c> для брони отдаёт сам ответ.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="slotDate">Локальная дата салона.</param>
    /// <param name="hairServiceId">Услуга справочника; <c>null</c> — «Любая услуга».</param>
    /// <param name="salonId">Салон; <c>null</c> — основной салон мастера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сетка слотов с поясом салона и длительностью шага.</returns>
    /// <exception cref="BarberSearchApiException">Услуга, салон или профиль недоступны.</exception>
    Task<BarberSlotsResponse> GetSlotsAsync(
        Guid barberProfileId,
        DateOnly slotDate,
        Guid? hairServiceId,
        Guid? salonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Опубликованные отзывы мастера: страница с непрозрачным курсором.
    /// </summary>
    /// <remarks>
    /// Это анонимное чтение: сервер не требует токен и не отдаёт автора, услугу и салон
    /// в <see cref="ReviewResponse"/>. Порядок — свежие сверху; <paramref name="cursor"/>
    /// возвращается из ответа как есть и не разбирается.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера; обязателен.</param>
    /// <param name="cursor">Курсор следующей страницы; <c>null</c> — первая.</param>
    /// <param name="pageSize">Размер страницы; <c>null</c> — серверный по умолчанию (20).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница отзывов; <c>NextCursor = null</c> означает последнюю.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<PagedResponse<ReviewResponse>> GetReviewsAsync(
        Guid barberProfileId,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Публичный каталог активных салонов для выбора места работы.
    /// </summary>
    /// <remarks>
    /// Это анонимное чтение на том же клиенте без авторизационного обработчика: барбер
    /// выбирает салон до входа в кабинет, а витрина — без входа. Ответ — голый массив без
    /// пагинации, как у справочников фильтров. Пустые фильтры не передаются вовсе: сервер
    /// трактует их как «без фильтра», но пустые <c>city=</c>/<c>search=</c> только засоряют
    /// адрес и кэш.
    /// </remarks>
    /// <param name="city">Город; пустая строка или <c>null</c> — без фильтра.</param>
    /// <param name="search">Поиск по названию и адресу; пустая строка — без фильтра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Салоны каталога в порядке сервера.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<IReadOnlyList<SalonDirectoryItemResponse>> SearchSalonsAsync(
        string? city,
        string? search,
        CancellationToken cancellationToken = default);
}
