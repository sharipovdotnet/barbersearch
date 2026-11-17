using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push;

/// <summary>
/// Доступ к очереди push-уведомлений: вставка с дедупликацией, пачка доставки и уборка.
/// </summary>
/// <remarks>
/// <para>
/// Дедупликация — обязанность хранилища: уникальный индекс <c>ux_push_notifications_booking_id</c>
/// по тройке <c>(booking_id, kind, start_at)</c> и <c>INSERT … ON CONFLICT DO NOTHING</c> делают
/// повторный тик и второй экземпляр приложения безопасными, а напоминание, событие создания и
/// события статуса одной брони сосуществуют. <c>start_at</c> в ключе отличает повторный перенос
/// на новое время от дубля: каждый новый целевой момент — отдельное событие.
/// <see cref="TryAddAsync"/> сообщает, вставлена ли строка, — по этому признаку обработчик
/// считает созданные уведомления.
/// </para>
/// <para>
/// Пачка доставки <b>забирается</b> атомарно (<c>FOR UPDATE SKIP LOCKED</c> и аренда в
/// <c>next_attempt_at</c>): два экземпляра не должны отправить одно сообщение дважды.
/// Конкретный механизм — забота реализации, обработчик считает пачку своей.
/// </para>
/// </remarks>
public interface IPushNotificationRepository
{
    /// <summary>
    /// Пытается вставить уведомление, не нарушая дедупликацию по
    /// <c>(booking_id, kind, start_at)</c>.
    /// </summary>
    /// <param name="notification">Новое уведомление в состоянии <c>Pending</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если строка вставлена; <c>false</c>, если такое уведомление уже есть.</returns>
    Task<Result<bool, Failure>> TryAddAsync(
        PushNotification notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Пачка уведомлений к доставке: <c>Pending</c>, меньше пяти попыток, время попытки наступило.
    /// </summary>
    /// <remarks>
    /// Условие: <c>delivery = 0 AND delivery_attempts &lt; 5 AND (next_attempt_at IS NULL OR
    /// next_attempt_at &lt;= now)</c>. Порядок — <c>created_at</c>, чтобы старое не голодало.
    /// </remarks>
    /// <param name="now">Текущий момент UTC — граница <c>next_attempt_at</c>.</param>
    /// <param name="batchSize">Размер пачки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<PushNotification>, Failure>> GetPendingForDeliveryAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет переходы доставки (отправлено, пропущено, ошибка).
    /// </summary>
    /// <param name="notification">Отслеживаемая сущность.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> UpdateAsync(
        PushNotification notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет уведомления старше момента отсечения (<c>Push:RetentionDays</c>).
    /// </summary>
    /// <param name="cutoff">Граница ретенции: всё, что создано раньше, удаляется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сколько строк удалено.</returns>
    Task<Result<int, Failure>> DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}
