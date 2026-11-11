using System.Net;
using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.EmailChanges;

/// <summary>
/// Текст письма подтверждения нового адреса электронной почты.
/// </summary>
/// <remarks>
/// <para>
/// Письмо уходит на <b>новый</b> адрес: только его владелец может подтвердить, что адрес
/// принадлежит ему. Только русский язык, без картинок и трекинга: HTML-версия и текстовый
/// дубль. Открытый токен — только внутри ссылки.
/// </para>
/// <para>
/// Имя пользователя вставляется в приветствие только через <see cref="WebUtility.HtmlEncode"/>:
/// имя приходит от клиента и не должно ломать разметку.
/// </para>
/// </remarks>
public static class EmailChangeConfirmationMessage
{
    /// <summary>
    /// Тема письма подтверждения нового адреса.
    /// </summary>
    public const string SUBJECT = "Подтвердите новый адрес электронной почты — BarberSearch";

    /// <summary>
    /// Собирает письмо со ссылкой подтверждения смены адреса.
    /// </summary>
    /// <param name="to">Адрес получателя — запрошенный новый адрес.</param>
    /// <param name="firstName">Имя пользователя для приветствия.</param>
    /// <param name="newEmail">Запрошенный адрес — для текста письма.</param>
    /// <param name="link">Абсолютная ссылка подтверждения; собирается в одном месте.</param>
    /// <param name="lifetimeHours">Срок жизни ссылки в часах — для текста письма.</param>
    public static EmailMessage Create(
        string to,
        string firstName,
        string newEmail,
        string link,
        int lifetimeHours)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(link);

        var encodedName = WebUtility.HtmlEncode(firstName);
        var encodedNewEmail = WebUtility.HtmlEncode(newEmail);
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
                <p>В вашем аккаунте BarberSearch запрошена смена адреса электронной почты на
                <b>{encodedNewEmail}</b>. Подтвердите новый адрес — после этого вход будет
                работать только с ним.</p>
                <p style="margin:32px 0;">
                  <a href="{encodedLink}"
                     style="display:inline-block;background:#1f1f1f;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;">
                    Подтвердить новый адрес
                  </a>
                </p>
                <p>Ссылка действует {lifetimeHours} часа. Если вы не запрашивали смену адреса,
                просто проигнорируйте это письмо — текущий адрес останется прежним, а на него
                мы отправили отдельное уведомление.</p>
                <p style="color:#666666;font-size:14px;">BarberSearch</p>
              </div>
            </body>
            </html>
            """;

        var text = $"""
            {greetingText}

            В вашем аккаунте BarberSearch запрошена смена адреса электронной почты на
            {newEmail}. Подтвердите новый адрес — после этого вход будет работать только с ним.

            Подтвердить новый адрес: {link}

            Ссылка действует {lifetimeHours} часа. Если вы не запрашивали смену адреса,
            просто проигнорируйте это письмо — текущий адрес останется прежним, а на него
            мы отправили отдельное уведомление.

            BarberSearch
            """;

        return new EmailMessage(to, SUBJECT, html, text);
    }
}
