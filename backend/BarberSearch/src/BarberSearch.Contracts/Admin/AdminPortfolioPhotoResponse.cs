using BarberSearch.Domain.Common;

namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Строка очереди «Портфолио» на экране <c>/admin/photos</c>: работа барбера, ждущая проверки.
/// </summary>
/// <remarks>
/// Только <see cref="PhotoModerationStatus.Pending"/>-фотографии, FIFO по времени создания
/// (<c>created_at</c>): отдельной метки поступления у портфолио нет, кадр создаётся сразу
/// в очередь. URL и подпись отдаются сырыми — администратор проверяет сам кадр, а не его
/// публичную маску.
/// <para>
/// <see cref="IsPublished"/> — намерение владельца, а не допуск: отклонённый или ещё не
/// проверенный кадр не публикуется независимо от флага. <see cref="OwnerEmail"/> — email
/// владельца профиля, персональные данные админского чтения.
/// </para>
/// </remarks>
/// <param name="PhotoId">Идентификатор фотографии портфолио.</param>
/// <param name="BarberProfileId">Профиль барбера, которому принадлежит кадр.</param>
/// <param name="BarberDisplayName">Отображаемое имя мастера.</param>
/// <param name="OwnerEmail">Email владельца профиля.</param>
/// <param name="Url">Сырой URL проверяемого кадра.</param>
/// <param name="Description">Подпись к кадру либо <c>null</c>.</param>
/// <param name="IsPublished">Опубликована ли фотография владельцем.</param>
/// <param name="ModerationStatus">Состояние проверки; в очереди всегда <c>Pending</c>.</param>
/// <param name="SubmittedAt">
/// Когда кадр создан и поступил в очередь: метка <c>created_at</c>, второй ключ курсора
/// вместе с <see cref="PhotoId"/>.
/// </param>
public record AdminPortfolioPhotoResponse(
    Guid PhotoId,
    Guid BarberProfileId,
    string BarberDisplayName,
    string OwnerEmail,
    string Url,
    string? Description,
    bool IsPublished,
    PhotoModerationStatus ModerationStatus,
    DateTimeOffset SubmittedAt);
