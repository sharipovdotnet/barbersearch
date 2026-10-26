using BarberSearch.Contracts.Common;
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
    /// Код ошибки <see cref="RotateAsync"/>: строку уже отозвал параллельный запрос, поэтому
    /// условный <c>UPDATE … WHERE revoked_at IS NULL</c> не нашёл, что менять. Это проигранная
    /// гонка двух ротаций одного токена, а не сбой базы.
    /// </summary>
    const string AlreadyRotatedErrorCode = ApiErrorCodes.RefreshToken.AlreadyRotated;

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
    /// <remarks>
    /// Отзыв выполняется условным <c>UPDATE … WHERE revoked_at IS NULL</c>, поэтому из двух
    /// параллельных ротаций одного токена побеждает ровно одна. Проигравший не получает
    /// исключения: результат — <see cref="Failure"/> с кодом
    /// <see cref="AlreadyRotatedErrorCode"/>, а замена не вставляется. В базе остаётся
    /// единственная живая цепочка — цепочка победителя.
    /// </remarks>
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
    /// <remarks>
    /// Отзыв идёт сырым рекурсивным <c>UPDATE</c>, поэтому отслеживаемые экземпляры токенов
    /// после вызова устаревают и снимаются с отслеживания: их повторное сохранение не должно
    /// вернуть <c>revoked_at</c> в <c>NULL</c>. Если токен понадобился снова, его перечитывают.
    /// </remarks>
    Task<UnitResult<Failure>> RevokeChainAsync(
        Guid tokenId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отзывает все живые токены пользователя: смена пароля, смена роли, деактивация.
    /// </summary>
    /// <remarks>
    /// Как и <see cref="RevokeChainAsync"/>, отзыв выполняется в обход отслеживания изменений,
    /// поэтому отслеживаемые токены пользователя после вызова снимаются с него: следующий
    /// <c>SaveChanges</c> не должен записать им прежний <c>revoked_at</c>.
    /// </remarks>
    Task<UnitResult<Failure>> RevokeAllForUserAsync(
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет истёкшие refresh-токены: строки, у которых <c>ExpiresAt &lt; cutoff</c>, —
    /// и возвращает количество удалённых строк.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Вызывается фоновой уборкой (R-50). Граница выбирается как
    /// <c>cutoff = now - RefreshToken.MAX_LIFETIME_DAYS</c> (30 дней), и это не «срок хранения
    /// на всякий случай», а гарантия безопасности ссылки <see cref="RefreshToken.ReplacedBy"/>.
    /// Ротация выпускает замену раньше, чем родитель истекает, а живёт токен не дольше
    /// <see cref="RefreshToken.MAX_LIFETIME_DAYS"/>, поэтому
    /// <c>expiresAt(child) &lt; expiresAt(parent) + 30 дней</c>. При таком cutoff у каждой
    /// удаляемой строки все потомки уже истекли: FK <c>replaced_by</c> с
    /// <c>ON DELETE SET NULL</c> никогда не обнулит ссылку живого токена. Меньший запас
    /// (например, «удалять всё истёкшее») эту гарантию дать не может.
    /// </para>
    /// <para>
    /// Отозванные, но ещё не истёкшие строки остаются: они нужны reuse-detection, чтобы
    /// повторное предъявление заменённого токена отзывало цепочку. Уборка не ограничивается
    /// листьями цепочки: у активной цепочки лист ровно один — живой кончик, — и leaf-only
    /// удаление не удаляло бы ничего. Поэтому фильтр — только <see cref="RefreshToken.ExpiresAt"/>,
    /// а ссылки потомков на удалённые строки обнуляются каскадом FK.
    /// </para>
    /// </remarks>
    /// <param name="cutoff">Граница: удаляются строки строго раньше этого момента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<int> DeleteExpiredAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}
