using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.Admin.RejectUserAvatar;

/// <summary>
/// Команда отклонения аватара пользователя, ждущего проверки.
/// </summary>
/// <remarks>
/// Причина обязательна: она показывается владельцу в кабинете, чтобы он понял, что не так.
/// Домен тримит её и ограничивает 500 символами:
/// <c>400 user.avatarModeration.reasonRequired</c> / <c>.reasonTooLong</c> с
/// <c>invalidField = "reason"</c>. Повторное решение — <c>409
/// user.avatarModeration.invalidStatus</c>, аккаунт без URL — <c>409
/// user.avatarModeration.noAvatar</c>.
/// </remarks>
/// <param name="UserId">Пользователь, чей аватар отклоняется.</param>
/// <param name="Reason">Причина отказа; обрезается доменом по краям.</param>
public record RejectUserAvatarCommand(Guid UserId, string? Reason) : ICommand;
