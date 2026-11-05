using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberRequests.Admin;

namespace BarberSearch.Application.BarberRequests.Admin.GetAdminBarberRequest;

/// <summary>
/// Запрос одной заявки в барберы для администраторской карточки.
/// </summary>
/// <param name="RequestId">Идентификатор заявки.</param>
public record GetAdminBarberRequestQuery(Guid RequestId)
    : IQuery<AdminBarberRequestResponse>;
