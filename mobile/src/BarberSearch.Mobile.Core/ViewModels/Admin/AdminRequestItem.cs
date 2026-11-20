using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Строка очереди заявок «Стать мастером»: заявитель, город, стаж, дата подачи и статус.
/// </summary>
/// <remarks>
/// <para>
/// Строка неизменяемая: список перечитывается при каждом появлении страницы и после решения,
/// поэтому строка не правится на месте, а заменяется новой. Кисти статуса не хранятся:
/// их выбирает разметка по <see cref="IsPending"/>, <see cref="IsApproved"/> и
/// <see cref="IsRejected"/>, чтобы тема перекрашивала бейдж без пересборки строки.
/// </para>
/// <para>
/// Даты показываются в поясе устройства, как у остальных служебных моментов: у заявки нет
/// салона, в поясе которого её читать. Текущий момент приходит параметром — так «год только
/// у не текущего года» проверяется без ожидания нового года.
/// </para>
/// </remarks>
public sealed partial class AdminRequestItem : ObservableObject
{
    /// <summary>
    /// Создаёт строку из ответа API.
    /// </summary>
    /// <param name="request">Заявка из администраторского списка.</param>
    /// <param name="now">Текущий момент для правила «год только не текущий».</param>
    public AdminRequestItem(AdminBarberRequestResponse request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        Id = request.Id;
        DisplayName = request.DisplayName;
        ApplicantName = request.Applicant.FullName;
        City = request.City;
        ExperienceText = BarberText.Experience(request.YearsOfExperience);
        SubmittedAtText = AccountText.Moment(request.CreatedAt, TimeZoneInfo.Local, now);
        StatusText = AdminRequestStatus.Text(request.Status);
        IsPending = request.Status == BarberRequestStatus.Pending;
        IsApproved = request.Status == BarberRequestStatus.Approved;
        IsRejected = request.Status == BarberRequestStatus.Rejected;

        MetaText = string.Join(" · ", City, ExperienceText, SubmittedAtText);
    }

    /// <summary>
    /// Идентификатор заявки — по нему открывается карточка.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Имя, под которым заявитель хочет работать.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Имя аккаунта заявителя: кто именно подал заявку.
    /// </summary>
    public string ApplicantName { get; }

    /// <summary>
    /// Город из заявки.
    /// </summary>
    public string City { get; }

    /// <summary>
    /// Стаж словами: «5 лет», «1 год».
    /// </summary>
    public string ExperienceText { get; }

    /// <summary>
    /// Когда заявка подана, в поясе устройства.
    /// </summary>
    public string SubmittedAtText { get; }

    /// <summary>
    /// Строка метаданных: город, стаж и дата подачи через точку.
    /// </summary>
    public string MetaText { get; }

    /// <summary>
    /// Подпись статуса: «На рассмотрении», «Одобрена» или «Отклонена».
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// Заявка ждёт решения: у неё есть действия в карточке.
    /// </summary>
    public bool IsPending { get; }

    /// <summary>
    /// Заявка одобрена: статус читается тоном «успех».
    /// </summary>
    public bool IsApproved { get; }

    /// <summary>
    /// Заявка отклонена: статус читается тоном «отказ».
    /// </summary>
    public bool IsRejected { get; }
}

/// <summary>
/// Подписи статусов заявки: одни на очередь и карточку.
/// </summary>
/// <remarks>
/// Значения API остаются английскими; русский текст берётся из каталога, чтобы очередь
/// и карточка не разошлись в словах. Неизвестный статус читается как «на рассмотрении»:
/// так он и ведёт себя — без решения.
/// </remarks>
internal static class AdminRequestStatus
{
    /// <summary>
    /// Подпись статуса заявки.
    /// </summary>
    /// <param name="status">Статус из ответа.</param>
    /// <returns>Русская подпись.</returns>
    public static string Text(BarberRequestStatus status) => status switch
    {
        BarberRequestStatus.Approved => Loc.T("Одобрена"),
        BarberRequestStatus.Rejected => Loc.T("Отклонена"),
        _ => Loc.T("На рассмотрении"),
    };
}
