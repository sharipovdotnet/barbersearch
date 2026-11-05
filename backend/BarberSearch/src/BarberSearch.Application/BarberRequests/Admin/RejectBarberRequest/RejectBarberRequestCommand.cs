using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberRequests.Admin.RejectBarberRequest;

/// <summary>
/// Команда отклонения заявки на роль барбера.
/// </summary>
/// <remarks>
/// Причина обязательна и проверяется доменом: заявитель должен увидеть, почему ему отказали.
/// Администратор берётся из claim'а текущего пользователя.
/// </remarks>
/// <param name="RequestId">Идентификатор заявки.</param>
/// <param name="Reason">Причина отказа; до <c>500</c> символов.</param>
public record RejectBarberRequestCommand(Guid RequestId, string? Reason) : ICommand;
