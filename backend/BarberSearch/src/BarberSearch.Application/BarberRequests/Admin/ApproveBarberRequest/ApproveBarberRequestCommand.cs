using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberRequests.Admin.ApproveBarberRequest;

/// <summary>
/// Команда одобрения заявки на роль барбера.
/// </summary>
/// <remarks>
/// Администратор берётся из claim'а текущего пользователя, а не из тела: иначе автор решения
/// в ответе и в журнале не совпадал бы с тем, кто действительно нажал «Одобрить».
/// </remarks>
/// <param name="RequestId">Идентификатор заявки.</param>
public record ApproveBarberRequestCommand(Guid RequestId) : ICommand;
