namespace BarberSearch.IntegrationTests;

/// <summary>
/// Одно перехваченное письмо: адресат, тема и оба тела.
/// </summary>
/// <param name="To">Адрес получателя.</param>
/// <param name="Subject">Тема письма.</param>
/// <param name="HtmlBody">HTML-версия.</param>
/// <param name="TextBody">Текстовая версия.</param>
public sealed record CapturedEmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody);
