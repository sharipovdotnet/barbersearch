using BarberSearch.Application.Push;
using BarberSearch.Domain;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IPushNotificationRepository"/> для тестов обработчиков.
/// </summary>
/// <remarks>
/// <see cref="TryAddAsync"/> повторяет уникальный индекс по тройке
/// <c>(booking_id, kind, start_at)</c>: повтор того же вида для того же визита и того же
/// времени возвращает <c>false</c>, поэтому тест «повторный тик не создаёт дубль» проверяет
/// поведение обработчика, а не отсутствие дедупликации. Напоминание, событие создания и
/// события статуса одной брони при этом сосуществуют, а повторный перенос на новое время
/// создаёт новую строку.
/// </remarks>
public sealed class FakePushNotificationRepository : IPushNotificationRepository
{
    private readonly List<PushNotification> _items = [];

    public IReadOnlyList<PushNotification> Items => _items;

    public int AddCalls { get; private set; }

    public int UpdateCalls { get; private set; }

    public DateTimeOffset? LastDeleteCutoff { get; private set; }

    /// <summary>
    /// Подмена поведения вставки — для проверки ошибки хранилища.
    /// </summary>
    public Func<PushNotification, UnitResult<Failure>>? OnAdd { get; set; }

    public void Seed(PushNotification notification) => _items.Add(notification);

    public Task<Result<bool, Failure>> TryAddAsync(
        PushNotification notification,
        CancellationToken cancellationToken = default)
    {
        AddCalls++;

        if (OnAdd is not null)
        {
            return Task.FromResult(Result.Failure<bool, Failure>(OnAdd(notification).Error));
        }

        if (_items.Any(candidate =>
                candidate.BookingId == notification.BookingId
                && candidate.Kind == notification.Kind
                && candidate.StartAt == notification.StartAt))
        {
            return Task.FromResult(Result.Success<bool, Failure>(false));
        }

        _items.Add(notification);

        return Task.FromResult(Result.Success<bool, Failure>(true));
    }

    public Task<Result<IReadOnlyList<PushNotification>, Failure>> GetPendingForDeliveryAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PushNotification> rows = _items
            .Where(notification =>
                notification.Delivery == PushDelivery.Pending
                && notification.DeliveryAttempts < Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS
                && (notification.NextAttemptAt is null || notification.NextAttemptAt <= now))
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .Take(batchSize)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<PushNotification>, Failure>(rows));
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        PushNotification notification,
        CancellationToken cancellationToken = default)
    {
        UpdateCalls++;

        return Task.FromResult(UnitResult.Success<Failure>());
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
