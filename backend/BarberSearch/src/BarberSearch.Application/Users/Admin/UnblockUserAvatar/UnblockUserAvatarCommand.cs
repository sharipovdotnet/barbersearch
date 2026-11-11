using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.Admin.UnblockUserAvatar;

/// <summary>
/// Команда возврата скрытого администратором аватара пользователя.
/// </summary>
/// <remarks>
/// Возврат не меняет URL и файл: снимается только флаг, поэтому показывается тот же кадр.
/// Повторный вызов идемпотентен.
/// </remarks>
/// <param name="UserId">Пользователь, чей аватар возвращается.</param>
public record UnblockUserAvatarCommand(Guid UserId) : ICommand;
