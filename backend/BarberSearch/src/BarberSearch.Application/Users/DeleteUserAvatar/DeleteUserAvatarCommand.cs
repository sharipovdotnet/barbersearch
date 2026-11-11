using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.DeleteUserAvatar;

/// <summary>
/// Команда удаления аватара текущего пользователя.
/// </summary>
/// <remarks>
/// Идентификатор пользователя приходит из claim'а <c>sub</c>, а не из команды: удалить можно
/// только собственный аватар. Параметров нет — операция идемпотентна.
/// </remarks>
public record DeleteUserAvatarCommand : ICommand;
