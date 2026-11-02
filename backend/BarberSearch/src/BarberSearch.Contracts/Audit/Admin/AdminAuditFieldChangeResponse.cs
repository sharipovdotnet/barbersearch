using System.Text.Json;

namespace BarberSearch.Contracts.Audit.Admin;

/// <summary>
/// Одно изменённое поле в записи журнала.
/// </summary>
/// <remarks>
/// <see cref="From"/> и <see cref="To"/> — JSON-скаляры (<c>string</c>, <c>number</c>,
/// <c>boolean</c>) либо <c>null</c>. Для создания и удаления <see cref="From"/> равен
/// <c>null</c>, а <see cref="To"/> — записанное значение: сравнивать нечего.
/// Секреты приходят строкой <c>"[redacted]"</c>, длинный текст — с маркером <c>...</c>:
/// так их пишет <c>AuditTrail</c>, и чтение журнала эти значения не раскрывает.
/// </remarks>
/// <param name="Field">Имя поля в camelCase, как его записал аудит.</param>
/// <param name="From">Значение до изменения; <c>null</c> для создания, удаления и пустого значения.</param>
/// <param name="To">Значение после изменения; <c>null</c> для удаления и пустого значения.</param>
public record AdminAuditFieldChangeResponse(string Field, JsonElement? From, JsonElement? To);
