using BarberSearch.Application.Push;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Фейковый push-канал: запоминает вызовы и отдаёт заранее заданный исход.
/// </summary>
/// <remarks>
/// <see cref="OnSend"/> позволяет различить устройства в одном тике: один токен может быть
/// мёртвым, второй — рабочим. Текст сохраняется целиком: тест проверяет, что в нём нет
/// персональных данных клиента и что токен не попадает в журнал.
/// </remarks>
public sealed class FakePushSender : IPushSender
{
    private readonly List<(string Token, PushMessage Message)> _calls = [];

    public IReadOnlyList<(string Token, PushMessage Message)> Calls => _calls;

    /// <summary>
    /// Настроен ли канал; <c>false</c> — режим разработки.
    /// </summary>
    public bool IsConfigured { get; set; } = true;

    /// <summary>
    /// Исход следующей отправки; по умолчанию — успех.
    /// </summary>
    public UnitResult<Failure> Result { get; set; } = UnitResult.Success<Failure>();

    /// <summary>
    /// Исход для конкретного токена; имеет приоритет над <see cref="Result"/>.
    /// </summary>
    public Func<string, UnitResult<Failure>>? OnSend { get; set; }

    public Task<UnitResult<Failure>> SendAsync(
        PushMessage message,
        string token,
        CancellationToken cancellationToken = default)
    {
        _calls.Add((token, message));

        return Task.FromResult(OnSend?.Invoke(token) ?? Result);
    }

    /// <summary>
    /// Исход «токен мёртв»: FCM ответил <c>404 UNREGISTERED</c>.
    /// </summary>
    public static Failure TokenUnregistered()
        => new(Error.Failure(
            ApiErrorCodes.Push.TokenUnregistered,
            "Push token is not registered (HTTP 404)."));

    /// <summary>
    /// Исход «временная ошибка»: сеть или <c>5xx</c>, попытку нужно повторить.
    /// </summary>
    public static Failure TemporaryFailure()
        => new(Error.Failure(
            ApiErrorCodes.Push.Send,
            "Push send failed (HTTP 502)."));
}
