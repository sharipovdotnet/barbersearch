using System.Net;
using System.Text;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Push;
using BarberSearch.Domain.Push;

namespace BarberSearch.Application.Bookings;

/// <summary>
/// Письмо клиенту об отмене или переносе записи, сделанных в мобильном приложении.
/// </summary>
/// <remarks>
/// <para>
/// Письмо существует только там, где приложение молчит: push о своём действии в приложении
/// не создаётся, а история отмен в шторке не хранится. С веба тот же повод уходит push'ем,
/// поэтому письма там нет — действие и так видно на экране, с которого его сделали
/// (AGENT.md §20.11).
/// </para>
/// <para>
/// В письме ровно то, что клиент видит в карточке: салон, мастер, услуга и время. Ни имени,
/// ни телефона, ни заметки, ни причины отмены, ни цены: снимок визита их не несёт (152-ФЗ),
/// а расширять снимок ради письма — отдельная работа. Строки «если это были не вы» нет:
/// у письма нет канала поддержки, и совет «смените пароль» без него — тупик.
/// </para>
/// <para>
/// Отсутствующий снимок деградирует текст так же, как у push: без салона выпадает его строка
/// и суффикс темы. Слово «сегодня»/«завтра» и дата в родительном падеже считаются по поясу
/// салона — тем же правилом, что у события статуса, поэтому письмо и push говорят одно и то же.
/// </para>
/// </remarks>
public static class BookingChangeMessage
{
    /// <summary>
    /// Тема письма об отмене без снимка салона.
    /// </summary>
    public const string CANCELLED_SUBJECT = "Запись отменена";

    /// <summary>
    /// Тема письма о переносе без снимка салона.
    /// </summary>
    public const string RESCHEDULED_SUBJECT = "Запись перенесена";

    /// <summary>
    /// Первая строка письма об отмене.
    /// </summary>
    public const string CANCELLED_FIRST_LINE = "Вы отменили запись:";

    /// <summary>
    /// Первая строка письма о переносе.
    /// </summary>
    public const string RESCHEDULED_FIRST_LINE = "Вы перенесли запись:";

    /// <summary>
    /// Собирает письмо об отмене или переносе записи.
    /// </summary>
    /// <param name="to">Подтверждённый адрес получателя.</param>
    /// <param name="snapshot">Снимок салона, мастера, услуги и пояса; части могут отсутствовать.</param>
    /// <param name="startAt">Время визита: новое — при переносе, прежнее — при отмене.</param>
    /// <param name="kind">
    /// <see cref="PushNotificationKind.BookingCancelledByCustomer"/> или
    /// <see cref="PushNotificationKind.BookingRescheduledByCustomer"/>; другие виды не письмо.
    /// </param>
    /// <param name="now">Текущий момент в UTC — для слова «сегодня»/«завтра».</param>
    public static EmailMessage Create(
        string to,
        PushBookingSnapshot snapshot,
        DateTimeOffset startAt,
        PushNotificationKind kind,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentNullException.ThrowIfNull(snapshot);

        var (firstLine, subject) = kind switch
        {
            PushNotificationKind.BookingCancelledByCustomer =>
                (CANCELLED_FIRST_LINE, CANCELLED_SUBJECT),
            PushNotificationKind.BookingRescheduledByCustomer =>
                (RESCHEDULED_FIRST_LINE, RESCHEDULED_SUBJECT),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The kind is not a customer booking change.")
        };

        var salonLine = string.IsNullOrWhiteSpace(snapshot.SalonName)
            ? null
            : "Салон «" + snapshot.SalonName.Trim() + "»";

        if (salonLine is not null)
        {
            subject += " — " + salonLine;
        }

        var visit = PushMessages.ComposeVisitBody(snapshot, startAt, now);

        var text = new StringBuilder()
            .Append(firstLine).Append('\n')
            .Append(visit);

        if (salonLine is not null)
        {
            text.Append('\n').Append(salonLine);
        }

        var html = new StringBuilder()
            .Append("<!DOCTYPE html>\n")
            .Append("<html lang=\"ru\">\n")
            .Append("<body style=\"margin:0;padding:24px;background:#f6f6f6;font-family:Arial,Helvetica,sans-serif;color:#1f1f1f;\">\n")
            .Append("  <div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;padding:32px;\">\n")
            .Append("    <p>").Append(WebUtility.HtmlEncode(firstLine)).Append("</p>\n")
            .Append("    <p>").Append(WebUtility.HtmlEncode(visit)).Append("</p>\n");

        if (salonLine is not null)
        {
            html.Append("    <p>").Append(WebUtility.HtmlEncode(salonLine)).Append("</p>\n");
        }

        html
            .Append("    <p style=\"color:#666666;font-size:14px;\">BarberSearch</p>\n")
            .Append("  </div>\n")
            .Append("</body>\n")
            .Append("</html>");

        return new EmailMessage(to, subject, html.ToString(), text.ToString());
    }
}
