using BarberSearch.Domain.BarberRequests;

namespace BarberSearch.Contracts.BarberRequests.Admin;

/// <summary>
/// Заявка в барберы в администраторском списке и карточке.
/// </summary>
/// <remarks>
/// Один тип на список и карточку: администратору нужны одни и те же поля в обоих местах,
/// а отдельная «лёгкая» строка списка сэкономила бы несколько байт ценой второго контракта
/// и второго маппинга. Вложенный <see cref="Applicant"/> — единственное отличие от
/// <c>BarberRequestResponse</c>; PII живёт только здесь, за политикой <c>AdminOnly</c>.
/// </remarks>
/// <param name="Id">Идентификатор заявки.</param>
/// <param name="Status">Статус заявки.</param>
/// <param name="DisplayName">Имя, под которым заявитель хочет работать.</param>
/// <param name="YearsOfExperience">Опыт работы в годах.</param>
/// <param name="City">Город.</param>
/// <param name="About">Рассказ о себе либо <c>null</c>.</param>
/// <param name="HasSalon">Уже работает в салоне.</param>
/// <param name="CreatedAt">Когда заявка подана.</param>
/// <param name="ReviewedAt">Когда принято решение либо <c>null</c>.</param>
/// <param name="RejectionReason">Причина отказа либо <c>null</c>.</param>
/// <param name="ReviewedByUserId">Администратор, принявший решение, либо <c>null</c>.</param>
/// <param name="Applicant">Аккаунт заявителя.</param>
public record AdminBarberRequestResponse(
    Guid Id,
    BarberRequestStatus Status,
    string DisplayName,
    int YearsOfExperience,
    string City,
    string? About,
    bool HasSalon,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt,
    string? RejectionReason,
    Guid? ReviewedByUserId,
    AdminBarberRequestApplicantResponse Applicant);
