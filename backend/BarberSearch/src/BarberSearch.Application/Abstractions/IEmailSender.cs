using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Abstractions;

/// <summary>
/// Отправка транзакционного письма.
/// </summary>
/// <remarks>
/// Абстракция живёт в Application, реализация — в Web (<c>SmtpEmailSender</c>): слой
/// Application не знает ни про SMTP, ни про настройки транспорта. Ошибка отправки —
/// <c>Error.Failure</c> с публичным кодом <c>auth.emailVerification.sendFailed</c>; адрес
/// получателя и текст письма в сообщение ошибки не попадают.
/// </remarks>
public interface IEmailSender
{
    /// <summary>
    /// Отправляет письмо.
    /// </summary>
    /// <param name="message">Готовое письмо: адресат, тема, HTML и текст.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Успех либо <c>Failure</c> с кодом сбоя отправки.</returns>
    Task<UnitResult<Failure>> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default);
}
