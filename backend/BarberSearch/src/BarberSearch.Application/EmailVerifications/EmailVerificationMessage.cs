using System.Net;
using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.EmailVerifications;

/// <summary>
/// Текст письма подтверждения адреса электронной почты.
/// </summary>
/// <remarks>
/// <para>
/// Письмо только на русском, без картинок и трекинга: HTML-версия и текстовый дубль.
/// Пароль, идентификатор пользователя и адрес получателя в тексте не появляются; открытый
/// токен — только внутри ссылки, в <c>href</c> и в текстовом дубле.
/// </para>
/// <para>
/// Имя пользователя вставляется в приветствие только через <see cref="WebUtility.HtmlEncode"/>:
/// имя приходит от клиента и не должно ломать разметку.
/// </para>
/// </remarks>
public static class EmailVerificationMessage
{
    /// <summary>
    /// Тема письма подтверждения.
    /// </summary>
    public const string SUBJECT = "Подтвердите адрес электронной почты — BarberSearch";

    /// <summary>
    /// Собирает письмо со ссылкой подтверждения.
    /// </summary>
    /// <param name="to">Адрес получателя.</param>
    /// <param name="firstName">Имя пользователя для приветствия.</param>
    /// <param name="link">Абсолютная ссылка подтверждения; собирается в одном месте.</param>
    /// <param name="lifetimeHours">Срок жизни ссылки в часах — для текста письма.</param>
    public static EmailMessage Create(
        string to,
        string firstName,
        string link,
        int lifetimeHours)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(link);

        var encodedName = WebUtility.HtmlEncode(firstName);
        var encodedLink = WebUtility.HtmlEncode(link);

        var greetingHtml = string.IsNullOrWhiteSpace(firstName)
            ? "<p>Здравствуйте!</p>"
            : $"<p>Здравствуйте, {encodedName}!</p>";

        var greetingText = string.IsNullOrWhiteSpace(firstName)
            ? "Здравствуйте!"
            : $"Здравствуйте, {firstName}!";

        var html = $"""
            <!DOCTYPE html>
            <html lang="ru">
            <body style="margin:0;padding:24px;background:#f6f6f6;font-family:Arial,Helvetica,sans-serif;color:#1f1f1f;">
              <div style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;padding:32px;">
                {greetingHtml}
                <p>Вы регистрировались на BarberSearch. Подтвердите адрес электронной почты —
                это нужно, чтобы мы знали, что адрес действительно ваш.</p>
                <p style="margin:32px 0;">
                  <a href="{encodedLink}"
                     style="display:inline-block;background:#1f1f1f;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;">
                    Подтвердить адрес
                  </a>
                </p>
                <p>Ссылка действует {lifetimeHours} часа. Если вы не регистрировались, просто
                проигнорируйте это письмо — без перехода по ссылке адрес не подтвердится.</p>
                <p style="color:#666666;font-size:14px;">BarberSearch</p>
              </div>
            </body>
            </html>
            """;

        var text = $"""
            {greetingText}

            Вы регистрировались на BarberSearch. Подтвердите адрес электронной почты —
            это нужно, чтобы мы знали, что адрес действительно ваш.

            Подтвердить адрес: {link}

            Ссылка действует {lifetimeHours} часа. Если вы не регистрировались, просто
            проигнорируйте это письмо — без перехода по ссылке адрес не подтвердится.

            BarberSearch
            """;

        return new EmailMessage(to, SUBJECT, html, text);
    }
}
