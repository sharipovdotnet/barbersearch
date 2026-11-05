using BarberSearch.Domain.BarberRequests;

namespace BarberSearch.Contracts.BarberRequests;

/// <summary>
/// Заявка клиента на роль барбера в ответах заявителя и администратора.
/// </summary>
/// <remarks>
/// Контракт один для создания и чтения своей заявки: заявитель видит ровно те поля, которые
/// указал, плюс статус и решение администратора. Персональные данные другого человека сюда
/// не попадают — администраторский ответ добавляет их отдельно
/// (<c>AdminBarberRequestResponse</c>).
/// </remarks>
/// <param name="Id">Идентификатор заявки.</param>
/// <param name="Status">Статус: <c>Pending</c>, <c>Approved</c> или <c>Rejected</c>.</param>
/// <param name="DisplayName">Имя, под которым заявитель хочет работать.</param>
/// <param name="YearsOfExperience">Опыт работы в годах.</param>
/// <param name="City">Город.</param>
/// <param name="About">Рассказ о себе либо <c>null</c>.</param>
/// <param name="HasSalon">Уже работает в салоне.</param>
/// <param name="CreatedAt">Когда заявка подана.</param>
/// <param name="ReviewedAt">Когда принято решение либо <c>null</c>.</param>
/// <param name="RejectionReason">Причина отказа либо <c>null</c>.</param>
public record BarberRequestResponse(
    Guid Id,
    BarberRequestStatus Status,
    string DisplayName,
    int YearsOfExperience,
    string City,
    string? About,
    bool HasSalon,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt,
    string? RejectionReason);
