using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.Admin.BlockUserAvatar;

/// <summary>
/// Команда административного скрытия аватара пользователя.
/// </summary>
/// <remarks>
/// Скрытие — маска на чтении, а не удаление: URL и файл остаются, поэтому «Вернуть фото»
/// показывает тот же кадр. Повторный вызов идемпотентен. Скрывать нечего, если аватар
/// не загружен (<c>409 user.blockAvatar.noAvatar</c>).
/// </remarks>
/// <param name="UserId">Пользователь, чей аватар скрывается.</param>
public record BlockUserAvatarCommand(Guid UserId) : ICommand;
