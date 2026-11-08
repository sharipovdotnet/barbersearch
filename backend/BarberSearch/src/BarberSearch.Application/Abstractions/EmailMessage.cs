namespace BarberSearch.Application.Abstractions;

/// <summary>
/// Готовое письмо для транспорта: адресат, тема и два тела.
/// </summary>
/// <remarks>
/// Application не знает про SMTP: транспорт выбирает слой Web. HTML и текстовый дубль
/// нужны одновременно — клиент без HTML должен прочитать то же письмо.
/// </remarks>
/// <param name="To">Адрес получателя.</param>
/// <param name="Subject">Тема письма.</param>
/// <param name="HtmlBody">HTML-версия тела.</param>
/// <param name="TextBody">Текстовая версия тела для <c>text/plain</c>.</param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody);
