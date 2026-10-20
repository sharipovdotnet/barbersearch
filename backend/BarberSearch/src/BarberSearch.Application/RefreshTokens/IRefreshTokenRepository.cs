using BarberSearch.Domain.RefreshTokens;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.RefreshTokens;

/// <summary>
/// Доступ к refresh-токенам: поиск по хешу, выдача, ротация и отзыв.
/// </summary>
/// <remarks>
/// Открытый токен через этот интерфейс не ходит: приложение передаёт только SHA-256,
/// а реализация в Infrastructure хранит ровно его. Отсутствие записи — <c>Error.NotFound</c>;
/// ошибки ограничений БД — <c>Error.Conflict</c> / <c>Error.Validation</c>.
/// </remarks>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Токен по хешу. Возвращённая сущность отслеживается контекстом, поэтому её изменение
    /// достаточно передать в <see cref="RotateAsync"/> или <see cref="RevokeAsync"/>.
    /// </summary>
    Task<Result<RefreshToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет новый токен (вход или первый выпуск пары).
    /// </summary>
    Task<UnitResult<Failure>> AddAsync(
        RefreshToken token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарно сохраняет ротацию: старый токен уже помечен <c>RevokedAt</c> + <c>ReplacedBy</c>,
    /// новый добавляется в той же транзакции.
    /// </summary>
    Task<UnitResult<Failure>> RotateAsync(
        RefreshToken current,
        RefreshToken replacement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет отзыв одного токена (logout).
    /// </summary>
    Task<UnitResult<Failure>> RevokeAsync(
        RefreshToken token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отзывает всю цепочку, начиная с указанного токена, следуя по <c>replaced_by</c>.
    /// Вызывается при повторном использовании уже заменённого токена.
    /// </summary>
    Task<UnitResult<Failure>> RevokeChainAsync(
        Guid tokenId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отзывает все живые токены пользователя: смена пароля, смена роли, деактивация.
    /// </summary>
    Task<UnitResult<Failure>> RevokeAllForUserAsync(
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);
}
