using BarberSearch.Application.Push;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Перехватывающий push-канал: запоминает сообщения и отдаёт заданный исход.
/// </summary>
/// <remarks>
/// Настоящий FCM в интеграционных тестах не вызывается. <see cref="IsConfigured"/> = <c>false</c>
/// включает режим разработки: обработчик доставки собирает текст и помечает строку
/// <c>Skipped</c> с причиной «push is not configured».
/// </remarks>
public sealed class CapturingPushSender : IPushSender
{
    private readonly List<CapturedPushMessage> _messages = [];

    public IReadOnlyList<CapturedPushMessage> Messages => _messages;

    /// <summary>
    /// Настроен ли канал; <c>false</c> — режим разработки без ключей.
    /// </summary>
    public bool IsConfigured { get; set; } = true;

    /// <summary>
    /// Успешен ли ответ транспорта; <c>false</c> — временная ошибка отправки.
    /// </summary>
    public bool Succeed { get; set; } = true;

    public Task<UnitResult<Failure>> SendAsync(
        PushMessage message,
        string token,
        CancellationToken cancellationToken = default)
    {
        _messages.Add(new CapturedPushMessage(token, message));

        return Task.FromResult(Succeed
            ? UnitResult.Success<Failure>()
            : UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Push.Send,
                "Push send failed (HTTP 502)."))));
    }
}

/// <summary>
/// Одно перехваченное push-сообщение с токеном устройства.
/// </summary>
/// <param name="Token">Токен, на который отправляли.</param>
/// <param name="Message">Заголовок, текст и визит.</param>
public sealed record CapturedPushMessage(string Token, PushMessage Message);
