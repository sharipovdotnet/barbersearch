using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;

namespace BarberSearch.Application.BarberRequests.GetMyBarberRequest;

/// <summary>
/// Запрос своей последней заявки на роль барбера.
/// </summary>
/// <remarks>
/// Параметра с идентификатором пользователя нет: заявитель берётся из claim'а <c>sub</c>,
/// поэтому прочитать чужую заявку нечем. Запрос доступен любой роли — после одобрения
/// человек должен видеть статус.
/// </remarks>
public record GetMyBarberRequestQuery : IQuery<MyBarberRequestResponse>;
