using BarberSearch.Application.Notifications;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBarberNotificationRepository"/> для тестов обработчиков.
/// </summary>
/// <remarks>
/// Главное, что повторяет фейк, — дедупликация: <see cref="TryAddAsync"/> возвращает
/// <c>false</c> на повторе визита для <c>Upcoming</c> и на повторе локальной даты для
/// <c>Digest</c>, как уникальные индексы в PostgreSQL. Поэтому тест «повторный тик не создаёт
/// дубль» проверяет поведение обработчика, а не отсутствие проверки.
/// </remarks>
public sealed class FakeBarberNotificationRepository : IBarberNotificationRepository
{
    private readonly List<BarberNotification> _items = [];

    public IReadOnlyList<BarberNotification> Items => _items;

    public int AddCalls { get; private set; }

    public int UpdateCalls { get; private set; }

    public int MarkAllReadCalls { get; private set; }

    /// <summary>
    /// Граница последней уборки: тест видит, что обработчик посчитал её от часов и ретенции,
    /// а не от системного времени.
    /// </summary>
    public DateTimeOffset? LastDeleteCutoff { get; private set; }

    /// <summary>
    /// Подмена поведения вставки — для проверки ошибки хранилища, которую в памяти не воспроизвести.
    /// </summary>
    public Func<BarberNotification, UnitResult<Failure>>? OnAdd { get; set; }

    public void Seed(BarberNotification notification) => _items.Add(notification);

    public Task<Result<IReadOnlyList<BarberNotification>, Failure>> GetPageAsync(
        Guid userId,
        BarberNotificationCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<BarberNotification> query = _items
            .Where(notification => notification.UserId == userId);

        if (cursor is { } position)
        {
            query = query.Where(notification =>
                notification.CreatedAt < position.CreatedAt
                || (notification.CreatedAt == position.CreatedAt
                    && notification.Id.CompareTo(position.Id) < 0));
        }

        IReadOnlyList<BarberNotification> rows = query
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(limit)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<BarberNotification>, Failure>(rows));
    }

    public Task<Result<int, Failure>> CountUnreadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var count = _items.Count(notification =>
            notification.UserId == userId && notification.ReadAt is null);

        return Task.FromResult(Result.Success<int, Failure>(count));
    }

    public Task<Result<BarberNotification, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var notification = _items.FirstOrDefault(item => item.Id == id);

        return Task.FromResult(
            notification is null
                ? Result.Failure<BarberNotification, Failure>(new Failure(Error.NotFound(
                    ApiErrorCodes.Notification.NotFound,
                    $"Notification '{id}' was not found.",
                    id)))
                : Result.Success<BarberNotification, Failure>(notification));
    }

    public Task<Result<bool, Failure>> TryAddAsync(
        BarberNotification notification,
        CancellationToken cancellationToken = default)
    {
        AddCalls++;

        if (OnAdd is not null)
        {
            return Task.FromResult(Result.Failure<bool, Failure>(OnAdd(notification).Error));
        }

        var duplicate = notification.Kind switch
        {
            BarberNotificationKind.Upcoming => _items.Any(item =>
                item.Kind == BarberNotificationKind.Upcoming
                && item.BookingId == notification.BookingId),
            BarberNotificationKind.Digest => _items.Any(item =>
                item.Kind == BarberNotificationKind.Digest
                && item.UserId == notification.UserId
                && item.DigestLocalDate == notification.DigestLocalDate),
            _ => false
        };

        if (duplicate)
        {
            return Task.FromResult(Result.Success<bool, Failure>(false));
        }

        _items.Add(notification);

        return Task.FromResult(Result.Success<bool, Failure>(true));
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        BarberNotification notification,
        CancellationToken cancellationToken = default)
    {
        UpdateCalls++;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<Result<int, Failure>> MarkAllReadAsync(
        Guid userId,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        MarkAllReadCalls++;

        var changed = 0;

        foreach (var notification in _items.Where(item =>
                     item.UserId == userId && item.ReadAt is null))
        {
            if (notification.MarkRead(readAt).IsSuccess)
            {
                changed++;
            }
        }

        return Task.FromResult(Result.Success<int, Failure>(changed));
    }

    public Task<Result<IReadOnlyList<BarberNotification>, Failure>> GetPendingForDeliveryAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BarberNotification> rows = _items
            .Where(notification =>
                notification.Delivery == BarberNotificationDelivery.Pending
                && notification.DeliveryAttempts
                    < Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS
                && (notification.NextAttemptAt is null || notification.NextAttemptAt <= now))
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .Take(batchSize)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<BarberNotification>, Failure>(rows));
    }

    public Task<Result<int, Failure>> DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        LastDeleteCutoff = cutoff;

        var removed = _items.RemoveAll(notification => notification.CreatedAt < cutoff);

        return Task.FromResult(Result.Success<int, Failure>(removed));
    }
}
