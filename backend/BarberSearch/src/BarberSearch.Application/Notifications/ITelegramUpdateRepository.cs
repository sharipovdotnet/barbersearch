using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Журнал обработанных обновлений Telegram — защита вебхука от повторной обработки.
/// </summary>
/// <remarks>
/// <para>
/// Telegram ретраит запрос, пока не получит <c>2xx</c>, а сеть может продублировать один и
/// тот же <c>update_id</c> двум экземплярам приложения. Таблица
/// <c>telegram_updates</c> с уникальным <c>update_id</c> — единственная корректная защита
/// для нескольких инстансов; память процесса не годится, потому что ретрай может прийти
/// на другой экземпляр.
/// </para>
/// <para>
/// Регистрация идёт до любых побочных эффектов: если обновление уже обработано, обработчик
/// ничего не делает и всё равно отвечает <c>200</c>.
/// </para>
/// </remarks>
public interface ITelegramUpdateRepository
{
    /// <summary>
    /// Пытается зарегистрировать обновление. <c>false</c> — оно уже было обработано.
    /// </summary>
    /// <param name="updateId">Идентификатор обновления из Bot API.</param>
    /// <param name="receivedAt">Момент приёма в UTC — часы Application.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если обновление новое и должно быть обработано.</returns>
    Task<Result<bool, Failure>> TryRegisterAsync(
        long updateId,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default);
}
