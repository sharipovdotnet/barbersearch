using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.Push;

/// <summary>
/// Токен устройства клиента (FCM/APNs) — адрес доставки push-уведомления.
/// </summary>
/// <remarks>
/// <para>
/// Один токен принадлежит ровно одному устройству и ровно одному аккаунту: уникальный
/// индекс <c>ux_push_device_tokens_token</c> держит это на уровне базы. Повторная
/// регистрация того же токена другим пользователем — не ошибка, а передача владения
/// (клиент вошёл в другой аккаунт на том же телефоне), поэтому запись обновляет
/// <see cref="UserId"/> и <see cref="LastSeenAt"/>, а не падает на уникальности.
/// </para>
/// <para>
/// Токен — непрозрачная строка транспорта, а не пользовательский текст: домен проверяет
/// только непустоту и предел длины и срезает пробелы по краям. Платформа обязана быть
/// определённым значением <see cref="PushPlatform"/>: неизвестное число не должно дойти
/// до БД и стать необслуживаемой строкой.
/// </para>
/// <para>
/// «Сейчас» домен не читает (AGENT.md §7.6): момент регистрации передаёт Application,
/// а <see cref="LastSeenAt"/> обновляется при каждом повторном входе. По нему же работает
/// ограничение числа устройств на пользователя и суточная уборка старых токенов.
/// </para>
/// </remarks>
public sealed class PushDeviceToken : Entity
{
    /// <summary>
    /// Публичный код ошибки «токен пуст или состоит из пробелов»; <c>400</c>.
    /// </summary>
    private const string TOKEN_INVALID_CODE = "pushDeviceToken.token.invalid";

    /// <summary>
    /// Публичный код ошибки «токен длиннее предела колонки»; <c>400</c>.
    /// </summary>
    private const string TOKEN_TOO_LONG_CODE = "pushDeviceToken.token.tooLong";

    /// <summary>
    /// Публичный код ошибки «платформа не определена»; <c>400</c>.
    /// </summary>
    private const string PLATFORM_UNKNOWN_CODE = "pushDeviceToken.platform.unknown";

    /// <summary>
    /// Публичный код ошибки «регистрация без пользователя»; <c>400</c>.
    /// </summary>
    private const string USER_INVALID_CODE = "pushDeviceToken.register.user";

    /// <summary>
    /// Публичный код ошибки «момент регистрации не задан»; <c>400</c>.
    /// </summary>
    private const string NOW_INVALID_CODE = "pushDeviceToken.register.now";

    /// <summary>
    /// Владелец токена: сюда приходит push о его визите.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Токен устройства в том виде, в каком его выдал транспорт; уникален.
    /// </summary>
    public string Token { get; private set; } = null!;

    /// <summary>
    /// Платформа устройства; определяет, какой транспорт будет отправлять сообщение.
    /// </summary>
    public PushPlatform Platform { get; private set; }

    /// <summary>
    /// Момент последней регистрации или повторного подтверждения токена.
    /// </summary>
    /// <remarks>
    /// По нему решается, какие из устройств пользователя старше остальных (при превышении
    /// предела устройств), и какие токены пора убрать суточной уборкой. Бизнес-«сейчас»
    /// передаётся аргументом, домен его не читает.
    /// </remarks>
    public DateTimeOffset LastSeenAt { get; private set; }

    private PushDeviceToken()
    {
    }

    /// <summary>
    /// Регистрирует токен устройства за пользователем.
    /// </summary>
    /// <remarks>
    /// Пробелы по краям токена срезаются: клиент может прислать строку с переводом строки
    /// из лога, и это не должно создавать «другой» токен. Все ошибки собираются сразу,
    /// как в остальных фабриках домена.
    /// </remarks>
    /// <param name="userId">Владелец устройства.</param>
    /// <param name="token">Токен устройства; непустой, не длиннее <see cref="Constants.MAX_PUSH_DEVICE_TOKEN_LENGTH"/>.</param>
    /// <param name="platform">Платформа устройства.</param>
    /// <param name="now">Момент регистрации в UTC.</param>
    public static Result<PushDeviceToken, Failure> Register(
        Guid userId,
        string? token,
        PushPlatform platform,
        DateTimeOffset now)
    {
        var errors = DomainErrors.Collect();

        if (userId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                USER_INVALID_CODE,
                "Push device token owner cannot be empty.",
                nameof(userId)));
        }

        var normalizedToken = NormalizeToken(token);

        if (normalizedToken is null)
        {
            errors.Add(Error.Validation(
                TOKEN_INVALID_CODE,
                "Push device token cannot be empty.",
                nameof(token)));
        }
        else if (normalizedToken.Length > Constants.MAX_PUSH_DEVICE_TOKEN_LENGTH)
        {
            errors.Add(Error.Validation(
                TOKEN_TOO_LONG_CODE,
                $"""
                 Push device token cannot be longer than
                 {Constants.MAX_PUSH_DEVICE_TOKEN_LENGTH} characters.
                 """,
                nameof(token)));
        }

        if (!Enum.IsDefined(platform))
        {
            errors.Add(Error.Validation(
                PLATFORM_UNKNOWN_CODE,
                "The push platform is not supported.",
                nameof(platform)));
        }

        if (now == default)
        {
            errors.Add(Error.Validation(
                NOW_INVALID_CODE,
                "The registration time cannot be default.",
                nameof(now)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new PushDeviceToken
        {
            UserId = userId,
            Token = normalizedToken!,
            Platform = platform,
            LastSeenAt = now
        };
    }

    /// <summary>
    /// Отмечает, что устройство снова зарегистрировалось или прислало токен.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: повтор с тем же моментом не двигает <see cref="Entity.UpdatedAt"/>,
    /// потому что реального изменения нет. Более ранний момент не откатывает
    /// <see cref="LastSeenAt"/> назад — часы разных инстансов не должны «старить» токен.
    /// </remarks>
    /// <param name="seenAt">Момент подтверждения в UTC.</param>
    public UnitResult<Failure> Touch(DateTimeOffset seenAt)
    {
        if (seenAt == default)
        {
            return DomainErrors.Fail(Error.Validation(
                NOW_INVALID_CODE,
                "The registration time cannot be default.",
                nameof(seenAt)));
        }

        if (seenAt <= LastSeenAt)
        {
            return DomainErrors.Ok();
        }

        LastSeenAt = seenAt;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Передаёт токен другому пользователю: устройство сменило аккаунт.
    /// </summary>
    /// <remarks>
    /// Токен принадлежит устройству, а не кабинету: после входа другого аккаунта push
    /// прежнему владельцу был бы утечкой чужого визита. Поэтому передача — это явное
    /// действие репозитория при <c>ON CONFLICT</c>, а не ошибка уникальности.
    /// </remarks>
    /// <param name="userId">Новый владелец устройства.</param>
    /// <param name="seenAt">Момент передачи в UTC.</param>
    public UnitResult<Failure> TransferTo(Guid userId, DateTimeOffset seenAt)
    {
        if (userId == Guid.Empty)
        {
            return DomainErrors.Fail(Error.Validation(
                USER_INVALID_CODE,
                "Push device token owner cannot be empty.",
                nameof(userId)));
        }

        if (seenAt == default)
        {
            return DomainErrors.Fail(Error.Validation(
                NOW_INVALID_CODE,
                "The registration time cannot be default.",
                nameof(seenAt)));
        }

        UserId = userId;
        LastSeenAt = seenAt;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Приводит токен к каноническому виду: пробелы по краям срезаются, пустая строка
    /// означает «токена нет» и хранится как <c>null</c> до фабричной проверки.
    /// </summary>
    private static string? NormalizeToken(string? token)
        => string.IsNullOrWhiteSpace(token) ? null : token.Trim();
}
