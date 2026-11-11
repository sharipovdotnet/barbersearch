using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.Admin.ApproveUserAvatar;

/// <summary>
/// Команда одобрения аватара пользователя, ждущего проверки.
/// </summary>
/// <remarks>
/// Одобрение — решение о самом кадре, а не о публикации: публично аватар появится по обычным
/// правилам (если он не скрыт администратором). Повторное решение по уже проверенному кадру —
/// <c>409 user.avatarModeration.invalidStatus</c>, а аккаунт без URL —
/// <c>409 user.avatarModeration.noAvatar</c>; оба кода возвращает домен.
/// </remarks>
/// <param name="UserId">Пользователь, чей аватар одобряется.</param>
public record ApproveUserAvatarCommand(Guid UserId) : ICommand;
