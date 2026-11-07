using BarberSearch.Domain.Notifications;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Сидирование уведомлений и настроек через реальный <see cref="ApplicationDbContext"/>.
/// </summary>
/// <remarks>
/// Доменные фабрики и переходы, никаких SQL-вставок в обход модели: тест проверяет
/// поведение приложения, а не собственные INSERT'ы.
/// </remarks>
internal sealed class NotificationTestData
{
    private readonly TestApp _app;

    public NotificationTestData(TestApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _app = app;
    }

    /// <summary>
    /// Привязывает профиль барбера к его пользователю: тик напоминаний ищет брони через
    /// <c>barber_profiles.user_id</c>, а не через claim.
    /// </summary>
    public async Task LinkBarberProfileAsync(SeededBarber barber)
    {
        ArgumentNullException.ThrowIfNull(barber);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == barber.User.Id);
        Require(user.LinkBarberProfile(barber.Profile.Id));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Заполняет имя и телефон клиента — ПДн, которых не должно быть в тексте Telegram.
    /// </summary>
    public async Task SetCustomerProfileAsync(
        Guid userId,
        string firstName,
        string lastName,
        string phoneNumber)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == userId);
        Require(user.UpdateProfile(firstName, lastName, phoneNumber, avatarUrl: null));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Создаёт настройки напоминаний, при необходимости сразу с привязанным чатом.
    /// </summary>
    public async Task<BarberNotificationSettings> SeedSettingsAsync(
        Guid userId,
        TimeOnly? digestTime = null,
        int? leadMinutes = null,
        bool isEnabled = true,
        bool digestEnabled = true,
        long? chatId = null)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var settings = Require(BarberNotificationSettings.CreateDefault(userId));

        if (digestTime is not null
            || leadMinutes is not null
            || !isEnabled
            || !digestEnabled)
        {
            Require(settings.Update(
                isEnabled,
                digestEnabled,
                digestTime ?? new TimeOnly(BarberNotificationSettings.DEFAULT_DIGEST_HOUR, 0),
                leadMinutes ?? 30));
        }

        if (chatId is { } linkedChatId)
        {
            Require(settings.LinkTelegram(linkedChatId, DateTimeOffset.UtcNow));
        }

        db.BarberNotificationSettings.Add(settings);
        await db.SaveChangesAsync();

        return settings;
    }

    /// <summary>
    /// Читает настройки пользователя без кэша контекста.
    /// </summary>
    public async Task<BarberNotificationSettings?> FindSettingsAsync(Guid userId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.BarberNotificationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.UserId == userId);
    }

    /// <summary>
    /// Добавляет уведомление напрямую доменной фабрикой — для чужих и уже существующих строк.
    /// </summary>
    public async Task AddNotificationAsync(BarberNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.BarberNotifications.Add(notification);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Уведомления пользователя в порядке создания; <paramref name="kind"/> сужает выборку.
    /// </summary>
    public async Task<IReadOnlyList<BarberNotification>> GetNotificationsAsync(
        Guid userId,
        BarberNotificationKind? kind = null)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var query = db.BarberNotifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId);

        if (kind is { } only)
        {
            query = query.Where(notification => notification.Kind == only);
        }

        return await query
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Число уведомлений пользователя; <paramref name="kind"/> сужает счётчик.
    /// </summary>
    public async Task<int> CountNotificationsAsync(
        Guid userId,
        BarberNotificationKind? kind = null)
    {
        var rows = await GetNotificationsAsync(userId, kind);

        return rows.Count;
    }

    private static T Require<T>(Result<T, Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }

        return result.Value;
    }

    private static void Require(UnitResult<Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }
    }
}
