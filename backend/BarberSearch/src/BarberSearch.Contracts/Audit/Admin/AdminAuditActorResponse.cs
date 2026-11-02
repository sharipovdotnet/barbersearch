namespace BarberSearch.Contracts.Audit.Admin;

/// <summary>
/// Автор изменения: кто нажал кнопку. Идентификатор исторический, а не ссылка:
/// профиль автора мог быть удалён, тогда <see cref="Email"/> равен <c>null</c>.
/// </summary>
/// <param name="Id">Идентификатор пользователя-автора.</param>
/// <param name="Email">Email автора на момент чтения журнала; <c>null</c>, если аккаунт удалён.</param>
public record AdminAuditActorResponse(Guid Id, string? Email);
