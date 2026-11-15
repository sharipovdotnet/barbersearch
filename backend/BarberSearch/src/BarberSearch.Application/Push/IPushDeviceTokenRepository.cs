using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push;

/// <summary>
/// Доступ к токенам устройств: регистрация, чтение пачки, снятие и уборка.
/// </summary>
/// <remarks>
/// <para>
/// Регистрация — upsert по уникальному токену: токен принадлежит устройству, а не кабинету,
/// поэтому повторная регистрация под другим пользователем передаёт владение
/// (<c>ON CONFLICT (token) DO UPDATE SET user_id = …, last_seen_at = …</c>), а не падает
/// на уникальности.
/// </para>
/// <para>
/// Предел устройств (<c>Push:MaxTokensPerUser</c>) закрывает хранилище, а не обработчик:
/// вставка и удаление самых старых устройств — одна транзакция, иначе два параллельных
/// входа с разных телефонов оставили бы пользователя с шестью токенами.
/// </para>
/// <para>
/// Снятие и уборка идемпотентны: удаление несуществующей строки — успех с нулём, потому что
/// клиент не должен различать «токен был» и «токена не было» (нет oracle'а перечисления).
/// </para>
/// </remarks>
public interface IPushDeviceTokenRepository
{
    /// <summary>
    /// Регистрирует токен: вставляет новый, обновляет владельца и <c>last_seen_at</c>
    /// у существующего и удаляет самые старые устройства сверх предела.
    /// </summary>
    /// <param name="token">Токен устройства с владельцем и платформой.</param>
    /// <param name="maxTokensPerUser">Сколько устройств оставлять у пользователя.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> RegisterAsync(
        PushDeviceToken token,
        int maxTokensPerUser,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Все токены пользователя, свежие сверху (<c>last_seen_at DESC, id DESC</c>).
    /// </summary>
    /// <param name="userId">Владелец устройств.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<PushDeviceToken>, Failure>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет токен, только если он принадлежит пользователю.
    /// </summary>
    /// <remarks>
    /// Чужой или неизвестный токен не трогается: обработчик отвечает успехом, поэтому
    /// перебором нельзя выяснить, зарегистрирован ли токен у другого аккаунта.
    /// </remarks>
    /// <param name="token">Токен устройства.</param>
    /// <param name="userId">Текущий пользователь.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сколько строк удалено (<c>0</c> — токена не было или он чужой).</returns>
    Task<Result<int, Failure>> DeleteAsync(
        string token,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет токен независимо от владельца — после отказа FCM.
    /// </summary>
    /// <remarks>
    /// Нужен доставке: <c>UNREGISTERED</c>/<c>INVALID_ARGUMENT</c> означают, что токен мёртв,
    /// и оставлять его в таблице нельзя. Владелец здесь не проверяется, потому что решение
    /// принял транспорт, а не клиент.
    /// </remarks>
    /// <param name="token">Мёртвый токен.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сколько строк удалено.</returns>
    Task<Result<int, Failure>> DeleteByTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет токены, не подтверждавшиеся с момента <paramref name="cutoff"/>.
    /// </summary>
    /// <param name="cutoff">Граница ретенции: всё, что старше, удаляется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сколько строк удалено.</returns>
    Task<Result<int, Failure>> DeleteStaleAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}
