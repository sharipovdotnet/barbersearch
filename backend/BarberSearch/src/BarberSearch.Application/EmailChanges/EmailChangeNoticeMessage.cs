using System.Net;
using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.EmailChanges;

/// <summary>
/// Уведомление на <b>текущий</b> адрес о том, что запрошена его смена.
/// </summary>
/// <remarks>
/// <para>
/// В письме нет ни токена, ни ссылки подтверждения: уведомление только сообщает о заявке и
/// подсказывает, что делать, если её отправили не вы. Подтверждать по нему нечего — это
/// защита владельца старого адреса, а не второй канал подтверждения.
/// </para>
/// <para>
/// Имя пользователя вставляется только через <see cref="WebUtility.HtmlEncode"/>.
/// </para>
/// </remarks>
public static class EmailChangeNoticeMessage
{
    /// <summary>
    /// Тема уведомления о запрошенной смене адреса.
    /// </summary>
    public const string SUBJECT = "Запрошена смена адреса электронной почты — BarberSearch";

    /// <summary>
    /// Собирает уведомление на старый адрес.
    /// </summary>
    /// <param name="to">Текущий адрес получателя.</param>
    /// <param name="firstName">Имя пользователя для приветствия.</param>
    /// <param name="newEmail">Запрошенный новый адрес.</param>
    public static EmailMessage Create(string to, string firstName, string newEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        var encodedName = WebUtility.HtmlEncode(firstName);
        var encodedNewEmail = WebUtility.HtmlEncode(newEmail);

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
                <b>{encodedNewEmail}</b>.</p>
                <p>Новый адрес станет адресом входа только после перехода по ссылке из письма,
                которое мы отправили на него. Пока ссылка не подтверждена, вход работает с
                текущим адресом и прежним паролем.</p>
                <p>Если вы не отправляли заявку, возможно, кто-то получил доступ к вашей сессии.
                Смените пароль в личном кабинете и напишите в поддержку — смена адреса не
                произойдёт без подтверждения с нового почтового ящика.</p>
                <p style="color:#666666;font-size:14px;">BarberSearch</p>
              </div>
            </body>
            </html>
            """;

        var text = $"""
            {greetingText}

            В вашем аккаунте BarberSearch запрошена смена адреса электронной почты на
            {newEmail}.

            Новый адрес станет адресом входа только после перехода по ссылке из письма,
            которое мы отправили на него. Пока ссылка не подтверждена, вход работает с
            текущим адресом и прежним паролем.

            Если вы не отправляли заявку, возможно, кто-то получил доступ к вашей сессии.
            Смените пароль в личном кабинете и напишите в поддержку — смена адреса не
            произойдёт без подтверждения с нового почтового ящика.

            BarberSearch
            """;

        return new EmailMessage(to, SUBJECT, html, text);
    }
}
