using BarberSearch.Domain.Common;

namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Строка очереди «Аватары» на экране <c>/admin/photos</c>: чей кадр ждёт проверки.
/// </summary>
/// <remarks>
/// Только <see cref="PhotoModerationStatus.Pending"/>-кадры, FIFO по времени поступления.
/// Аватар — поле пользователя, отдельной сущности фотографии у него нет, поэтому строка
/// описывает владельца и его текущий URL. URL отдаётся сырым: администратор должен видеть
/// кадр, который проверяет, — маска публичных чтений здесь не применяется.
/// <para>
/// <see cref="SubmittedAt"/> — техническая метка <c>users.avatar_submitted_at</c>, она же
/// второй ключ курсора вместе с <see cref="UserId"/>. Email — персональные данные, но это
/// админское чтение: без него поддержка не найдёт владельца кадра.
/// </para>
/// </remarks>
/// <param name="UserId">Идентификатор пользователя-владельца аватара.</param>
/// <param name="FirstName">Имя владельца.</param>
/// <param name="LastName">Фамилия владельца.</param>
/// <param name="Email">Email владельца.</param>
/// <param name="AvatarUrl">Сырой URL проверяемого кадра.</param>
/// <param name="ModerationStatus">Состояние проверки; в очереди всегда <c>Pending</c>.</param>
/// <param name="SubmittedAt">Когда кадр поступил в очередь проверки.</param>
public record AdminAvatarPhotoResponse(
    Guid UserId,
    string FirstName,
    string LastName,
    string Email,
    string AvatarUrl,
    PhotoModerationStatus ModerationStatus,
    DateTimeOffset SubmittedAt);
