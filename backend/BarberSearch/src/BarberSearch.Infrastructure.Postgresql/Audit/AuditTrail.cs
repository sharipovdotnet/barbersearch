using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Reviews;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BarberSearch.Infrastructure.Postgresql.Audit;

/// <summary>
/// Собирает строки аудита из change tracker перед сохранением изменений (R-34).
/// </summary>
/// <remarks>
/// <para>
/// Точка перехвата — <c>RepositoryBase.SaveChangesAsync</c>: это единственное место, где
/// репозитории пишут в БД. Строки аудита добавляются в тот же <c>SaveChanges</c> и в ту же
/// транзакцию <c>IUnitOfWork</c>, поэтому либо сохраняются и изменение, и аудит, либо
/// откатываются оба.
/// </para>
/// <para>
/// Web-фильтр не подходит: он пишет после коммита, не видит старых значений и может потерять
/// запись, если запись в БД уже прошла. EF-интерсептор не подходит: контекст регистрируется
/// через <c>AddDbContextPool</c>, а интерсептор <c>SaveChanges</c> создаётся один раз на пул
/// и не может получить scoped <see cref="ICurrentUser"/>.
/// </para>
/// <para>
/// Аудируются только <see cref="User"/>, <see cref="BarberProfile"/> и <see cref="Review"/>,
/// и только свойства из allowlist. Логины (<c>LastLoginAt</c>, <c>FailedLoginAttempts</c>)
/// и правки профиля без административного смысла не пишутся: иначе таблица превращается
/// в шум, в котором не видно смены роли и скрытия отзыва.
/// </para>
/// <para>
/// Секреты не попадают в БД в открытом виде: значения свойств, имя которых содержит
/// <c>password</c>, <c>hash</c>, <c>stamp</c>, <c>token</c> или <c>secret</c>, заменяются
/// на <c>"[redacted]"</c>. Строки длиннее
/// <see cref="AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH"/> обрезаются с маркером.
/// </para>
/// </remarks>
public sealed class AuditTrail
{
    private const string REDACTED = "[redacted]";
    private const string TRUNCATION_MARKER = "...";

    /// <summary>
    /// Фрагменты имени свойства, по которым значение считается секретом.
    /// </summary>
    private static readonly string[] SECRET_NAME_MARKERS =
        ["password", "hash", "stamp", "token", "secret"];

    /// <summary>
    /// Настройки JSON для payload: camelCase и enum'ы строками. Кириллица не экранируется
    /// в <c>\uXXXX</c> — payload в БД должен читаться глазами при разборе инцидента.
    /// </summary>
    private static readonly JsonSerializerOptions PAYLOAD_JSON_OPTIONS = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Allowlist аудируемых типов: стабильное имя сущности и свойства, которые имеют
    /// административный смысл. Всё остальное (метки времени, <c>xmin</c>, коллекции,
    /// счётчики входов) игнорируется.
    /// </summary>
    private static readonly Dictionary<Type, AuditedEntity> AUDITED_ENTITIES =
        new()
        {
            [typeof(User)] = new(
                "user",
                [
                    nameof(User.Email),
                    nameof(User.Role),
                    nameof(User.IsActive),
                    nameof(User.IsEmailVerified),
                    nameof(User.IsPhoneVerified),
                    nameof(User.PasswordHash),
                    nameof(User.SecurityStamp),
                    nameof(User.BarberProfileId)
                ]),
            [typeof(BarberProfile)] = new(
                "barber_profile",
                [
                    nameof(BarberProfile.UserId),
                    nameof(BarberProfile.Grade),
                    nameof(BarberProfile.IsActive),
                    nameof(BarberProfile.IsVerified),
                    nameof(BarberProfile.Rating),
                    nameof(BarberProfile.ReviewsCount)
                ]),
            [typeof(Review)] = new(
                "review",
                [
                    nameof(Review.BookingId),
                    nameof(Review.BarberProfileId),
                    nameof(Review.UserId),
                    nameof(Review.Rating),
                    nameof(Review.Text),
                    nameof(Review.IsPublished)
                ])
        };

    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Создаёт сервис аудита.
    /// </summary>
    /// <param name="currentUser">Текущий пользователь запроса (actor).</param>
    /// <param name="timeProvider">Часы: момент аудита управляем в тестах.</param>
    public AuditTrail(ICurrentUser currentUser, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Добавляет в контекст строки аудита для изменённых сущностей allowlist.
    /// </summary>
    /// <remarks>
    /// Метод ничего не сохраняет: строки попадают в БД тем же <c>SaveChanges</c>, что и само
    /// изменение. Повторный вызов на уже сохранённом контексте не создаёт дублей — после
    /// <c>SaveChanges</c> сущности снова <c>Unchanged</c> и в выборку не попадают.
    /// </remarks>
    /// <param name="context">Контекст, изменения которого сохраняются.</param>
    internal void Capture(ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entries = context.ChangeTracker
            .Entries()
            .Where(entry => entry.State is EntityState.Added
                or EntityState.Modified
                or EntityState.Deleted)
            .Where(entry => AUDITED_ENTITIES.ContainsKey(entry.Metadata.ClrType))
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        // Одно время на всю пачку: строки, собранные за один SaveChanges, должны быть
        // согласованы между собой.
        var occurredAt = _timeProvider.GetUtcNow();
        var actorUserId = _currentUser.TryGetUserId(out var userId) ? userId : (Guid?)null;

        var rows = new List<AuditLogEntry>(entries.Count);

        foreach (var entry in entries)
        {
            var policy = AUDITED_ENTITIES[entry.Metadata.ClrType];
            var row = CreateRow(entry, policy, occurredAt, actorUserId);

            if (row is not null)
            {
                rows.Add(row);
            }
        }

        if (rows.Count != 0)
        {
            context.Set<AuditLogEntry>().AddRange(rows);
        }
    }

    /// <summary>
    /// Строит строку аудита для одной записи change tracker. <c>null</c> — писать нечего
    /// (например, у <c>Modified</c> не изменилось ни одно свойство allowlist).
    /// </summary>
    private static AuditLogEntry? CreateRow(
        EntityEntry entry,
        AuditedEntity policy,
        DateTimeOffset occurredAt,
        Guid? actorUserId)
    {
        if (entry.Property("Id").CurrentValue is not Guid entityId)
        {
            return null;
        }

        var action = entry.State switch
        {
            EntityState.Added => $"{policy.EntityType}.created",
            EntityState.Modified => $"{policy.EntityType}.updated",
            EntityState.Deleted => $"{policy.EntityType}.deleted",
            _ => null
        };

        if (action is null)
        {
            return null;
        }

        var payload = entry.State == EntityState.Modified
            ? CreateChangesPayload(entry, policy)
            : CreateValuesPayload(entry, policy);

        if (payload is null)
        {
            return null;
        }

        return AuditLogEntry.Create(
            occurredAt,
            actorUserId,
            action,
            policy.EntityType,
            entityId,
            payload);
    }

    /// <summary>
    /// Payload создания и удаления: <c>{"values":{…}}</c> из allowlist-свойств.
    /// </summary>
    private static string? CreateValuesPayload(EntityEntry entry, AuditedEntity policy)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var propertyName in policy.Properties)
        {
            if (entry.Metadata.FindProperty(propertyName) is null)
            {
                continue;
            }

            values[ToCamelCase(propertyName)] = Normalize(
                entry.Property(propertyName).CurrentValue,
                propertyName);
        }

        if (values.Count == 0)
        {
            return null;
        }

        return Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["values"] = values
        });
    }

    /// <summary>
    /// Payload изменения: <c>{"changes":{"role":{"from":…,"to":…}}}</c> только по реально
    /// изменённым свойствам allowlist.
    /// </summary>
    /// <remarks>
    /// Сравнение старых и новых значений обязательно вместе с <c>IsModified</c>: отсоединённый
    /// граф, присоединённый через <c>Context.Update</c>, помечает изменёнными все свойства,
    /// хотя значения не менялись. Без сравнения каждое сохранение давало бы ложную запись.
    /// </remarks>
    private static string? CreateChangesPayload(EntityEntry entry, AuditedEntity policy)
    {
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var propertyName in policy.Properties)
        {
            if (entry.Metadata.FindProperty(propertyName) is null)
            {
                continue;
            }

            var property = entry.Property(propertyName);

            if (!property.IsModified)
            {
                continue;
            }

            var from = property.OriginalValue;
            var to = property.CurrentValue;

            if (Equals(from, to))
            {
                continue;
            }

            changes[ToCamelCase(propertyName)] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["from"] = Normalize(from, propertyName),
                ["to"] = Normalize(to, propertyName)
            };
        }

        if (changes.Count == 0)
        {
            return null;
        }

        return Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["changes"] = changes
        });
    }

    private static string Serialize(Dictionary<string, object?> payload)
        => JsonSerializer.Serialize(payload, PAYLOAD_JSON_OPTIONS);

    /// <summary>
    /// Приводит значение свойства к JSON-примитиву: секреты скрываются, длинные строки
    /// обрезаются, enum'ы становятся строками, идентификаторы — строками.
    /// </summary>
    private static object? Normalize(object? value, string propertyName)
    {
        if (value is null)
        {
            return null;
        }

        if (IsSecret(propertyName))
        {
            return REDACTED;
        }

        return value switch
        {
            string text => Truncate(text),
            bool or int or long or decimal or double or float => value,
            Guid guid => guid.ToString(),
            Enum enumeration => enumeration.ToString(),
            DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
            _ => Truncate(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
        };
    }

    /// <summary>
    /// Считается ли свойство секретом по имени (без учёта регистра).
    /// </summary>
    private static bool IsSecret(string propertyName)
        => SECRET_NAME_MARKERS.Any(
            marker => propertyName.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Обрезает строку до <see cref="AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH"/> символов,
    /// оставляя место под маркер. Длина результата не превышает предел.
    /// </summary>
    private static string Truncate(string value)
    {
        if (value.Length <= AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH)
        {
            return value;
        }

        var keep = AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH - TRUNCATION_MARKER.Length;
        var truncated = value[..keep];

        // Не разрываем суррогатную пару: иначе в JSON попала бы невалидная UTF-16-строка.
        if (char.IsHighSurrogate(truncated[^1]))
        {
            truncated = truncated[..^1];
        }

        return truncated + TRUNCATION_MARKER;
    }

    private static string ToCamelCase(string propertyName)
        => JsonNamingPolicy.CamelCase.ConvertName(propertyName);

    /// <summary>
    /// Правило аудита одного типа: стабильное имя и allowlist свойств.
    /// </summary>
    private sealed record AuditedEntity(string EntityType, IReadOnlyList<string> Properties);
}
