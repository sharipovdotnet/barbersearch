namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Строка узкого чтения аватара пользователя: <c>avatar_url</c> либо <c>null</c>.
/// </summary>
/// <remarks>
/// Отдельный тип, а не <c>string?</c>: EF Core иначе не отличил бы отсутствие пользователя
/// (нет строки) от пользователя без аватара (<c>NULL</c> в колонке). В Application тип
/// не выходит — репозиторий разворачивает его в <c>Result&lt;string?, Failure&gt;</c>.
/// </remarks>
/// <param name="AvatarUrl">Сохранённый URL аватара либо <c>null</c>.</param>
internal sealed record UserAvatarUrlLookup(string? AvatarUrl);
