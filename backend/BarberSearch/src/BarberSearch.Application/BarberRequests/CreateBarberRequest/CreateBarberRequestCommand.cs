using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;

namespace BarberSearch.Application.BarberRequests.CreateBarberRequest;

/// <summary>
/// Команда подачи заявки на роль барбера.
/// </summary>
/// <remarks>
/// Роль в команде не передаётся: заявка всегда создаётся от текущего пользователя и только
/// в статусе <c>Pending</c>. Данные приходят одним контрактом, чтобы поля нельзя было
/// перепутать местами.
/// </remarks>
public record CreateBarberRequestCommand(CreateBarberRequestRequest Request) : ICommand;
