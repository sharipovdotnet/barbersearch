using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Abstractions;

/// <summary>
/// Отправка транзакционного письма.
/// </summary>
/// <remarks>
/// <para>
/// Абстракция живёт в Application, реализация — в <c>BarberSearch.Infrastructure.Email</c>
/// (<c>SmtpEmailSender</c>): слой Application не знает ни про SMTP, ни про настройки
/// транспорта. Готовность почты для обработчиков — отдельный порт
/// <see cref="IEmailSenderAvailability"/>; здесь остаётся только отправка.
/// </para>
/// <para>
/// Единственный вызывающий — диспетчер outbox. Обработчики запросов письма не отправляют:
/// они кладут строку в outbox в своей транзакции.
/// </para>
/// <para>
/// Ошибка отправки — <c>Error.Failure</c>: код и сообщение не содержат ни адреса получателя,
/// ни тела письма.
/// </para>
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
