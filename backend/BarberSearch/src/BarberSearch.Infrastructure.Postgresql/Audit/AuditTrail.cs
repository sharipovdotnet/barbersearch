using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Reviews;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Specializations;
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
/// Аудируются девять типов: <see cref="User"/>, <see cref="BarberProfile"/>,
/// <see cref="PortfolioPhoto"/>, <see cref="BarberService"/>, <see cref="Review"/>,
/// <see cref="Salon"/>, <see cref="HairService"/>, <see cref="Specialization"/> и
/// <see cref="Booking"/>, и только свойства из allowlist. У брони пишутся только изменения
/// (<see cref="Booking.Status"/> и <see cref="Booking.BarberNote"/>): создание брони —
/// действие клиента, а строка аудита на каждую бронь раздувала бы журнал без новой
/// информации, которую бронь уже не хранит. Логины (<c>LastLoginAt</c>,
/// <c>FailedLoginAttempts</c>) и правки профиля без административного смысла не пишутся:
/// иначе таблица превращается в шум, в котором не видно смены роли и скрытия отзыва.
/// Дети <see cref="BarberProfile"/> — салоны, расписание и отпуска — не отдельные агрегаты
/// и в allowlist не входят: их правки строки не дают. Исключений два: <see cref="PortfolioPhoto"/>
/// (загрузка, скрытие и удаление кадра — жизненный цикл контента, который администратор
/// проверяет и блокирует) и <see cref="BarberService"/> (прайс: кто и когда поставил цену) —
/// у них собственные типы, хотя они остаются детьми профиля.
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
            // Поля согласия (#42) в allowlist не входят намеренно: журнал — история изменений,
            // а не реестр согласий. Добавление consentIp протащило бы IP (персональные данные)
            // в payload, который читают в админском журнале и экспорте. Запись согласия живёт
            // в users и отдаётся в карточке пользователя; отзыв и повторное согласие — отдельный
            // срез с собственной таблицей истории.
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
                    nameof(User.BarberProfileId),

                    // Скрытие/возврат аватара (#87): решение администратора о контенте,
                    // а не техническая правка. AvatarUrl добавлен вместе с
                    // AvatarModerationStatus: Id кадра и mediaId в URL — разные GUID'ы,
                    // поэтому без URL строка загрузки или удаления не называет файл.
                    // AvatarModerationStatus меняется при загрузке (Approved → Pending),
                    // так что строка появляется в любом случае — пусть в ней будет и URL.
                    // Причина отказа и reviewed-метки не входят: это свободный текст
                    // администратора и дубли occurred_at/actor_user_id.
                    nameof(User.AvatarUrl),
                    nameof(User.AvatarModerationStatus),
                    nameof(User.IsAvatarBlocked)
                ]),
            [typeof(BarberProfile)] = new(
                "barber_profile",
                [
                    nameof(BarberProfile.UserId),
                    nameof(BarberProfile.Grade),
                    nameof(BarberProfile.IsActive),
                    nameof(BarberProfile.IsVerified),
                    nameof(BarberProfile.Rating),
                    nameof(BarberProfile.ReviewsCount),

                    // Карточка (#96): текст, который читает клиент. Списки (языки, достижения,
                    // способы оплаты, формат общения, доп. услуги), специализации и стаж
                    // сознательно не входят: diff массива нечитаем, а payload раздувается.
                    // DisplayName не входит по другой причине (D4): это owned-тип, а не
                    // свойство, и при замене инстанса (UpdateProfile) EF теряет снимок —
                    // старая запись удаляется, новая приходит Added с OriginalValue ==
                    // CurrentValue, поэтому diff from/to построить нечем. Тихая дыра
                    // зафиксирована тестом Capture_WhenBarberProfileDisplayNameChanged_
                    // WritesNoRowBecauseOwnedSnapshotIsLost и §14 #30, а не молчанием.
                    nameof(BarberProfile.Bio),
                    nameof(BarberProfile.LatePolicy)
                ]),

            // Прайс барбера (#96): цена, длительность и видимость строки в карточке. Это
            // ребёнок агрегата, но, как и PortfolioPhoto, получает собственный тип:
            // администратор различает «профиль» и «прайс», и фильтр журнала должен тоже.
            // AuditsCreation true: добавление услуги — редкое осмысленное событие (десятки
            // строк на профиль), а без created нельзя ответить «когда услуга появилась».
            // Deleted не бывает: DELETE в API деактивирует строку (isActive), потому что
            // на неё ссылаются брони, — событие, которого в домене не было, не выдумываем.
            [typeof(BarberService)] = new(
                "barber_service",
                [
                    nameof(BarberService.BarberProfileId),
                    nameof(BarberService.HairServiceId),
                    nameof(BarberService.Price),
                    nameof(BarberService.DurationMinutes),
                    nameof(BarberService.IsActive)
                ]),

            // Портфолио — второй (после прайса) ребёнок BarberProfile в allowlist: загрузка
            // и удаление кадра — действие с контентом, а не техническая правка профиля.
            // AuditsCreation
            // остаётся true: кадров максимум десять на профиль, объём журнала это не взрывает.
            // Url обязателен: PortfolioPhoto.Id и mediaId в /api/media/portfolio/... — разные
            // GUID'ы, поэтому после удаления строку иначе не связать с файлом.
            // RejectionReason, ReviewedAt/ReviewedByUserId и ThumbnailUrl не входят: свободный
            // текст администратора, дубли occurred_at/actor_user_id и производная файла.
            [typeof(PortfolioPhoto)] = new(
                "portfolio_photo",
                [
                    nameof(PortfolioPhoto.BarberProfileId),
                    nameof(PortfolioPhoto.Url),
                    nameof(PortfolioPhoto.Description),
                    nameof(PortfolioPhoto.SortOrder),
                    nameof(PortfolioPhoto.IsPublished),
                    nameof(PortfolioPhoto.IsBlocked),
                    nameof(PortfolioPhoto.ModerationStatus)
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
                ]),

            // Салон: имя попадает в values строки создания (переименования салона нет),
            // контакты и координаты администратор правит существующими ручками, выключение
            // пишет isActive. ModerationStatus добавлен в #48: без него решение администратора
            // (одобрение/отказ) не видно в журнале, хотя пишется как salon.updated.
            // Kind/IsListed добавлены в #54: тип места и видимость в каталоге задаются только
            // при создании, поэтому попадают в values строки salon.created — там же, где имя.
            // RejectionReason сознательно не в allowlist: это текст администратора, а не
            // история изменения строки; OwnerUserId тоже не нужен — владелец не PII, но
            // и diff по нему бесполезен. Дети салона (BarberSalon, WorkingSchedule) —
            // другие агрегаты, их правки сюда не относятся.
            [typeof(Salon)] = new(
                "salon",
                [
                    nameof(Salon.Name),
                    nameof(Salon.Address),
                    nameof(Salon.City),
                    nameof(Salon.PostalCode),
                    nameof(Salon.Country),
                    nameof(Salon.TimeZoneId),
                    nameof(Salon.Latitude),
                    nameof(Salon.Longitude),
                    nameof(Salon.PhoneNumber),
                    nameof(Salon.WebsiteUrl),
                    nameof(Salon.IsActive),
                    nameof(Salon.Kind),
                    nameof(Salon.IsListed),
                    nameof(Salon.ModerationStatus)
                ]),

            // Услуга каталога: Update меняет имя, описание и длительность, а категория
            // задаётся только при создании — она нужна в values, а не в changes.
            // Переименование audit'ится как updated с name from/to.
            [typeof(HairService)] = new(
                "hair_service",
                [
                    nameof(HairService.Name),
                    nameof(HairService.Description),
                    nameof(HairService.Category),
                    nameof(HairService.DefaultDurationMinutes),
                    nameof(HairService.IsActive)
                ]),

            // Специализация: Rename меняет имя и описание, активация — isActive.
            // Переименование аудируется как updated с name from/to.
            [typeof(Specialization)] = new(
                "specialization",
                [
                    nameof(Specialization.Name),
                    nameof(Specialization.Description),
                    nameof(Specialization.IsActive)
                ]),

            // Бронь: Status, BarberNote, CancellationReason и время визита. Перенос меняет
            // только StartAt/EndAt, поэтому строка booking.updated несёт старый и новый момент
            // (from/to в ISO), а подписи к ним есть в админском журнале. Price/SalonId/
            // BarberServiceId/UserId/BarberProfileId по-прежнему не меняются ни одной
            // операцией — правка цены остаётся отдельным срезом и расширит allowlist вместе
            // с ней. IdempotencyKey не аудируется никогда: это технический ключ клиента,
            // а не история изменения.
            // AuditsCreation: false — создание брони не пишется сознательно: бронь уже хранит
            // владельца, время и цену, а строка на каждую бронь (1,2 млн в load-базе) раздувает
            // audit_log без новой информации. Журнал отвечает на «кто отменил/подтвердил/
            // отметил неявку/перенёс и когда», а не на «кто забронировал».
            [typeof(Booking)] = new(
                "booking",
                [
                    nameof(Booking.Status),
                    nameof(Booking.BarberNote),
                    nameof(Booking.CancellationReason),
                    nameof(Booking.StartAt),
                    nameof(Booking.EndAt)
                ],
                AuditsCreation: false)
        };

    /// <summary>
    /// Стабильные имена всех аудируемых типов — единый источник для guard-теста, который
    /// сверяет allowlist с реестром фильтра <c>entityType</c>. Имена совпадают
    /// с <c>AdminAuditEntityTypes.ToStorageName</c> по построению.
    /// </summary>
    internal static IReadOnlyCollection<string> EntityTypeNames
        => AUDITED_ENTITIES.Values
            .Select(policy => policy.EntityType)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

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
    /// Строит строку аудита для одной записи change tracker. <c>null</c> — писать нечего:
    /// у <c>Modified</c> не изменилось ни одно свойство allowlist, либо тип создаётся
    /// с <see cref="AuditedEntity.AuditsCreation"/> = <c>false</c> (создание брони).
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

        // Флаг политики, а не случайная ветка: создание брони — действие клиента, и строка
        // на каждую бронь не даёт журналу новой информации (см. комментарий allowlist).
        if (entry.State == EntityState.Added && !policy.AuditsCreation)
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
    /// Правило аудита одного типа: стабильное имя, allowlist свойств и признак
    /// «писать ли строку создания».
    /// </summary>
    /// <param name="EntityType">Стабильное snake_case-имя типа в <c>audit_log</c>.</param>
    /// <param name="Properties">Свойства, которые имеют административный смысл.</param>
    /// <param name="AuditsCreation">
    /// <c>false</c> — создание сущности строки не пишет. У брони это осознанное решение:
    /// бронь хранит владельца, время и цену, а журнал фиксирует изменения.
    /// </param>
    private sealed record AuditedEntity(
        string EntityType,
        IReadOnlyList<string> Properties,
        bool AuditsCreation = true);
}
