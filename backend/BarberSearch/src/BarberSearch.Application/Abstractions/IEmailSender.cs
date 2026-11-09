using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Abstractions;

/// <summary>
/// Отправка транзакционного письма.
/// </summary>
/// <remarks>
/// Абстракция живёт в Application, реализация — в <c>BarberSearch.Infrastructure.Email</c>
/// (<c>SmtpEmailSender</c>): слой Application не знает ни про SMTP, ни про настройки
/// транспорта. Ошибка отправки — <c>Error.Failure</c> с публичным кодом
/// <c>auth.emailVerification.sendFailed</c>; адрес получателя и текст письма в сообщение
/// ошибки не попадают.
/// </remarks>
public interface IEmailSender
{
    /// <summary>
    /// Настроен ли транспорт: есть хост, порт и валидный адрес отправителя.
    /// </summary>
    /// <remarks>
    /// Проверка готовности почты для обработчиков: они решают, можно ли выпускать токен,
    /// не зная про <c>EmailOptions</c> и SMTP. Пустая секция <c>Email</c> — нормальное
    /// состояние, при котором <see cref="IsAvailable"/> равно <c>false</c>.
    /// </remarks>
    bool IsAvailable { get; }

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
