using BarberSearch.Contracts.Salons.CreateSalon;

namespace BarberSearch.Mobile.Core.Api.Salons;

/// <summary>
/// Записи салонов, доступные барберу-владельцу.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный интерфейс, а не метод в <see cref="IBarberSearchApi"/>: публичные чтения витрины
/// ходят на клиент без авторизационного обработчика, а создание салона — запись от имени
/// барбера, и сервер пускает её только по профилю из токена (<c>BarberOnly</c>). Здесь пока
/// ровно один метод — тот, который нужен мобильной форме «Создать салон».
/// </para>
/// <para>
/// Путь <c>POST api/salons/own</c> повторяет веб (<c>createOwnSalon</c>): владелец берётся из
/// claim, а не из тела, поэтому подставить чужой профиль нельзя.
/// </para>
/// </remarks>
public interface ISalonsApi
{
    /// <summary>
    /// Создаёт салон от имени барбера из токена.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Публичный (listed) салон уходит на проверку и получает
    /// <c>SalonModerationStatus.Pending</c>; скрытое место создаётся сразу активным.
    /// Первый салон профиля сервер делает основным сам — клиент за это не отвечает.
    /// </para>
    /// <para>
    /// <c>X-CSRF-TOKEN</c> не отправляется: клиент Bearer, а не cookie.
    /// </para>
    /// </remarks>
    /// <param name="request">Данные салона; обязательность и формат проверяет домен.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор созданного салона из ответа <c>201 Created</c>.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    /// <exception cref="InvalidOperationException">
    /// Ответ <c>201</c> пришёл без идентификатора — контракт нарушен.
    /// </exception>
    Task<Guid> CreateOwnAsync(
        CreateSalonRequest request,
        CancellationToken cancellationToken = default);
}
