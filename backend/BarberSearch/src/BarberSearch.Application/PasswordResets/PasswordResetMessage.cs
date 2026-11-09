using System.Net;
using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PasswordResets;

/// <summary>
/// Текст письма со ссылкой для сброса пароля.
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
/// <para>
/// Ссылку собирает вызывающий: message не знает про <c>App:PublicUrl</c> и не дублирует
/// правило «ровно один <c>/reset-password?token=…</c>».
/// </para>
/// </remarks>
public static class PasswordResetMessage
{
    /// <summary>
    /// Тема письма сброса пароля.
    /// </summary>
    public const string SUBJECT = "Сброс пароля — BarberSearch";

    /// <summary>
    /// Собирает письмо со ссылкой для сброса пароля.
    /// </summary>
    /// <param name="to">Адрес получателя.</param>
    /// <param name="firstName">Имя пользователя для приветствия.</param>
    /// <param name="link">Абсолютная ссылка на форму нового пароля; собирается в одном месте.</param>
    /// <param name="lifetimeMinutes">Срок жизни ссылки в минутах — для текста письма.</param>
    public static EmailMessage Create(
        string to,
        string firstName,
        string link,
        int lifetimeMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(link);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lifetimeMinutes);

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
                <p>Вы запросили сброс пароля на BarberSearch. Нажмите кнопку ниже, чтобы задать
                новый пароль.</p>
                <p style="margin:32px 0;">
                  <a href="{encodedLink}"
                     style="display:inline-block;background:#1f1f1f;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;">
                    Задать новый пароль
                  </a>
                </p>
                <p>Ссылка действует {lifetimeMinutes} минут. Если вы не запрашивали сброс,
                просто проигнорируйте письмо — пароль останется прежним.</p>
                <p style="color:#666666;font-size:14px;">BarberSearch</p>
              </div>
            </body>
            </html>
            """;

        var text = $"""
            {greetingText}

            Вы запросили сброс пароля на BarberSearch. Нажмите кнопку ниже, чтобы задать
            новый пароль.

            Задать новый пароль: {link}

            Ссылка действует {lifetimeMinutes} минут. Если вы не запрашивали сброс, просто
            проигнорируйте письмо — пароль останется прежним.

            BarberSearch
            """;

        return new EmailMessage(to, SUBJECT, html, text);
    }
}
