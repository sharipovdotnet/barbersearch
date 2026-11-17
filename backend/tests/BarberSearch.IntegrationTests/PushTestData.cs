using BarberSearch.Domain.Push;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Сидирование и чтение push-данных через реальный <see cref="ApplicationDbContext"/>.
/// </summary>
/// <remarks>
/// Токен создаётся доменной фабрикой, уведомления читаются без кэша контекста: проверяется
/// то, что реально легло в PostgreSQL, а не состояние трекера.
/// </remarks>
internal sealed class PushTestData
{
    private readonly TestApp _app;

    public PushTestData(TestApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _app = app;
    }

    /// <summary>
    /// Регистрирует токен устройства за пользователем.
    /// </summary>
    public async Task SeedDeviceTokenAsync(
        Guid userId,
        string token,
        DateTimeOffset? lastSeenAt = null)
    {
        var device = PushDeviceToken.Register(
            userId,
            token,
            PushPlatform.Android,
            lastSeenAt ?? DateTimeOffset.UtcNow).Value;

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.PushDeviceTokens.Add(device);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Все push-напоминания пользователя в порядке создания.
    /// </summary>
    public async Task<IReadOnlyList<PushNotification>> GetNotificationsAsync(Guid userId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.PushNotifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Все push-уведомления таблицы: нужно сценариям, где получателя нет (гостевая бронь)
    /// или проверяется «не создано ни одной строки».
    /// </summary>
    public async Task<IReadOnlyList<PushNotification>> GetAllNotificationsAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.PushNotifications
            .AsNoTracking()
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Все токены пользователя без кэша контекста.
    /// </summary>
    public async Task<IReadOnlyList<PushDeviceToken>> GetDeviceTokensAsync(Guid userId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.PushDeviceTokens
            .AsNoTracking()
            .Where(device => device.UserId == userId)
            .OrderByDescending(device => device.LastSeenAt)
            .ThenByDescending(device => device.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Число токенов пользователя.
    /// </summary>
    public async Task<int> CountDeviceTokensAsync(Guid userId)
    {
        var rows = await GetDeviceTokensAsync(userId);

        return rows.Count;
    }
}
